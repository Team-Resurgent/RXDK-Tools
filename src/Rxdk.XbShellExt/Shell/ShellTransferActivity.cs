namespace Rxdk.XbShellExt.Shell;

using System.Runtime.InteropServices;

internal static class ShellTransferActivity
{
    public static void Begin() => NativeMethods.XbShellExt_BeginTransferActivity();

    public static void End() => NativeMethods.XbShellExt_EndTransferActivity();

    private static class NativeMethods
    {
        [DllImport("Rxdk.XbShellExt.Shell.dll")]
        internal static extern void XbShellExt_BeginTransferActivity();

        [DllImport("Rxdk.XbShellExt.Shell.dll")]
        internal static extern void XbShellExt_EndTransferActivity();
    }
}
