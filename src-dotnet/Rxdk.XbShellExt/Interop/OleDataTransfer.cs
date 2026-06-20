using System.Runtime.InteropServices.ComTypes;

namespace Rxdk.XbShellExt.Interop;

[ComImport]
[Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAsyncOperation
{
    [PreserveSig]
    int SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool fDoOpAsync);

    [PreserveSig]
    int GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool pfIsOpAsync);

    [PreserveSig]
    int StartOperation(nint pbcReserved);

    [PreserveSig]
    int InOperation([MarshalAs(UnmanagedType.Bool)] out bool pfInAsyncOp);

    [PreserveSig]
    int EndOperation(int hResult, nint pbcReserved, uint dwEffects);
}

internal static class OleConstants
{
    public const int DvaspectContent = 1;
    public const int TymedHglobal = 1;
    public const int TymedIstream = 4;
    public const int DatadirGet = 1;
    public const int DatadirSet = 2;

    public const uint DropeffectNone = 0;
    public const uint DropeffectCopy = 1;
    public const uint DropeffectMove = 2;
    public const uint DropeffectLink = 4;

    public const int DvEFormatetc = unchecked((int)0x8004006A);
    public const int DvETymeds = unchecked((int)0x8004006B);
    public const int DvELindex = unchecked((int)0x80040068);
}

