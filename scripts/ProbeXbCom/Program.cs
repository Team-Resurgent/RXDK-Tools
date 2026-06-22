using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Interop;

static string F(int hr) => $"0x{hr:X8}";

var clsid = new Guid(ComGuids.XboxFolder);
var clsidManaged = new Guid(ComGuids.XboxFolderManaged);
var iidSf = new Guid(ComGuids.ShellFolder);
var iidSf2 = new Guid(ComGuids.ShellFolder2);
var iidPf = new Guid(ComGuids.PersistFolder);
var iidPf2 = new Guid(ComGuids.PersistFolder2);
var view2 = new Guid(ComGuids.ShellView2);

ProbeNative.PrintRegistryPath("CC44", clsid);
ProbeNative.PrintRegistryPath("CC45", clsidManaged);

var hrCo45 = ProbeNative.CoCreateInstance(clsidManaged, iidSf, out var managedFolder);
Console.WriteLine($"CoCreate CC45(IShellFolder): {F(hrCo45)} ptr=0x{managedFolder:X}");
var hrGco = ProbeNative.DllGetClassObjectFromComHost(clsidManaged, out var factory);
Console.WriteLine($"DllGetClassObject CC45: {F(hrGco)} factory=0x{factory:X}");
if (factory != 0) Marshal.Release(factory);
if (managedFolder != 0)
{
    Console.WriteLine($"  CC45 QI IPersistFolder2: {F(ProbeNative.QueryInterface(managedFolder, iidPf2, out var pf2m))} ptr=0x{pf2m:X}");
    if (pf2m != 0) Marshal.Release(pf2m);
    Marshal.Release(managedFolder);
}

var hrCo = ProbeNative.CoCreateInstance(clsid, iidSf, out var comFolder);
Console.WriteLine($"CoCreate CC44(IShellFolder): {F(hrCo)} ptr=0x{comFolder:X}");
if (hrCo < 0) return 1;

Console.WriteLine($"  CC44 QI IPersistFolder: {F(ProbeNative.QueryInterface(comFolder, iidPf, out var pf))} ptr=0x{pf:X}");
if (pf != 0) Marshal.Release(pf);
Console.WriteLine($"  CC44 QI IPersistFolder2: {F(ProbeNative.QueryInterface(comFolder, iidPf2, out var pf2))} ptr=0x{pf2:X}");
if (pf2 != 0) Marshal.Release(pf2);
Console.WriteLine($"  CC44 QI IShellFolder2: {F(ProbeNative.QueryInterface(comFolder, iidSf2, out var sf2Probe))} ptr=0x{sf2Probe:X}");
if (sf2Probe != 0) Marshal.Release(sf2Probe);

if (ProbeNative.TryParseNamespace(out var pidl) >= 0 && pidl != 0)
{
    Console.WriteLine($"Initialize: {F(ProbeNative.Initialize(comFolder, pidl))}");
    ProbeNative.ILFree(pidl);
}

// Direct SHCreateShellFolderView with CoCreate pointer as pshf
var sfv = new SfvCreate
{
    cbSize = (uint)Marshal.SizeOf<SfvCreate>(),
    pshf = comFolder,
    psvOuter = 0,
    psfvcb = 0,
};
var hrDirect = ProbeNative.CreateShellFolderView(ref sfv, out var directView);
Console.WriteLine($"SHCreateShellFolderView(null cb, comFolder as pshf): {F(hrDirect)} view=0x{directView:X}");

if (directView != 0)
{
    var hrQiView2 = Marshal.QueryInterface(directView, ref view2, out var view2Ptr);
    Console.WriteLine($"  QI view->IShellView2: {F(hrQiView2)} ptr=0x{view2Ptr:X}");
    if (view2Ptr != 0) Marshal.Release(view2Ptr);
    Marshal.Release(directView);
}

// QI to IShellFolder2 for pshf
if (Marshal.QueryInterface(comFolder, ref iidSf2, out var sf2) >= 0)
{
    sfv.pshf = sf2;
    hrDirect = ProbeNative.CreateShellFolderView(ref sfv, out directView);
    Console.WriteLine($"SHCreateShellFolderView(null cb, IShellFolder2 ptr): {F(hrDirect)} view=0x{directView:X}");
    if (directView != 0) Marshal.Release(directView);
    Marshal.Release(sf2);
}

