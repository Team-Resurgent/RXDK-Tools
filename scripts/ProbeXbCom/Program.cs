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

Marshal.Release(comFolder);
if (viewPtr != 0) Marshal.Release(viewPtr);

return (hrDirect >= 0 && directView != 0) || (hrView >= 0 && viewPtr != 0) ? 0 : 1;

internal static class ProbeNative
{
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint r, int f);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint ctx, ref Guid iid, out nint ppv);
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