[ComImport]
[Guid("0000010E-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataObject
{
    [PreserveSig] int GetData(ref FORMATETC pFormatetc, out STGMEDIUM pMedium);
    [PreserveSig] int GetDataHere(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium);
    [PreserveSig] int QueryGetData(ref FORMATETC pFormatetc);
    [PreserveSig] int GetCanonicalFormatEtc(ref FORMATETC pFormatetcIn, out FORMATETC pFormatetcOut);
    [PreserveSig] int SetData(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium, bool fRelease);
    [PreserveSig] int EnumFormatEtc(uint dwDirection, out IEnumFORMATETC? ppenumFormatEtc);
    [PreserveSig] int DAdvise(ref FORMATETC pFormatetc, uint advf, IAdviseSink pAdvSink, out uint pdwConnection);
    [PreserveSig] int DUnadvise(uint dwConnection);
    [PreserveSig] int EnumDAdvise(out IEnumSTATDATA? ppenumAdvise);
}

[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
    [PreserveSig] int DragEnter(IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    [PreserveSig] int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTL
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DROPFILES
{
    public int pFiles;
    public POINTL pt;
    public int fNC;
    public int fWide;
}

internal static class ClipboardFormats
{
    public const int HDrop = 15;

    private static readonly object Sync = new();
    private static int _preferredDropEffect = -1;
    private static int _performedDropEffect = -1;
    private static int _xboxFileDescriptor = -1;
    private static int _fileDescriptorW = -1;
    private static int _fileContents = -1;

    public static int PreferredDropEffect => GetRegisteredFormat(ref _preferredDropEffect, "Preferred DropEffect");

    public static int PerformedDropEffect => GetRegisteredFormat(ref _performedDropEffect, "Performed DropEffect");

    public static int XboxFileDescriptor => GetRegisteredFormat(ref _xboxFileDescriptor, "XBOX_FILEDESCRIPTOR");

    public static int FileDescriptorW => GetRegisteredFormat(ref _fileDescriptorW, "FileGroupDescriptorW");

    public static int FileContents => GetRegisteredFormat(ref _fileContents, "FileContents");

    private static int GetRegisteredFormat(ref int cache, string name)
    {
        if (cache >= 0)
            return cache;

        lock (Sync)
        {
            if (cache < 0)
                cache = RegisterClipboardFormat(name);
        }

        return cache;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterClipboardFormat(string lpszFormat);
}

internal static class OleDataTransfer
{
    private const uint FdAttributes = 0x00000004;
    private const uint FdCreationTime = 0x00000008;
    private const uint FdFileSize = 0x00000040;
    private const uint FdWriteTime = 0x00000080;
    private const uint FdProgressUi = 0x00004000;
    private const uint FileAttributeDirectory = 0x00000010;

    private const int StgmRead = 0x00000000;
    private const int StgmShareDenyWrite = 0x00000020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileDescriptorW
    {
        public uint dwFlags;
        public Guid clsid;
        public long sizel;
        public long pointl;
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
    }

    public static FORMATETC CreateFormat(int cf, int tymed = OleConstants.TymedHglobal, int lindex = -1)
    {
        return new FORMATETC
        {
            cfFormat = (short)cf,
            ptd = IntPtr.Zero,
            dwAspect = (DVASPECT)OleConstants.DvaspectContent,
            lindex = lindex,
            tymed = (TYMED)tymed,
        };
    }

    public static bool SupportsFormat(Interop.IDataObject dataObject, int cf)
    {
        var format = CreateFormat(cf);
        return dataObject.QueryGetData(ref format) == HResults.Ok;
    }

    public static bool SupportsPasteFormat(Interop.IDataObject dataObject)
    {
        return SupportsFormat(dataObject, ClipboardFormats.XboxFileDescriptor) ||
               SupportsFormat(dataObject, ClipboardFormats.HDrop) ||
               SupportsFormat(dataObject, ClipboardFormats.FileDescriptorW);
    }

    public static void ReleaseMedium(ref STGMEDIUM medium)
    {
        if (medium.unionmember != IntPtr.Zero)
            ReleaseStgMedium(ref medium);
    }

    public static IReadOnlyList<string> ReadHdropPaths(Interop.IDataObject dataObject)
    {
        var format = CreateFormat(ClipboardFormats.HDrop);
        if (dataObject.GetData(ref format, out var medium) != HResults.Ok || medium.unionmember == IntPtr.Zero)
            return Array.Empty<string>();

        try
        {
            var source = GlobalLock(medium.unionmember);
            if (source == IntPtr.Zero)
                return Array.Empty<string>();

            try
            {
                var drop = Marshal.PtrToStructure<DROPFILES>(source);
                var pathPtr = IntPtr.Add(source, drop.pFiles);
                var paths = new List<string>();
                while (true)
                {
                    var path = Marshal.PtrToStringUni(pathPtr);
                    if (string.IsNullOrEmpty(path))
                        break;

                    paths.Add(path);
                    pathPtr = IntPtr.Add(pathPtr, (path.Length + 1) * 2);
                }

                return paths;
            }
            finally
            {
                GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    public static nint CreateHdrop(IEnumerable<string> paths)
    {
        var encoded = new List<byte>();
        foreach (var path in paths)
        {
            encoded.AddRange(System.Text.Encoding.Unicode.GetBytes(path));
            encoded.Add(0);
            encoded.Add(0);
        }
        encoded.Add(0);
        encoded.Add(0);

        var headerSize = Marshal.SizeOf<DROPFILES>();
        var totalSize = headerSize + encoded.Count;
        var hGlobal = GlobalAlloc(0x0042, (nuint)totalSize);
        if (hGlobal == IntPtr.Zero)
            return IntPtr.Zero;

        var locked = GlobalLock(hGlobal);
        try
        {
            var drop = new DROPFILES
            {
                pFiles = headerSize,
                pt = default,
                fNC = 0,
                fWide = 1,
            };
            Marshal.StructureToPtr(drop, locked, false);
            Marshal.Copy(encoded.ToArray(), 0, IntPtr.Add(locked, headerSize), encoded.Count);
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }

    public static nint CreateDropEffect(uint effect)
    {
        var hGlobal = GlobalAlloc(0x0042, 4);
        if (hGlobal == IntPtr.Zero)
            return IntPtr.Zero;

        var locked = GlobalLock(hGlobal);
        try
        {
            Marshal.WriteInt32(locked, (int)effect);
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }

    public static uint ChooseDropEffect(uint grfKeyState, uint allowed)
    {
        if ((grfKeyState & 0x0008) != 0 && (allowed & OleConstants.DropeffectMove) != 0)
            return OleConstants.DropeffectMove;
        if ((grfKeyState & 0x0004) != 0 && (allowed & OleConstants.DropeffectLink) != 0)
            return OleConstants.DropeffectLink;
        if ((allowed & OleConstants.DropeffectCopy) != 0)
            return OleConstants.DropeffectCopy;
        return OleConstants.DropeffectNone;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    [DllImport("shlwapi.dll", PreserveSig = true)]
    internal static extern int SHGetThreadRef(out nint ppunk);
}
