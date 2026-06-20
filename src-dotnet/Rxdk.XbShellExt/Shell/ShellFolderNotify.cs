using Rxdk.XbShellExt.Interop;

namespace Rxdk.XbShellExt.Shell;

internal static class ShellFolderNotify
{
    public static void NotifyItemRemoved(nint absolutePidl, bool isDirectory)
    {
        if (absolutePidl == 0)
            return;

        var notifyPidl = PidlHelper.Clone(absolutePidl);
        try
        {
            NativeMethods.SHChangeNotify(
                isDirectory ? NativeMethods.ShChangeNotifyRmdir : NativeMethods.ShChangeNotifyDelete,
                NativeMethods.ShcnfIdList | NativeMethods.ShcnfFlush,
                notifyPidl,
                0);
        }
        finally
        {
            PidlHelper.Free(notifyPidl);
        }
    }

    public static void RefreshFolder(nint folderPidl)
    {
        if (folderPidl == 0)
            return;

        var notifyPidl = PidlHelper.Clone(folderPidl);
        try
        {
            NativeMethods.SHChangeNotify(
                NativeMethods.ShChangeNotifyUpdatedir,
                NativeMethods.ShcnfFlush,
                notifyPidl,
                0);
        }
        finally
        {
            PidlHelper.Free(notifyPidl);
        }
    }

    public static void RefreshDisplayFolder(string displayPath)
    {
        if (string.IsNullOrWhiteSpace(displayPath))
            return;

        var pidl = PidlHelper.BuildNamespaceRelativePidl(displayPath);
        if (pidl == 0)
            return;

        try
        {
            NativeMethods.SHChangeNotify(
                NativeMethods.ShChangeNotifyUpdatedir,
                NativeMethods.ShcnfFlush,
                pidl,
                0);
        }
        finally
        {
            PidlHelper.Free(pidl);
        }
    }
}
