namespace Rxdk.XbShellExt.Interop;

public static class ComGuids
{
    public const string XboxFolder = "DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44";
    public const string XboxFolderManaged = "DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45";
    public const string ShellFolder = "000214E6-0000-0000-C000-000000000046";
    public const string ShellFolder2 = "93F2F68C-1D1B-11d3-A30E-00C04F79ABD1";
    public const string PersistFolder = "000214EA-0000-0000-C000-000000000046";
    public const string PersistFolder2 = "1AC3D9F0-175C-11D1-95BE-00609797EA4F";
    public const string EnumIdList = "000214F2-0000-0000-C000-000000000046";
    public const string ContextMenu = "000214E4-0000-0000-C000-000000000046";
    public const string ExtractIcon = "0006D000-0000-0000-C000-000000000046";
    public const string XboxShellExtUi = "A7F2C9E1-4B8D-4A2E-9C31-8E5D6F0A2B44";
    public const string ShellExtInit = "000214E8-0000-0000-C000-000000000046";
    public const string ShellView = "000214E1-0000-0000-C000-000000000046";
    public const string ShellView2 = "000214E3-0000-0000-C000-000000000046";
    public const string ShellFolderViewCb = "2047E320-F2A9-11CE-AE65-08002B2E1262";
    public const string DataObject = "0000010E-0000-0000-C000-000000000046";
    public const string DropTarget = "00000122-0000-0000-C000-000000000046";
    public const string Malloc = "00000002-0000-0000-C000-000000000046";
}

internal static class HResults
{
    public const int Ok = 0;
    public const int False = 1;
    public const int InvalidArg = unchecked((int)0x80070057);
    public const int NotImpl = unchecked((int)0x80004001);
    public const int NoInterface = unchecked((int)0x80004002);
    public const int OutOfMemory = unchecked((int)0x8007000E);
    public const int NoObject = unchecked((int)0x800401E5);
    public const int Fail = unchecked((int)0x80004005);
    public const int AccessDenied = unchecked((int)0x80070005);
    public const int ReadFault = unchecked((int)0x80030006);
    // IShellFolder::CompareIDs success codes (legacy xbshlext S_* values).
    public const int Equal = 0;
    public const int Greater = 1;
    public const int Less = unchecked((int)0x0000FFFF);
}

internal static class ShellConstants
{
    public const uint ShgdnNormal = 0x0000;
    public const uint ShgdnForParsing = 0x8000;
    public const uint ShgdnForAddressBar = 0x4000;
    public const uint ShgdnForEditing = 0x1000;
    public const uint ShgdnInfolder = 0x0001;

    public const string NamespaceDisplayName = "Xbox Neighborhood";

    public const uint ShcidsColumnMask = 0x0000FFFF;

    public const uint ShcontfFolders = 0x0020;
    public const uint ShcontfNonFolders = 0x0040;
    public const uint ShcontfIncludeHidden = 0x0080;
    public const uint ShcontfFastItems = 0x2000;
    public const uint ShcontfFlatList = 0x4000;

    public const uint SfgaoFolder = 0x20000000;
    public const uint SfgaoBrowsable = 0x08000000;
    public const uint SfgaoHassubfolder = 0x80000000;
    public const uint SfgaoCanlink = 0x00000004;
    public const uint SfgaoHaspropsheet = 0x00000040;
    public const uint SfgaoDropTarget = 0x00000100;
    public const uint SfgaoCandelete = 0x00000020;
    public const uint SfgaoCanrename = 0x00000010;
    public const uint SfgaoCancopy = 0x00000001;
    public const uint SfgaoCanmove = 0x00000002;
    public const uint SfgaoHidden = 0x00080000;
    public const uint SfgaoStream = 0x00400000;

    public const uint RootAttributes =
        SfgaoCanlink | SfgaoCanrename | SfgaoFolder | SfgaoHassubfolder | SfgaoBrowsable;

    public const uint ConsoleAttributes =
        SfgaoCanlink | SfgaoCandelete | SfgaoHaspropsheet | SfgaoFolder | SfgaoHassubfolder | SfgaoBrowsable;

    public const uint VolumeAttributes =
        SfgaoCanlink | SfgaoHaspropsheet | SfgaoDropTarget | SfgaoFolder | SfgaoHassubfolder | SfgaoBrowsable;

    public const uint DirectoryAttributes =
        SfgaoCancopy | SfgaoCanmove | SfgaoCanlink | SfgaoCanrename | SfgaoCandelete |
        SfgaoHaspropsheet | SfgaoDropTarget | SfgaoFolder | SfgaoHassubfolder | SfgaoBrowsable;

    public const uint FileAttributes =
        SfgaoCancopy | SfgaoCanmove | SfgaoCanrename | SfgaoCandelete | SfgaoHaspropsheet;