if (ProbeNative.TryParseNamespace(out var nsPidl) >= 0 && nsPidl != 0)
{
    var csfv = new Csfv
    {
        cbSize = (uint)Marshal.SizeOf<Csfv>(),
        pshf = comFolder,
        psvOuter = 0,
        pidl = nsPidl,
        lEvents = 0,
        pfnCallback = 0,
        fvm = 1,
    };
    var hrEx = ProbeNative.CreateShellFolderViewEx(ref csfv, out directView);
    Console.WriteLine($"SHCreateShellFolderViewEx(null cb, pidl): {F(hrEx)} view=0x{directView:X}");
    if (directView != 0) Marshal.Release(directView);
    ProbeNative.ILFree(nsPidl);
}

var riid = view2;
var hrView = ProbeNative.CreateViewObject(comFolder, ref riid, out var viewPtr);
Console.WriteLine($"CreateViewObject(IShellView2): {F(hrView)} ptr=0x{viewPtr:X}");
if (viewPtr != 0) Marshal.Release(viewPtr);

// Reproduce folder navigation: root(CC44) -> bind "myxbox" -> bind "C" ->
// enumerate + exercise per-child UI objects the way DefView does, looped.
var consoleSegment = Environment.GetEnvironmentVariable("XB_PROBE_CONSOLE") ?? "myxbox";
var driveSegment = Environment.GetEnvironmentVariable("XB_PROBE_DRIVE") ?? "C";
var iterations = int.TryParse(Environment.GetEnvironmentVariable("XB_PROBE_ITERS"), out var it) ? it : 10;
var ops = (Environment.GetEnvironmentVariable("XB_PROBE_OPS") ?? "view,attrs,icon,menu,dataobj")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
bool DoView() => ops.Contains("view");
bool DoAttrs() => ops.Contains("attrs");
bool DoIcon() => ops.Contains("icon");
bool DoMenu() => ops.Contains("menu");
bool DoDataObj() => ops.Contains("dataobj");
Console.WriteLine($"ops enabled: {string.Join(',', ops)}");
var iidCtxMenu = new Guid(ComGuids.ContextMenu);
var iidExtractIcon = new Guid(ComGuids.ExtractIcon);
var iidDataObj = new Guid(ComGuids.DataObject);
var viewGuidA = new Guid("64961751-0835-43C0-8FFE-D57686530E64");
var viewGuidB = new Guid("8279FEB8-5CA4-45C4-BE27-770DCDEA1DEB");

for (var iter = 0; iter < iterations; iter++)
{
    Console.WriteLine($"--- navigate {consoleSegment}\\{driveSegment} (iter {iter}) ---");

    var consolePidl = ProbeNative.CreateSimplePidl(consoleSegment);
    var hrBindConsole = ProbeNative.BindToObject(comFolder, consolePidl, iidSf, out var consoleSf);
    ProbeNative.CoTaskMemFree(consolePidl);
    if (hrBindConsole < 0 || consoleSf == 0) { Console.WriteLine($"  bind {consoleSegment} failed {F(hrBindConsole)}"); break; }

    var drivePidl = ProbeNative.CreateSimplePidl(driveSegment);
    var hrBindDrive = ProbeNative.BindToObject(consoleSf, drivePidl, iidSf, out var driveSf);
    ProbeNative.CoTaskMemFree(drivePidl);
    if (hrBindDrive < 0 || driveSf == 0) { Console.WriteLine($"  bind {driveSegment} failed {F(hrBindDrive)}"); Marshal.Release(consoleSf); break; }

    if (DoView())
    {
        var gA = viewGuidA; ProbeNative.CreateViewObject(driveSf, ref gA, out var vA); if (vA != 0) Marshal.Release(vA);
        var gB = viewGuidB; ProbeNative.CreateViewObject(driveSf, ref gB, out var vB); if (vB != 0) Marshal.Release(vB);
    }

    const uint shcontf = 0x20 | 0x40; // SHCONTF_FOLDERS | SHCONTF_NONFOLDERS
    var children = new List<nint>();
    if (ProbeNative.EnumObjects(driveSf, shcontf, out var enumIdl) >= 0 && enumIdl != 0)
    {
        while (ProbeNative.EnumNext(enumIdl, out var childPidl) == 0 && childPidl != 0)
        {
            children.Add(childPidl);
            if (children.Count > 5000) break;
        }
        Marshal.Release(enumIdl);
    }

    foreach (var child in children)
    {
        if (DoAttrs())
        {
            uint attrs = 0xFFFFFFFF;
            ProbeNative.GetAttributesOf(driveSf, child, ref attrs);
        }

        if (DoIcon() && ProbeNative.GetUIObjectOf(driveSf, child, iidExtractIcon, out var icon) >= 0 && icon != 0)
            Marshal.Release(icon);

        if (DoMenu() && ProbeNative.GetUIObjectOf(driveSf, child, iidCtxMenu, out var menu) >= 0 && menu != 0)
        {
            var hmenu = ProbeNative.CreatePopupMenu();
            ProbeNative.QueryContextMenu(menu, hmenu, 0, 1, 0x7FFF, 0);
            if (hmenu != 0) ProbeNative.DestroyMenu(hmenu);
            Marshal.Release(menu);
        }

        if (DoDataObj() && ProbeNative.GetUIObjectOf(driveSf, child, iidDataObj, out var dobj) >= 0 && dobj != 0)
        {
            if (iter == 0 && children.IndexOf(child) == 0)
                ProbeNative.ExerciseCopyGetData(dobj);
            Marshal.Release(dobj);
        }
    }

    foreach (var child in children) ProbeNative.CoTaskMemFree(child);

    var iidDriveView = view2;
    if (ProbeNative.CreateViewObject(driveSf, ref iidDriveView, out var driveView) >= 0 && driveView != 0)
        Marshal.Release(driveView);

    Console.WriteLine($"  iter {iter}: enumerated {children.Count} child item(s), exercised UI objects");

    Marshal.Release(driveSf);
    Marshal.Release(consoleSf);
}

