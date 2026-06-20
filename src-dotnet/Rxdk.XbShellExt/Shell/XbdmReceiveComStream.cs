using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Interop;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;
namespace Rxdk.XbShellExt.Shell;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class XbdmReceiveComStream : INativeComStream, IDisposable
{
    private const int SeekSet = 0;
    private const int SeekCur = 1;
    private const int SeekEnd = 2;

    private readonly XbdmConnection _connection;
    private readonly string _wirePath;
    private readonly XboxDragTransferSession.StreamLease _lease;
    private readonly SemaphoreSlim _getFileGate;
    private readonly XboxDragTransferSession _session;
    private XbdmFileReceiver? _receiver;
    private readonly ulong _expectedSize;
    private bool _gateHeld;
    private bool _disposed;
    private bool _failed;
    private bool _replayCompleted;
    private int _readFailureHr = HResults.ReadFault;

    public XbdmReceiveComStream(
        XbdmConnection connection,
        XboxDragEntry entry,
        XboxDragTransferSession.StreamLease lease,
        SemaphoreSlim getFileGate,
        XboxDragTransferSession session)
    {
        _connection = connection;
        _wirePath = entry.WirePath;
        _expectedSize = entry.Size;
        _lease = lease;
        _getFileGate = getFileGate;
        _session = session;
        session.RegisterStream(this);
    }

    internal string WirePath => _wirePath;

    internal void Abort()
    {
        if (_disposed)
            return;

        _failed = true;
        var position = _receiver?.Position ?? 0UL;
        var total = _receiver?.TotalSize ?? 0UL;
        _receiver?.Dispose();
        _receiver = null;
        ReleaseGetFileGate();
        ManagedTrace.Line(
            $"TransferAbort path='{_wirePath}' position={position}/{total}");
    }

    ~XbdmReceiveComStream() => Dispose();

    internal bool TryStart()
    {
        try
        {
            return EnsureReceiver();
        }
        catch
        {
            _failed = true;
            return false;
        }
    }

    public int Read(IntPtr pv, int cb, IntPtr pcbRead)
    {
        if (_disposed || pv == IntPtr.Zero || cb < 0)
        {
            WriteCount(pcbRead, 0);
            return HResults.Fail;
        }

        if (_replayCompleted)
        {
            WriteCount(pcbRead, 0);
            return HResults.False;
        }

        if (_failed)
        {
            WriteCount(pcbRead, 0);
            return HResults.ReadFault;
        }

        if (cb == 0)
        {
            WriteCount(pcbRead, 0);
            return HResults.Ok;
        }

        try
        {
            if (!EnsureReceiver())
            {
                _failed = true;
                WriteCount(pcbRead, 0);
                return _readFailureHr;
            }

            if (_replayCompleted)
            {
                WriteCount(pcbRead, 0);
                return HResults.False;
            }

            var buffer = new byte[cb];
            var read = _receiver!.Read(buffer.AsSpan());
            if (read > 0)
                Marshal.Copy(buffer, 0, pv, read);

            _lease.ReportCurrentFileProgress(_receiver!.Position, _receiver.TotalSize);
            if (read > 0)
            {
                ManagedTrace.Line(
                    $"TransferRead path='{_wirePath}' read={read} position={_receiver.Position}/{_expectedSize}");
            }

            WriteCount(pcbRead, read);
            if (read == 0)
            {
                if (_receiver.IsComplete)
                {
                    _session.RecordCompletedWirePath(_wirePath, this);
                    _lease.MarkCompleted();
                }
                else
                {
                    _failed = true;
                    ComErrorInfo.SetMessage($"The transfer ended early for '{_wirePath}'.");
                    WriteCount(pcbRead, 0);
                    return HResults.ReadFault;
                }

                return HResults.False;
            }

            if (_receiver.IsComplete)
            {
                _session.RecordCompletedWirePath(_wirePath, this);
                _lease.MarkCompleted();
            }

            return HResults.Ok;
        }
        catch (OperationCanceledException)
        {
            _failed = true;
            _lease.ReportTransferFailure(null, cancelled: true);
            WriteCount(pcbRead, 0);
            return HResults.ReadFault;
        }
        catch (Exception ex)
        {
            _failed = true;
            _readFailureHr = MapReadFailure(ex);
            _lease.ReportTransferFailure(ex.Message);
            WriteCount(pcbRead, 0);
            return _readFailureHr;
        }
    }

