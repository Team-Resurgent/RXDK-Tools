using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Interop;

namespace Rxdk.XbShellExt.Shell;

/// <summary>
/// Serves CFSTR_FILECONTENTS by reading a temp file downloaded from the console.
/// Downloads are serialized through <see cref="XboxDragTransferSession"/> so Explorer
/// never opens more than one XBDM connection at a time during paste.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class XbdmReceiveComStream : INativeComStream, IDisposable
{
    private const int SeekSet = 0;
    private const int SeekCur = 1;
    private const int SeekEnd = 2;

    private readonly XboxDragEntry _entry;
    private readonly XboxDragTransferSession.StreamLease _lease;
    private readonly XboxDragTransferSession _session;
    private FileStream? _fileStream;
    private readonly ulong _expectedSize;
    private bool _disposed;
    private bool _failed;
    private bool _downloadStarted;
    private int _readFailureHr = HResults.ReadFault;

    public XbdmReceiveComStream(
        XboxDragEntry entry,
        XboxDragTransferSession.StreamLease lease,
        XboxDragTransferSession session)
    {
        _entry = entry;
        _expectedSize = entry.Size;
        _lease = lease;
        _session = session;
        session.RegisterStream(this);
    }

    internal string WirePath => _entry.WirePath;

    internal void Abort()
    {
        if (_disposed)
            return;

        _failed = true;
        CloseFile();
        ManagedTrace.Line($"TransferAbort path='{_entry.WirePath}'");
    }

    internal void FailCancelled()
    {
        if (_disposed || _failed)
            return;

        _failed = true;
        CloseFile();
        ManagedTrace.Line($"TransferCancel path='{_entry.WirePath}'");
    }

    ~XbdmReceiveComStream() => Dispose();

    public int Read(IntPtr pv, int cb, IntPtr pcbRead)
    {
        if (_disposed || pv == IntPtr.Zero || cb < 0)
        {
            WriteCount(pcbRead, 0);
            return _session.IsCancelRequested ? HResults.Abort : HResults.Fail;
        }

        if (_failed)
        {
            WriteCount(pcbRead, 0);
            return _session.IsCancelRequested ? HResults.Abort : HResults.ReadFault;
        }

        if (cb == 0)
        {
            WriteCount(pcbRead, 0);
            return HResults.Ok;
        }

        try
        {
            _lease.ThrowIfCancelled();

            if (!EnsureFileOpen())
            {
                _failed = true;
                WriteCount(pcbRead, 0);
                return _readFailureHr;
            }

            var buffer = new byte[cb];
            var totalRead = _fileStream!.Read(buffer, 0, cb);
            if (totalRead > 0)
            {
                Marshal.Copy(buffer, 0, pv, totalRead);
                _lease.ReportCurrentFileProgress((ulong)_fileStream.Position, _expectedSize);
                ManagedTrace.Line(
                    $"TransferRead path='{_entry.WirePath}' read={totalRead} position={_fileStream.Position}/{_expectedSize}");
            }

            if (totalRead == 0)
            {
                if (_fileStream.Position >= (long)_expectedSize || _fileStream.Length <= _fileStream.Position)
                {
                    _session.RecordCompletedWirePath(_entry.WirePath, this);
                    _lease.MarkCompleted();
                }
                else
                {
                    _failed = true;
                    ComErrorInfo.SetMessage($"The transfer ended early for '{_entry.WirePath}'.");
                    WriteCount(pcbRead, 0);
                    return HResults.ReadFault;
                }

                WriteCount(pcbRead, 0);
                return HResults.False;
            }

            if (_fileStream.Position >= _fileStream.Length)
            {
                _session.RecordCompletedWirePath(_entry.WirePath, this);
                _lease.MarkCompleted();
            }

            WriteCount(pcbRead, totalRead);
            return HResults.Ok;
        }
        catch (OperationCanceledException)
        {
            _failed = true;
            WriteCount(pcbRead, 0);
            return HResults.Abort;
        }
        catch (Exception) when (_session.IsCancelRequested)
        {
            _failed = true;
            WriteCount(pcbRead, 0);
            return HResults.Abort;
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
        ex is Rxdk.Xbdm.XbdmException xbdm && xbdm.HResultCode == Rxdk.Xbdm.XbdmHResults.CannotAccess
            ? HResults.AccessDenied
            : HResults.ReadFault;

    public int Write(IntPtr pv, int cb, IntPtr pcbWritten)
    {
        WriteCount(pcbWritten, 0);
        return HResults.Ok;
    }

    public int Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        if (_disposed || _failed)
        {
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64(plibNewPosition, 0);
            return HResults.False;
        }

        try
        {
            if (!EnsureFileOpen())
                return HResults.Fail;

            var origin = dwOrigin switch
            {
                SeekSet => SeekOrigin.Begin,
                SeekCur => SeekOrigin.Current,
                SeekEnd => SeekOrigin.End,
                _ => throw new ArgumentOutOfRangeException(nameof(dwOrigin)),
            };

            var newPos = _fileStream!.Seek(dlibMove, origin);
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64(plibNewPosition, newPos);

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
            return totalRead > 0 || (_fileStream?.Position ?? 0) >= (long)_expectedSize
                ? HResults.Ok
                : HResults.False;
        }
        catch (OperationCanceledException)
        {
            _failed = true;
            WriteCount(pcbRead, 0);
            WriteCount(pcbWritten, 0);
            return HResults.Abort;
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

        var reportedSize = _expectedSize;
        if (_fileStream != null)
            reportedSize = (ulong)Math.Max(_fileStream.Length, (long)_expectedSize);

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

        if (_fileStream != null)
        {
            ManagedTrace.Line(
                $"TransferDone path='{_entry.WirePath}' catalogSize={_expectedSize} " +
                $"bytesRead={_fileStream.Position} length={_fileStream.Length}");
        }

        _disposed = true;
        CloseFile();
        _lease.Dispose();
        _session.NotifyComStreamReleased();
        GC.SuppressFinalize(this);
    }

    private bool EnsureFileOpen()
    {
        if (_fileStream != null)
            return true;

        if (_session.IsWirePathCompleted(_entry.WirePath))
        {
            try
            {
                var tempPath = _session.GetCachedTempPath(_entry.WirePath);
                if (tempPath == null)
                    return false;

                _fileStream = File.OpenRead(tempPath);
                ManagedTrace.Line($"TransferReplay path='{_entry.WirePath}' temp='{tempPath}'");
                return true;
            }
            catch (Exception ex)
            {
                _readFailureHr = MapReadFailure(ex);
                _lease.ReportTransferFailure(ex.Message);
                return false;
            }
        }

        if (_downloadStarted)
            return _fileStream != null;

        _downloadStarted = true;
        try
        {
            var tempPath = _session.EnsureDownloadedToTemp(_entry, _lease);
            _fileStream = File.OpenRead(tempPath);
            return true;
        }
        catch (Exception ex)
        {
            _readFailureHr = MapReadFailure(ex);
            _lease.ReportTransferFailure(ex.Message);
            return false;
        }
    }

    private void CloseFile()
    {
        _fileStream?.Dispose();
        _fileStream = null;
    }

    private static void WriteCount(IntPtr ptr, int value)
    {
        if (ptr != IntPtr.Zero)
            Marshal.WriteInt32(ptr, value);
    }

    private static int ClampCount(long value) =>
        (int)Math.Min(value, int.MaxValue);
}
