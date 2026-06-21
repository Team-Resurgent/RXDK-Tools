using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Ui;

internal static class ShellNotify
{
    private const uint ShChangeNotifyUpdatedir = 0x00001000;
    private const uint ShcnfPath = 0x0001;
    private const uint ShcnfFlush = 0x1000;

    // Matches native ROOT_GUID_NAME_WIDE in src/xbshlext/xbfolder.h
    private const string RootNamespacePath = "::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    public static void NotifyFolderTreeChanged() => NotifyRootNamespaceUpdated();

    public static void NotifyFolderContentsChanged() => NotifyRootNamespaceUpdated();

    private static void NotifyRootNamespaceUpdated()
    {
        var pathPtr = Marshal.StringToCoTaskMemUni(RootNamespacePath);
        try
        {
            SHChangeNotify(ShChangeNotifyUpdatedir, ShcnfPath | ShcnfFlush, pathPtr, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