    private static int MapReadFailure(Exception ex) =>
        ex is XbdmException xbdm && xbdm.HResultCode == XbdmHResults.CannotAccess
            ? HResults.AccessDenied
            : HResults.ReadFault;

    public int Write(IntPtr pv, int cb, IntPtr pcbWritten)
    {
        WriteCount(pcbWritten, 0);
        return HResults.Ok;
    }

    public int Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        if (_disposed || _failed || _receiver == null)
        {
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64(plibNewPosition, 0);
            return HResults.False;
        }

        try
        {
            var target = dwOrigin switch
            {
                SeekSet => (ulong)dlibMove,
                SeekCur => _receiver.Position + (ulong)dlibMove,
                SeekEnd => _receiver.TotalSize + (ulong)dlibMove,
                _ => throw new ArgumentOutOfRangeException(nameof(dwOrigin)),
            };

            if (target > _receiver.TotalSize)
            {
                _failed = true;
                return HResults.Fail;
            }

            if (target < _receiver.Position)
            {
                ResetReceiver();
                if (target > 0 && _receiver != null)
                    _receiver.Skip(target);
            }
            else if (target > _receiver.Position)
            {
                _receiver.Skip(target - _receiver.Position);
            }

            if (plibNewPosition != IntPtr.Zero && _receiver != null)
                Marshal.WriteInt64(plibNewPosition, (long)_receiver.Position);

            return HResults.Ok;
        }
        catch
        {
            _failed = true;
            return HResults.Fail;
        }
    }

    public int SetSize(long libNewSize) => HResults.Ok;

    public int CopyTo(INativeComStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
    {
        if (_disposed || _failed || pstm == null)
        {
            WriteCount(pcbRead, 0);
            WriteCount(pcbWritten, 0);
            return HResults.Fail;
        }

        var buffer = new byte[8192];
        var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var bytesReadPtr = Marshal.AllocHGlobal(sizeof(int));
        var bytesWrittenPtr = Marshal.AllocHGlobal(sizeof(int));
        long totalRead = 0;
        long totalWritten = 0;
        try
        {
            var remaining = cb <= 0 ? long.MaxValue : cb;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(remaining, buffer.Length);
                var hr = Read(bufferHandle.AddrOfPinnedObject(), chunk, bytesReadPtr);
                var read = Marshal.ReadInt32(bytesReadPtr);
                if (read == 0)
                {
                    if (hr < 0)
                    {
                        _failed = true;
                        WriteCount(pcbRead, ClampCount(totalRead));
                        WriteCount(pcbWritten, ClampCount(totalWritten));
                        return hr;
                    }

                    break;
                }

                totalRead += read;
                remaining -= read;

                hr = pstm.Write(bufferHandle.AddrOfPinnedObject(), read, bytesWrittenPtr);
                var written = Marshal.ReadInt32(bytesWrittenPtr);
                if (hr < 0 || written <= 0)
                {
                    _failed = true;
                    WriteCount(pcbRead, ClampCount(totalRead));
                    WriteCount(pcbWritten, ClampCount(totalWritten));
                    return HResults.Fail;
                }

                totalWritten += written;
            }

            WriteCount(pcbRead, ClampCount(totalRead));
            WriteCount(pcbWritten, ClampCount(totalWritten));
            return totalRead > 0 || (_receiver?.IsComplete ?? false) ? HResults.Ok : HResults.False;
        }
        catch (OperationCanceledException)
        {
            _failed = true;
            _lease.ReportTransferFailure(null, cancelled: true);
            WriteCount(pcbRead, 0);
            WriteCount(pcbWritten, 0);
            return HResults.Fail;
        }
        catch (Exception ex)
        {
            _failed = true;
            _lease.ReportTransferFailure(ex.Message);
            WriteCount(pcbRead, 0);
            WriteCount(pcbWritten, 0);
            return HResults.Fail;
        }
        finally
        {
            Marshal.FreeHGlobal(bytesReadPtr);
            Marshal.FreeHGlobal(bytesWrittenPtr);
            bufferHandle.Free();
        }
    }

    public int Commit(int grfCommitFlags) => HResults.Ok;

    public int Revert() => HResults.Ok;

    public int LockRegion(long libOffset, long cb, int dwLockType) => HResults.Ok;

    public int UnlockRegion(long libOffset, long cb, int dwLockType) => HResults.Ok;

    public int Stat(out NativeStatStg pstatstg, int grfStatFlag)
    {
        if (_disposed)
        {
            pstatstg = default;
            return HResults.Fail;
        }

        // Keep a stable STATSTG after a read failure so Explorer does not treat the stream as invalid.
        var reportedSize = _expectedSize > 0
            ? _expectedSize
            : (_receiver?.TotalSize ?? 0UL);
        pstatstg = new NativeStatStg
        {
            type = 2,
            cbSize = (long)Math.Min(reportedSize, (ulong)long.MaxValue),
            grfMode = 0,
            pwcsName = IntPtr.Zero,
        };
        return HResults.Ok;
    }

    public int Clone(out INativeComStream ppstm)
    {
        ppstm = null!;
        return HResults.NotImpl;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_receiver != null)
        {
            ManagedTrace.Line(
                $"TransferDone path='{_wirePath}' catalogSize={_expectedSize} getFileSize={_receiver.TotalSize} " +
                $"bytesRead={_receiver.Position} complete={_receiver.IsComplete}");
        }

        _disposed = true;
        _receiver?.Dispose();
        _receiver = null;
        ReleaseGetFileGate();
        _lease.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool EnsureReceiver()
    {
        if (_replayCompleted)
            return true;

        if (_receiver != null)
            return true;

        if (_session.IsWirePathCompleted(_wirePath))
        {
            _replayCompleted = true;
            ManagedTrace.Line($"TransferReplay path='{_wirePath}' size={_expectedSize}");
            return true;
        }

        try
        {
            OpenReceiver();
            return _receiver != null || _replayCompleted;
        }
        catch (Exception ex)
        {
            _failed = true;
            _readFailureHr = MapReadFailure(ex);
            _lease.ReportTransferFailure(ex.Message);
            return false;
        }
    }

    private void OpenReceiver()
    {
        _getFileGate.Wait();
        _gateHeld = true;
        try
        {
            if (_session.IsWirePathCompleted(_wirePath))
            {
                _replayCompleted = true;
                ReleaseGetFileGate();
                ManagedTrace.Line($"TransferReplay path='{_wirePath}' size={_expectedSize}");
                return;
            }

            _receiver = _connection.OpenFileReceiver(_wirePath);
            _receiver.Start();
            ManagedTrace.Line(
                $"TransferOpen path='{_wirePath}' catalogSize={_expectedSize} getFileSize={_receiver.TotalSize}");
        }
        catch (Exception ex)
        {
            ReleaseGetFileGate();
            ManagedTrace.Line($"TransferOpen failed path='{_wirePath}' error={ex.Message}");
            throw;
        }
    }

    private void ReleaseGetFileGate()
    {
        if (!_gateHeld)
            return;

        _getFileGate.Release();
        _gateHeld = false;
    }

    private void ResetReceiver()
    {
        _receiver?.Dispose();
        _receiver = null;
        ReleaseGetFileGate();
        OpenReceiver();
    }

    private static void WriteCount(IntPtr ptr, int value)
    {
        if (ptr != IntPtr.Zero)
            Marshal.WriteInt32(ptr, value);
    }

    private static int ClampCount(long value) =>
        (int)Math.Min(value, int.MaxValue);
}
