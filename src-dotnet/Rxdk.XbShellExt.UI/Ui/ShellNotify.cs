using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Ui;

internal static class ShellNotify
{
    private const uint ShChangeNotifyMkdir = 0x00000008;
    private const uint ShChangeNotifyUpdatedir = 0x00001000;
    private const uint ShcnfPath = 0x0001;
    private const uint ShcnfFlush = 0x1000;

    [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
    private static extern void SHChangeNotifyInternal(uint eventId, uint flags, nint item1, nint item2);

    public static void NotifyFolderTreeChanged() =>
        SHChangeNotifyInternal(ShChangeNotifyMkdir, ShcnfPath | ShcnfFlush, 0, 0);

    public static void NotifyFolderContentsChanged() =>
        SHChangeNotifyInternal(ShChangeNotifyUpdatedir, ShcnfPath | ShcnfFlush, 0, 0);
}
