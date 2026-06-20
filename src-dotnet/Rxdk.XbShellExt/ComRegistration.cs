using System.Reflection;

using Microsoft.Win32;

using Rxdk.XbShellExt.Com;

using Rxdk.XbShellExt.Interop;



namespace Rxdk.XbShellExt;



public static class ComRegistration

{

    private const string ManagedClsid = ComGuids.XboxFolderManaged;



    public static void Register(Type type)

    {

        var modulePath = ResolveComModulePath();

        RegisterManagedClass(modulePath);

    }



    public static void Unregister(Type type)

    {

        Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{ManagedClsid}", throwOnMissingSubKey: false);

    }



    private static void RegisterManagedClass(string modulePath)

    {

        using var clsid = Registry.ClassesRoot.CreateSubKey($@"CLSID\{ManagedClsid}");

        clsid.SetValue(string.Empty, "Xbox Neighborhood (Managed)");

        using var inproc = clsid.CreateSubKey("InprocServer32");

        inproc.SetValue(string.Empty, modulePath);

        inproc.SetValue("ThreadingModel", "Apartment");

    }



    private static string ResolveComModulePath()

    {

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        var comHost = Path.Combine(assemblyDir, "Rxdk.XbShellExt.comhost.dll");

        if (File.Exists(comHost))

            return comHost;



        return Assembly.GetExecutingAssembly().Location;

    }

}


