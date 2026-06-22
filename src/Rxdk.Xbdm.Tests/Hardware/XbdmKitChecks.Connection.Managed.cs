using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private static KitCheckResult SyncConsoleTime(XbdmKitSession session)
    {
        try
        {
            KitTestProgress.Phase("Syncing kit clock to local UTC");
            if (session.Managed is ManagedXbdmConnection managed)
                managed.SyncConsoleClock();
            else
                SendSetSystemTime(session.ManagedDebug);

            Thread.Sleep(500);
            var kitTime = session.ManagedDebug.GetSystemTime();
            var delta = Math.Abs((kitTime - DateTime.UtcNow).TotalSeconds);
            if (delta > 5)
            {
                return KitCheck.Fail(
                    ConnectionCategory,
                    "SyncConsoleTime",
                    "Kit clock diverged from local time after setsystime.",
                    kitTime.ToString("O"));
            }

            return KitCheck.Pass(ConnectionCategory, "SyncConsoleTime", $"delta={delta:F1}s");
        }
        catch (Exception ex)
        {
            return KitCheck.Fail(ConnectionCategory, "SyncConsoleTime", ex.Message);
        }
    }

    private static void SendSetSystemTime(IXbdmDebugConnection debug)
    {
        var ft = DateTime.UtcNow.ToFileTimeUtc();
        var high = (uint)(ft >> 32);
        var low = (uint)ft;
        debug.SendCommand($"setsystime clockhi=0x{high:x8} clocklo=0x{low:x8}");
    }

    private static KitCheckResult CompareDrives(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            ConnectionCategory,
            "ListDrives",
            () => session.Managed.ListDrives().OrderBy(c => c).ToArray(),
            drives => string.Join(',', drives));

    private static KitCheckResult CompareListDirectory(XbdmKitSession session)
    {
        var drive = XbdmKitSession.PickScratchDrive(session.Managed);
        if (drive == default)
            return KitCheck.Skip(ConnectionCategory, "ListDirectory", "No drives.");

        var path = $"{drive}:\\";
        return KitCheck.ManagedCheck(
            ConnectionCategory,
            "ListDirectory",
            () => session.Managed.ListDirectory(path).OrderBy(e => e.Name).ToArray(),
            entries => $"{entries.Length} entries at {path}");
    }

    private static KitCheckResult CompareGetFileAttributes(XbdmKitSession session)
    {
        var drive = XbdmKitSession.PickScratchDrive(session.Managed);
        if (drive == default)
            return KitCheck.Skip(ConnectionCategory, "GetFileAttributes", "No drives.");

        var path = $"{drive}:\\";
        return KitCheck.ManagedCheck(
            ConnectionCategory,
            "GetFileAttributes",
            () => session.Managed.GetFileAttributes(path),
            attr => $"{attr.Name} attr=0x{attr.Attributes:x} size={attr.Size}");
    }

    private static KitCheckResult CompareDiskFreeSpace(XbdmKitSession session)
    {
        var drive = XbdmKitSession.PickScratchDrive(session.Managed);
        if (drive == default)
            return KitCheck.Skip(ConnectionCategory, "GetDiskFreeSpace", "No drives.");

        var path = $"{drive}:\\";
        return KitCheck.ManagedCheck(
            ConnectionCategory,
            "GetDiskFreeSpace",
            () => session.Managed.GetDiskFreeSpace(path),
            space => $"free={space.FreeBytes} total={space.TotalBytes}");
    }

    private static KitCheckResult CompareResolveAddress(XbdmKitSession session)
    {
        if (System.Net.IPAddress.TryParse(Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE"), out _))
            return KitCheck.Skip(ConnectionCategory, "TryResolveXboxAddress", "Console is already an IP address.");

        return KitCheck.ManagedCheck(
            ConnectionCategory,
            "TryResolveXboxAddress",
            () => session.Managed.TryResolveXboxAddress(),
            addr => addr?.ToString() ?? "(null)");
    }

    private static KitCheckResult CompareAltAddress(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            ConnectionCategory,
            "TryGetAltAddress",
            () => session.Managed.TryGetAltAddress(),
            addr => addr?.ToString() ?? "(null)");

    private static KitCheckResult CompareNameOfXbox(XbdmKitSession session) =>
        CompareNameOfXboxCore(session, resolvable: true, "GetNameOfXbox(resolvable)");

    private static KitCheckResult CompareNameOfXboxLocal(XbdmKitSession session) =>
        CompareNameOfXboxCore(session, resolvable: false, "GetNameOfXbox(local)");

    private static KitCheckResult CompareNameOfXboxCore(
        XbdmKitSession session,
        bool resolvable,
        string checkName) =>
        KitCheck.ManagedCheck(
            ConnectionCategory,
            checkName,
            () => session.Managed.GetNameOfXbox(resolvable));

    private static KitCheckResult CompareSecurityEnabled(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            ConnectionCategory,
            "IsSecurityEnabled",
            () => session.Managed.IsSecurityEnabled(),
            locked => locked ? "locked" : "unlocked");

    private static KitCheckResult CompareSupportsUserPrivileges(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            ConnectionCategory,
            "SupportsUserPrivileges",
            () => session.Managed.SupportsUserPrivileges(),
            supported => supported ? "supported" : "unsupported");

    private static KitCheckResult CompareUserAccess(XbdmKitSession session)
    {
        if (!session.Managed.SupportsUserPrivileges())
            return KitCheck.Skip(ConnectionCategory, "GetUserAccess", "User privileges not supported.");
        if (!session.Managed.IsSecurityEnabled())
            return KitCheck.Skip(ConnectionCategory, "GetUserAccess", "Kit unlocked; checked during Security suite.");

        return KitCheck.ManagedCheck(
            ConnectionCategory,
            "GetUserAccess",
            () => session.Managed.GetUserAccess(),
            access => $"0x{access:x}");
    }

    private static KitCheckResult CompareScreenshot(XbdmKitSession session)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xbdm-managed-{Guid.NewGuid():N}.bmp");
        try
        {
            return KitCheck.ManagedCheck(
                ConnectionCategory,
                "CaptureScreenshot",
                () =>
                {
                    session.Managed.CaptureScreenshot(path);
                    var bmp = BmpTestHelper.ReadInfo(path);
                    return $"{bmp.Width}x{bmp.Height} {bmp.BitCount}bpp hash=0x{bmp.PixelHash:x8}";
                });
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static KitCheckResult CompareUseSharedConnection(XbdmKitSession session) =>
        KitCheck.ManagedAction(
            ConnectionCategory,
            "UseSharedConnection",
            () =>
            {
                session.Managed.UseSharedConnection(true);
                session.Managed.UseSharedConnection(false);
            });
}