Console.WriteLine("navigation probe completed without crash");

Marshal.Release(comFolder);

return (hrDirect >= 0 && directView != 0) || (hrView >= 0 && viewPtr != 0) ? 0 : 1;

internal static class ProbeNative
{
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint r, int f);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint ctx, ref Guid iid, out nint ppv);
    [DllImport("ole32.dll")] private static extern nint CoTaskMemAlloc(nuint cb);
    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(nint p);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string pszName, nint pbc, out nint ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("shell32.dll")] public static extern void ILFree(nint pidl);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [DllImport("kernel32.dll")] private static extern int FreeLibrary(nint hModule);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, EntryPoint = "GetProcAddress")]
    private static extern nint GetProcAddressByName(nint hModule, string procName);
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")] private static extern nint GetProcAddressOrdinal(nint hModule, nint ordinal);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObjectFn(ref Guid clsid, ref Guid iid, out nint ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeFn(nint self, nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateViewObjectFn(nint self, nint hwndOwner, nint riid, out nint ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BindToObjectFn(nint self, nint pidl, nint pbc, ref Guid riid, out nint ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumObjectsFn(nint self, nint hwnd, uint flags, out nint ppenum);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumNextFn(nint self, uint celt, out nint rgelt, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetUIObjectOfFn(nint self, nint hwnd, uint cidl, nint apidl, ref Guid riid, nint rgf, out nint ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAttributesOfFn(nint self, uint cidl, nint apidl, ref uint rgf);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryContextMenuFn(nint self, nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDataFn(nint self, ref FormatEtc format, out StgMedium medium);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryGetDataFn(nint self, ref FormatEtc format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StreamReadFn(nint self, nint pv, int cb, out int read);

    [StructLayout(LayoutKind.Sequential)]
    private struct FormatEtc
    {
        public ushort cfFormat;
        public nint ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StgMedium
    {
        public uint tymed;
        public nint unionmember;
        public nint pUnkForRelease;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClipboardFormat(string format);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] public static extern int DestroyMenu(nint hMenu);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateShellFolderViewDelegate(ref SfvCreate pcsfv, out nint ppsv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateShellFolderViewExDelegate(ref Csfv pcsfv, out nint ppsv);

    static ProbeNative() => CoInitializeEx(0, 2);

    public static int CoCreateInstance(Guid clsid, Guid iid, out nint ppv)
    {
        ppv = 0;
        return CoCreateInstance(ref clsid, 0, 1, ref iid, out ppv);
    }

    public static int QueryInterface(nint unknown, Guid iid, out nint ppv)
    {
        ppv = 0;
        return Marshal.QueryInterface(unknown, ref iid, out ppv);
    }

    public static int DllGetClassObjectFromComHost(Guid clsid, out nint factory)
    {
        factory = 0;
        var subKey = $@"CLSID\{clsid:B}\InprocServer32";
        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(subKey);
        var path = key?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(path))
            return unchecked((int)0x80040111);

        var module = LoadLibrary(path);
        if (module == 0)
            return Marshal.GetHRForLastWin32Error();

        try
        {
            var proc = GetProcAddressByName(module, "DllGetClassObject");
            if (proc == 0)
                return unchecked((int)0x80004005);

            var fn = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectFn>(proc);
            var iidClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
            return fn(ref clsid, ref iidClassFactory, out factory);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    public static void PrintRegistryPath(string label, Guid clsid)
    {
        var subKey = $@"CLSID\{clsid:B}\InprocServer32";
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(subKey);
            var path = key?.GetValue(null) as string ?? "(missing)";
            Console.WriteLine($"{label} InprocServer32: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{label} InprocServer32: (error: {ex.Message})");
        }
    }

    public static int TryParseNamespace(out nint pidl)
    {
        pidl = 0;
        var attrs = 0u;
        return SHParseDisplayName("::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}", 0, out pidl, 0, out attrs);
    }

    public static int Initialize(nint shellFolder, nint pidl)
    {
        var iidPf2 = new Guid(ComGuids.PersistFolder2);
        var iidPf = new Guid(ComGuids.PersistFolder);
        nint pf = 0;
        if (Marshal.QueryInterface(shellFolder, ref iidPf2, out pf) < 0 &&
            Marshal.QueryInterface(shellFolder, ref iidPf, out pf) < 0)
            return unchecked((int)0x80004002);
        try
        {
            var vtable = Marshal.ReadIntPtr(pf);
            var fnPtr = Marshal.ReadIntPtr(vtable, IntPtr.Size * 4);
            var fn = Marshal.GetDelegateForFunctionPointer<InitializeFn>(fnPtr);
            return fn(pf, pidl);
        }
        finally { if (pf != 0) Marshal.Release(pf); }
    }

    public static int CreateShellFolderView(ref SfvCreate sfv, out nint view)
    {
        view = 0;
        var shell32 = LoadLibrary("shell32.dll");
        var proc = GetProcAddressByName(shell32, "SHCreateShellFolderView");
        if (proc == 0) proc = GetProcAddressOrdinal(shell32, 256);
        if (proc == 0) return unchecked((int)0x80004005);
        var del = Marshal.GetDelegateForFunctionPointer<CreateShellFolderViewDelegate>(proc);
        return del(ref sfv, out view);
    }

    public static int CreateShellFolderViewEx(ref Csfv csfv, out nint view)
    {
        view = 0;
        var shell32 = LoadLibrary("shell32.dll");
        var proc = GetProcAddressByName(shell32, "SHCreateShellFolderViewEx");
        if (proc == 0) return unchecked((int)0x80004005);
        var del = Marshal.GetDelegateForFunctionPointer<CreateShellFolderViewExDelegate>(proc);
        return del(ref csfv, out view);
    }

    public static nint CreateSimplePidl(string segment)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(segment + '\0');
        var cb = (ushort)(sizeof(ushort) + bytes.Length);
        var total = cb + sizeof(ushort);
        var pidl = CoTaskMemAlloc((nuint)total);
        if (pidl == 0) throw new OutOfMemoryException();
        Marshal.WriteInt16(pidl, (short)cb);
        Marshal.Copy(bytes, 0, pidl + sizeof(ushort), bytes.Length);
        Marshal.WriteInt16(pidl + cb, 0);
        return pidl;
    }

    public static int BindToObject(nint shellFolder, nint pidl, Guid riid, out nint ppv)
    {
        ppv = 0;
        var vtable = Marshal.ReadIntPtr(shellFolder);
        var fn = Marshal.GetDelegateForFunctionPointer<BindToObjectFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 5));
        return fn(shellFolder, pidl, 0, ref riid, out ppv);
    }

    public static int EnumObjects(nint shellFolder, uint flags, out nint ppenum)
    {
        ppenum = 0;
        var vtable = Marshal.ReadIntPtr(shellFolder);
        var fn = Marshal.GetDelegateForFunctionPointer<EnumObjectsFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 4));
        return fn(shellFolder, 0, flags, out ppenum);
    }

    public static int EnumNext(nint enumIdList, out nint pidl)
    {
        pidl = 0;
        var vtable = Marshal.ReadIntPtr(enumIdList);
        var fn = Marshal.GetDelegateForFunctionPointer<EnumNextFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 3));
        return fn(enumIdList, 1, out pidl, out _);
    }

    public static int GetUIObjectOf(nint shellFolder, nint childPidl, Guid riid, out nint ppv)
    {
        ppv = 0;
        var arr = CoTaskMemAlloc((nuint)IntPtr.Size);
        Marshal.WriteIntPtr(arr, childPidl);
        try
        {
            var vtable = Marshal.ReadIntPtr(shellFolder);
            var fn = Marshal.GetDelegateForFunctionPointer<GetUIObjectOfFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 10));
            return fn(shellFolder, 0, 1, arr, ref riid, 0, out ppv);
        }
        finally { CoTaskMemFree(arr); }
    }

    public static int GetAttributesOf(nint shellFolder, nint childPidl, ref uint rgf)
    {
        var arr = CoTaskMemAlloc((nuint)IntPtr.Size);
        Marshal.WriteIntPtr(arr, childPidl);
        try
        {
            var vtable = Marshal.ReadIntPtr(shellFolder);
            var fn = Marshal.GetDelegateForFunctionPointer<GetAttributesOfFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 9));
            return fn(shellFolder, 1, arr, ref rgf);
        }
        finally { CoTaskMemFree(arr); }
    }

    public static int QueryContextMenu(nint contextMenu, nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags)
    {
        var vtable = Marshal.ReadIntPtr(contextMenu);
        var fn = Marshal.GetDelegateForFunctionPointer<QueryContextMenuFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 3));
        return fn(contextMenu, hmenu, indexMenu, idCmdFirst, idCmdLast, flags);
    }

    public static void ExerciseCopyGetData(nint dataObject)
    {
        var cfDesc = RegisterClipboardFormat("FileGroupDescriptorW");
        var cfContents = RegisterClipboardFormat("FileContents");
        const uint tymedHglobal = 1;
        const uint tymedIstream = 4;
        const uint aspectContent = 1;

        var vtable = Marshal.ReadIntPtr(dataObject);
        var getData = Marshal.GetDelegateForFunctionPointer<GetDataFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 3));
        var queryGetData = Marshal.GetDelegateForFunctionPointer<QueryGetDataFn>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 5));

        var descEtc = new FormatEtc { cfFormat = cfDesc, dwAspect = aspectContent, lindex = -1, tymed = tymedHglobal };
        var hrDesc = getData(dataObject, ref descEtc, out var descMedium);
        Console.WriteLine($"  GetData(FileGroupDescriptorW): 0x{hrDesc:X8} tymed={descMedium.tymed}");
        if (hrDesc < 0 || descMedium.unionmember == 0)
            return;

        var locked = GlobalLock(descMedium.unionmember);
        var itemCount = locked != 0 ? Marshal.ReadInt32(locked) : 0;
        if (locked != 0) GlobalUnlock(descMedium.unionmember);
        Console.WriteLine($"    descriptor items={itemCount}");

        for (var index = 0; index < Math.Min(itemCount, 16); index++)
        {
            var contentsEtc = new FormatEtc
            {
                cfFormat = cfContents,
                dwAspect = aspectContent,
                lindex = index,
                tymed = tymedIstream,
            };
            if (queryGetData(dataObject, ref contentsEtc) < 0)
                continue;

            var hrContents = getData(dataObject, ref contentsEtc, out var contentsMedium);
            Console.WriteLine($"  GetData(FileContents lindex={index}): 0x{hrContents:X8} tymed={contentsMedium.tymed}");
            if (hrContents < 0 || contentsMedium.unionmember == 0)
                continue;

            var iidStream = new Guid("0000000c-0000-0000-c000-000000000046");
            if (Marshal.QueryInterface(contentsMedium.unionmember, ref iidStream, out var stream) < 0 || stream == 0)
            {
                Console.WriteLine("    QI IStream failed");
                Marshal.Release(contentsMedium.unionmember);
                continue;
            }

            var streamVtable = Marshal.ReadIntPtr(stream);
            var readFn = Marshal.GetDelegateForFunctionPointer<StreamReadFn>(Marshal.ReadIntPtr(streamVtable, IntPtr.Size * 3));
            var buffer = Marshal.AllocCoTaskMem(4096);
            var hrRead = readFn(stream, buffer, 64, out var bytesRead);
            Marshal.FreeCoTaskMem(buffer);
            Console.WriteLine($"    IStream::Read: 0x{hrRead:X8} bytes={bytesRead}");
            Marshal.Release(stream);
            Marshal.Release(contentsMedium.unionmember);
            break;
        }

        if (descMedium.pUnkForRelease != 0)
            Marshal.Release(descMedium.pUnkForRelease);
        else
            GlobalFree(descMedium.unionmember);
    }

    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint hMem);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint hMem);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint hMem);

    public static int CreateViewObject(nint shellFolder, ref Guid riid, out nint ppv)
    {
        ppv = 0;
        var riidPtr = Marshal.AllocCoTaskMem(16);
        try
        {
            Marshal.StructureToPtr(riid, riidPtr, false);
            var vtable = Marshal.ReadIntPtr(shellFolder);
            var fnPtr = Marshal.ReadIntPtr(vtable, IntPtr.Size * 8);
            var fn = Marshal.GetDelegateForFunctionPointer<CreateViewObjectFn>(fnPtr);
            return fn(shellFolder, 0, riidPtr, out ppv);
        }
        finally { Marshal.FreeCoTaskMem(riidPtr); }
    }
}