    public const uint AddConsoleAttributes = SfgaoCanlink;

    public const int CmInvokeDefaultCommand = unchecked((int)0xFFFFFFFF);
    public const int CmInvokeVerbNoUi = 0x00000004;

    public const uint GcsHelptextA = 0x00000001;
    public const uint GcsHelptextW = 0x00000002;
    public const uint GcsValidateA = 0x00000003;
    public const uint GcsValidateW = 0x00000004;
    public const uint GcsVerba = 0x00000005;
    public const uint GcsVerbw = 0x00000006;

    public const uint CmfExplore = 0x00000004;

    public const string AddConsoleSegment = "?Add Xbox";
    public const string AddConsoleDisplayName = "Add Xbox";

    public static string SegmentToDisplayName(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return segment;

        return segment[0] == '?' ? segment[1..] : segment;
    }
}

public enum StrRetType : uint
{
    Wstr = 0,
    Offset = 0x1,
    CStr = 0x2,
    UOffset = 0x3,
    OffsetW = 0x4,
    OffsetA = 0x5,
}

[StructLayout(LayoutKind.Sequential)]
public struct StrRet
{
    public StrRetType uType;
    public StrRetUnion u;

    [StructLayout(LayoutKind.Explicit)]
    public struct StrRetUnion
    {
        [FieldOffset(0)] public nuint Offset;
        [FieldOffset(0)] public nint Pointer;
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct CmInvokeCommandInfo
{
    public int cbSize;
    public uint fMask;
    public nint hwnd;
    public nint lpVerb;
    public nint lpParameters;
    public nint lpDirectory;
    public int nShow;
    public int dwHotKey;
    public nint hIcon;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SfvCreate
{
    public uint cbSize;
    public nint pshf;
    public nint psvOuter;
    public nint psfvcb;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct Csfv
{
    public uint cbSize;
    public nint pshf;
    public nint psvOuter;
    public nint pidl;
    public int lEvents;
    public nint pfnCallback;
    public int fvm;
}

internal static class FolderViewMode
{
    public const int Icon = 1;
}

[ComImport]
[Guid(ComGuids.Malloc)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMalloc
{
    [PreserveSig]
    int Alloc(uint cb, out nint ppv);

    [PreserveSig]
    int Realloc(nint pv, uint cb, out nint ppv);

    [PreserveSig]
    int Free(nint pv);

    [PreserveSig]
    int GetSize(nint pv, out uint pcb);

    [PreserveSig]
    int DidAlloc(nint pv);

    [PreserveSig]
    int HeapMinimize();
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct ShellDetails
{
    public int fmt;
    public int cxChar;
    public StrRet str;
}

[StructLayout(LayoutKind.Sequential)]
public struct ShColumnId
{
    public Guid fmtid;
    public uint pid;
}

internal static class ShellColumnState
{
    public const uint TypeStr = 0x00000001;
    public const uint OnByDefault = 0x00000010;
}

internal static class ShellColumnIds
{
    public static readonly Guid FmtIdStorage = new("B725F130-47EF-101A-A5F1-02608C9EBACC");
    public const uint PidStgName = 0x0000000A;
}

// IShellFolder2 is declared FLAT (it does NOT inherit IShellFolder) and
// redeclares all ten IShellFolder methods first, then its own seven. The CLR's
// classic COM-callable wrapper does not lay out inherited base-interface methods
// reliably for a derived [ComImport] interface, so a native caller invoking an
// inherited IShellFolder method (e.g. EnumObjects) through an IShellFolder2
// pointer mis-dispatches and crashes. Declaring every slot explicitly here makes
// the IShellFolder2 vtable self-contained and binary-compatible with the native
// IShellFolder2 layout. XboxFolder implements both IShellFolder and IShellFolder2;
// a single managed method body satisfies the same-signature members of both.
[ComImport]
[Guid(ComGuids.ShellFolder2)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellFolder2
{
    [PreserveSig] int ParseDisplayName(nint hwnd, nint pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out nint ppidl, out uint pdwAttributes);
    [PreserveSig] int EnumObjects(nint hwnd, uint grfFlags, out IEnumIDList? ppEnumIDList);
    [PreserveSig] int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);
    [PreserveSig] int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);
    [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
    [PreserveSig] int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);
    [PreserveSig] int GetAttributesOf(uint cidl, nint apidl, ref uint rgfInOut);
    [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint cidl, nint apidl, ref Guid riid, nint prgfInOut, out nint ppv);
    [PreserveSig] int GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName);
    [PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out nint ppidlOut);
    [PreserveSig] int GetDefaultSearchGUID(out Guid pguid);
    [PreserveSig] int EnumSearches(out nint ppenum);
    [PreserveSig] int GetDefaultColumn(uint dwRes, out uint pSort, out uint pDisplay);
    [PreserveSig] int GetDefaultColumnState(uint iColumn, out uint pcsFlags);
    [PreserveSig] int GetDetailsEx(nint pidl, nint pscid, nint pv);
    [PreserveSig] int GetDetailsOf(nint pidl, uint iColumn, out ShellDetails psd);
    [PreserveSig] int MapColumnToSCID(uint iColumn, out ShColumnId pscid);
}

[ComImport]
[Guid(ComGuids.ShellFolder)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellFolder
{
    [PreserveSig] int ParseDisplayName(nint hwnd, nint pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out nint ppidl, out uint pdwAttributes);
    [PreserveSig] int EnumObjects(nint hwnd, uint grfFlags, out IEnumIDList? ppEnumIDList);
    [PreserveSig] int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);
    [PreserveSig] int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);
    [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
    [PreserveSig] int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);
    [PreserveSig] int GetAttributesOf(uint cidl, nint apidl, ref uint rgfInOut);
    [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint cidl, nint apidl, ref Guid riid, nint prgfInOut, out nint ppv);
    [PreserveSig] int GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName);
    [PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out nint ppidlOut);
}

[ComImport]
[Guid(ComGuids.PersistFolder)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPersistFolder
{
    [PreserveSig] int GetClassID(out Guid pClassID);
    [PreserveSig] int Initialize(nint pidl);
}

[ComImport]
[Guid(ComGuids.PersistFolder2)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPersistFolder2
{
    [PreserveSig] int GetClassID(out Guid pClassID);
    [PreserveSig] int Initialize(nint pidl);
    [PreserveSig] int GetCurFolder(out nint ppidl);
}

[ComImport]
[Guid(ComGuids.EnumIdList)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumIDList
{
    [PreserveSig] int Next(uint celt, nint rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumIDList? ppEnum);
}

[ComImport]
[Guid(ComGuids.ContextMenu)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfo pici);
    [PreserveSig] int GetCommandString(nuint idCmd, uint uType, nint pReserved, StringBuilder pszName, uint cchMax);
}

[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out nint ppvObject);
}

[ComImport]
[Guid("000214E2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    [PreserveSig] int BrowseObject(nint pidl, uint wFlags);
}

[ComImport]
[Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectWithSite
{
    [PreserveSig] int SetSite(nint pUnkSite);
    [PreserveSig] int GetSite(ref Guid riid, out nint ppvSite);
}

internal static class ShellBrowserFlags
{
    public const uint SbspDefBrowser = 0x0000;
    public const uint SbspOpenMode = 0x4000;
    public const uint SbspRelative = 0x0000;
}

internal static class ShellServiceIds
{
    public static readonly Guid ShellBrowser = new("E07010BC-BC17-44DD-AD61-132959A1AD21");
}

internal static class ShellBrowserGuids
{
    public static readonly Guid IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
}

[ComImport]
[Guid(ComGuids.ShellExtInit)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellExtInit
{
    [PreserveSig] int Initialize(nint pidlFolder, nint pdtobj, nint hkeyProgId);
}

[ComImport]
[Guid(ComGuids.ShellFolderViewCb)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellFolderViewCB
{
    [PreserveSig] int MessageSFVCB(uint uMsg, nint wParam, nint lParam);
}

internal static partial class NativeMethods
{
    public const uint ShChangeNotifyMkdir = 0x00000008;
    public const uint ShChangeNotifyUpdatedir = 0x00001000;
    public const uint ShcnfPath = 0x0001;
    public const uint ShcnfFlush = 0x1000;

    [DllImport("shell32.dll")]
    public static extern nint ILClone(nint pidl);

    [DllImport("shell32.dll")]
    public static extern void ILFree(nint pidl);

    [DllImport("shell32.dll")]
    public static extern nint ILCombine(nint pidl1, nint pidl2);

    [DllImport("ole32.dll")]
    public static extern nint CoTaskMemAlloc(nuint cb);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(nint pv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(uint wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int StrRetToBufW(ref StrRet pstr, nint pidl, StringBuilder pszBuf, uint cchBuf);

    [DllImport("Rxdk.XbShellExt.Shell.dll", PreserveSig = true)]
    internal static extern int XbShellExt_CreateNativeContextMenu(
        nint folderPidl,
        uint cidl,
        nint apidl,
        ref Guid riid,
        out nint ppv);

    [DllImport("Rxdk.XbShellExt.Shell.dll", PreserveSig = true)]
    internal static extern int XbShellExt_CreateNativeExtractIcon(
        nint folderPidl,
        uint cidl,
        nint apidl,
        ref Guid riid,
        out nint ppv);

    public static int CreateShellFolderView(IShellFolder folder, IShellFolderViewCB? callback, out nint shellView)
    {
        shellView = 0;
        var iidShellFolder = new Guid(ComGuids.ShellFolder);
        var unknown = Marshal.GetIUnknownForObject(folder);
        nint pshf = 0;
        nint psfvcb = 0;
        nint folderPidl = 0;
        try
        {
            var hr = Marshal.QueryInterface(unknown, ref iidShellFolder, out pshf);
            if (hr < 0)
                return hr;

            var iidPersistFolder2 = new Guid(ComGuids.PersistFolder2);
            if (Marshal.QueryInterface(unknown, ref iidPersistFolder2, out var persistFolder2) >= 0)
            {
                try
                {
                    var persist = (IPersistFolder2)Marshal.GetObjectForIUnknown(persistFolder2)!;
                    persist.GetCurFolder(out folderPidl);
                }
                finally
                {
                    Marshal.Release(persistFolder2);
                }
            }

            if (callback != null)
            {
                psfvcb = Marshal.GetComInterfaceForObject(callback, typeof(IShellFolderViewCB));
                if (psfvcb == 0)
                    return HResults.NoObject;
            }

            var sfv = new SfvCreate
            {
                cbSize = (uint)Marshal.SizeOf<SfvCreate>(),
                pshf = pshf,
                psvOuter = 0,
                psfvcb = psfvcb,
            };

            hr = CreateShellFolderViewViaExport(ref sfv, out shellView);
            if (hr >= 0 && shellView != 0)
                return hr;

            if (psfvcb != 0)
            {
                sfv.psfvcb = 0;
                hr = CreateShellFolderViewViaExport(ref sfv, out shellView);
                if (hr >= 0 && shellView != 0)
                    return hr;
            }

            if (folderPidl != 0)
            {
                var csfv = new Csfv
                {
                    cbSize = (uint)Marshal.SizeOf<Csfv>(),
                    pshf = pshf,
                    psvOuter = 0,
                    pidl = folderPidl,
                    lEvents = 0,
                    pfnCallback = 0,
                    fvm = FolderViewMode.Icon,
                };

                hr = CreateShellFolderViewExViaExport(ref csfv, out shellView);
                if (hr >= 0 && shellView != 0)
                    return hr;
            }

            return hr;
        }
        finally
        {
            if (folderPidl != 0)
                ILFree(folderPidl);
            if (psfvcb != 0)
                Marshal.Release(psfvcb);
            if (pshf != 0)
                Marshal.Release(pshf);
            Marshal.Release(unknown);
        }
    }

    public static int CreateShellFolderViewViaExport(ref SfvCreate pcsfv, out nint ppsv)
    {
        ppsv = 0;
        var proc = ResolveShellFolderViewExport("SHCreateShellFolderView", 256);
        if (proc == 0)
            return HResults.NoObject;

        var del = Marshal.GetDelegateForFunctionPointer<CreateShellFolderViewDelegate>(proc);
        return del(ref pcsfv, out ppsv);
    }

    public static int CreateShellFolderViewExViaExport(ref Csfv pcsfv, out nint ppsv)
    {
        ppsv = 0;
        var proc = ResolveShellFolderViewExport("SHCreateShellFolderViewEx", 0);
        if (proc == 0)
            return HResults.NoObject;

        var del = Marshal.GetDelegateForFunctionPointer<CreateShellFolderViewExDelegate>(proc);
        return del(ref pcsfv, out ppsv);
    }

    private static nint ResolveShellFolderViewExport(string name, int ordinalFallback)
    {
        var shell32 = LoadLibrary("shell32.dll");
        if (shell32 == 0)
            return 0;

        var proc = GetProcAddressByName(shell32, name);
        if (proc == 0 && ordinalFallback != 0)
            proc = GetProcAddressOrdinal(shell32, ordinalFallback);

        return proc;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateShellFolderViewDelegate(ref SfvCreate pcsfv, out nint ppsv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateShellFolderViewExDelegate(ref Csfv pcsfv, out nint ppsv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, EntryPoint = "GetProcAddress")]
    private static extern nint GetProcAddressByName(nint hModule, string procName);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")]
    private static extern nint GetProcAddressOrdinal(nint hModule, nint ordinal);

    public const uint SeeMaskIdList = 0x00000100;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellExecuteExW(ref ShellExecuteInfo lpExecInfo);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ShellExecuteInfo
{
    public int cbSize;
    public uint fMask;
    public nint hwnd;
    public nint lpVerb;
    public nint lpFile;
    public nint lpParameters;
    public nint lpDirectory;
    public int nShow;
    public nint hInstApp;
    public nint lpIDList;
    public nint lpClass;
    public nint hkeyClass;
    public uint dwHotKey;
    public nint hIcon;
    public nint hProcess;
}
