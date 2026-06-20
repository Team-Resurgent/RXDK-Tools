using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Interop;

// Native IStream vtable (HRESULT-returning). ComTypes.IStream uses void methods and
// must not be exposed to native callers — Explorer will AV on Read/Stat.
[ComImport]
[Guid("0000000c-0000-0000-c000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INativeComStream
{
    [PreserveSig]
    int Read(IntPtr pv, int cb, IntPtr pcbRead);

    [PreserveSig]
    int Write(IntPtr pv, int cb, IntPtr pcbWritten);

    [PreserveSig]
    int Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition);

    [PreserveSig]
    int SetSize(long libNewSize);

    [PreserveSig]
    int CopyTo(INativeComStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten);

    [PreserveSig]
    int Commit(int grfCommitFlags);

    [PreserveSig]
    int Revert();

    [PreserveSig]
    int LockRegion(long libOffset, long cb, int dwLockType);

    [PreserveSig]
    int UnlockRegion(long libOffset, long cb, int dwLockType);

    [PreserveSig]
    int Stat(out NativeStatStg pstatstg, int grfStatFlag);

    [PreserveSig]
    int Clone(out INativeComStream ppstm);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStatStg
{
    public IntPtr pwcsName;
    public int type;
    public long cbSize;
    public long mtime;
    public long ctime;
    public long atime;
    public int grfMode;
    public int grfLocksSupported;
    public Guid clsid;
    public int grfStateBits;
    public int reserved;
}
