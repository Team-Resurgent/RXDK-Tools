using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Interop;
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
    private XbdmFileReceiver? _receiver;
    private readonly ulong _expectedSize;
    private bool _disposed;
    private bool _failed;

    public XbdmReceiveComStream(
        XbdmConnection connection,
        XboxDragEntry entry,
        XboxDragTransferSession.StreamLease lease)
    {
        _connection = connection;
        _wirePath = entry.WirePath;
        _expectedSize = entry.Size;
        _lease = lease;
    }

    public int Read(IntPtr pv, int cb, IntPtr pcbRead)
    {
        if (_disposed || pv == IntPtr.Zero || cb < 0)
        {
            WriteCount(pcbRead, 0);
            return HResults.Fail;
        }

        if (_failed)
        {
            WriteCount(pcbRead, 0);
            return HResults.Fail;
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
                return HResults.Fail;
            }

            var buffer = new byte[cb];
            var read = _receiver!.Read(buffer.AsSpan());
            if (read > 0)
                Marshal.Copy(buffer, 0, pv, read);

            _lease.ReportBytes(read);
            WriteCount(pcbRead, read);
            if (read == 0)
            {
                if (_receiver.IsComplete)
                    _lease.MarkCompleted();
                else
                {
                    _failed = true;
                    return HResults.Fail;
                }

                return HResults.False;
            }

            if (_receiver.IsComplete)
                _lease.MarkCompleted();

            return HResults.Ok;
        }
        catch
        {
            _failed = true;
            WriteCount(pcbRead, 0);
            return HResults.Fail;
        }
    }

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
        WriteCount(pcbRead, 0);
        WriteCount(pcbWritten, 0);
        return HResults.NotImpl;
    }

    public int Commit(int grfCommitFlags) => HResults.Ok;

    public int Revert() => HResults.Ok;

    public int LockRegion(long libOffset, long cb, int dwLockType) => HResults.Ok;

    public int UnlockRegion(long libOffset, long cb, int dwLockType) => HResults.Ok;

    public int Stat(out NativeStatStg pstatstg, int grfStatFlag)
    {
        if (_disposed || _failed)
        {
            pstatstg = default;
            return HResults.Fail;
        }

        var size = _receiver?.TotalSize ?? _expectedSize;
        pstatstg = new NativeStatStg
        {
            type = 2,
            cbSize = (long)Math.Min(size, (ulong)long.MaxValue),
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

        _disposed = true;
        _receiver?.Dispose();
        _receiver = null;
        _lease.Dispose();
    }

    private bool EnsureReceiver()
    {
        if (_receiver != null)
            return true;

        try
        {
            OpenReceiver();
            return _receiver != null;
        }
        catch
        {
            _failed = true;
            return false;
        }
    }

    private void OpenReceiver()
    {
        _receiver = _connection.OpenFileReceiver(_wirePath);
        _receiver.Start();
    }

    private void ResetReceiver()
    {
        _receiver?.Dispose();
        _receiver = null;
        OpenReceiver();
    }

    private static void WriteCount(IntPtr ptr, int value)
    {
        if (ptr != IntPtr.Zero)
            Marshal.WriteInt32(ptr, value);
    }
}
