using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Interop;

namespace Rxdk.XbShellExt.Shell;

/// <summary>
/// Serves Explorer's duplicate CFSTR_FILECONTENTS probes for a file that was already
/// transferred on this drag session. Avoids issuing another GETFILE on the shared XBDM
/// connection, which would block later files and can desync the protocol.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class CompletedDragComStream : INativeComStream, IDisposable
{
    private readonly ulong _size;
    private readonly string _wirePath;
    private readonly XboxDragTransferSession? _session;
    private int _released;

    public CompletedDragComStream(XboxDragEntry entry, XboxDragTransferSession? session = null)
    {
        _wirePath = entry.WirePath;
        _size = entry.Size;
        _session = session;
        _session?.NotifyComStreamHeld();
        ManagedTrace.Line($"TransferReplay path='{_wirePath}' size={_size}");
    }

    ~CompletedDragComStream() => ReleaseHold();

    public int Read(IntPtr pv, int cb, IntPtr pcbRead)
    {
        WriteCount(pcbRead, 0);
        return HResults.False;
    }

    public int Write(IntPtr pv, int cb, IntPtr pcbWritten)
    {
        WriteCount(pcbWritten, 0);
        return HResults.Ok;
    }

    public int Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        ulong target = dwOrigin switch
        {
            0 => (ulong)Math.Max(0, dlibMove),
            1 => 0UL,
            2 => _size,
            _ => 0UL,
        };

        if (plibNewPosition != IntPtr.Zero)
            Marshal.WriteInt64(plibNewPosition, (long)Math.Min(target, _size));

        return HResults.Ok;
    }

    public int SetSize(long libNewSize) => HResults.Ok;

    public int CopyTo(INativeComStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
    {
        WriteCount(pcbRead, 0);
        WriteCount(pcbWritten, 0);
        return HResults.Ok;
    }

    public int Commit(int grfCommitFlags) => HResults.Ok;

    public int Revert() => HResults.Ok;

    public int LockRegion(long libOffset, long cb, int dwLockType) => HResults.Ok;

    public int UnlockRegion(long libOffset, long cb, int dwLockType) => HResults.Ok;

    public int Stat(out NativeStatStg pstatstg, int grfStatFlag)
    {
        pstatstg = new NativeStatStg
        {
            type = 2,
            cbSize = (long)Math.Min(_size, (ulong)long.MaxValue),
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
        ReleaseHold();
        GC.SuppressFinalize(this);
    }

    private void ReleaseHold()
    {
        if (Interlocked.CompareExchange(ref _released, 1, 0) != 0)
            return;

        _session?.NotifyComStreamReleased();
    }

    private static void WriteCount(IntPtr ptr, int value)
    {
        if (ptr != IntPtr.Zero)
            Marshal.WriteInt32(ptr, value);
    }
}
