using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    private const string ConnectionCategory = "Connection";
    private const string FileCategory = "File";
    private const string DebugCategory = "Debug";
    private const string LaunchCategory = "Launch";

    public static ParityReportSummary RunAll(string console)
    {
        if (!XbdmParitySession.TryCreate(console, out var session, out var skipReason))
        {
            return new ParityReportSummary
            {
                ConsoleName = console,
                PasswordConfigured = HasPassword(),
                Results = [ParityCompare.Skip("Session", "Open", skipReason)],
            };
        }

        var results = new List<ParityCheckResult>();

        using (session)
        {
            results.AddRange(RunSuite("Connection", () => RunConnectionChecks(session)));
            results.AddRange(RunSuite("File", () => RunFileChecks(session)));
            results.AddRange(RunSuite("Debug", () => RunDebugChecks(session)));
            results.AddRange(RunSuite("Launch", () => RunLaunchChecks(session)));
            results.AddRange(RunSuite("Execution", () => RunExecutionChecks(session)));
            results.AddRange(RunSuite("Extended", () => RunExtendedChecks(session)));
            results.AddRange(RunSuite("Bridge", () => RunBridgeChecks(console, session)));
            results.AddRange(RunSuite("Security", () => RunSecurityChecks(session)));
        }

        results.AddRange(RunSuite("Reboot", RunRebootChecks));

        return new ParityReportSummary
        {
            ConsoleName = console,
            PasswordConfigured = HasPassword(),
            Results = results,
        };
    }

    public static IReadOnlyList<ParityCheckResult> RunConnectionChecks(XbdmParitySession session)
    {
        var results = new List<ParityCheckResult>
        {
            SyncConsoleTime(session),
            SafeCheck(ConnectionCategory, () => CompareDrives(session)),
            SafeCheck(ConnectionCategory, () => CompareListDirectory(session)),
            SafeCheck(ConnectionCategory, () => CompareGetFileAttributes(session)),
            SafeCheck(ConnectionCategory, () => CompareDiskFreeSpace(session)),
            SafeCheck(ConnectionCategory, () => CompareResolveAddress(session)),
            SafeCheck(ConnectionCategory, () => CompareAltAddress(session)),
            SafeCheck(ConnectionCategory, () => CompareNameOfXbox(session)),
            SafeCheck(ConnectionCategory, () => CompareNameOfXboxLocal(session)),
            SafeCheck(ConnectionCategory, () => CompareSecurityEnabled(session)),
            SafeCheck(ConnectionCategory, () => CompareSupportsUserPrivileges(session)),
            SafeCheck(ConnectionCategory, () => CompareUserAccess(session)),
            SafeCheck(ConnectionCategory, () => CompareScreenshot(session)),
            SafeCheck(ConnectionCategory, () => CompareUseSharedConnection(session)),
        };
        return results;
    }

    public static IReadOnlyList<ParityCheckResult> RunSecurityChecks(XbdmParitySession session)
    {
        if (!AllowSecurityTests())
        {
            return
            [
                ParityCompare.Skip(
                    SecurityCategory,
                    "SecurityRoundTrip",
                    "Set RXDK_PARITY_ALLOW_SECURITY=1 — locks kit briefly, then restores unlocked."),
            ];
        }

        return RunSecurityRoundTrip(session).ToList();
    }

    public static IReadOnlyList<ParityCheckResult> RunFileChecks(XbdmParitySession session)
    {
        var results = new List<ParityCheckResult>();
        try
        {
            results.AddRange(ExecuteFileRoundTrip(session));
        }
        catch (Exception ex)
        {
            results.Add(ParityCompare.Fail(FileCategory, "FileRoundTrip", ex.Message));
        }

        return results;
    }

    public static IReadOnlyList<ParityCheckResult> RunDebugChecks(XbdmParitySession session)
    {
        return
        [
            SafeCheck(() => CompareThreadList(session)),
            SafeCheck(() => CompareLoadedModules(session)),
            SafeCheck(() => CompareSystemTime(session)),
            SafeCheck(() => CompareXtlData(session)),
            SafeCheck(() => CompareSendCommandSetsystime(session)),
            SafeCheck(() => CompareNotificationSession(session)),
            SafeCheck(() => ComparePerformanceCounterList(session)),
            SafeCheck(() => CompareMemoryAtModuleBase(session)),
            SafeCheck(() => CompareBreakpointQuery(session)),
        ];
    }

    private static ParityCheckResult SafeCheck(Func<ParityCheckResult> check) =>
        SafeCheck(DebugCategory, check);

    private static ParityCheckResult SafeCheck(string category, Func<ParityCheckResult> check)
    {
        try
        {
            return check();
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Fail(category, "Unhandled", ex.Message);
        }
    }

    public static IReadOnlyList<ParityCheckResult> RunLaunchChecks(XbdmParitySession session)
    {
        var results = new List<ParityCheckResult> { CompareUploadTriangleXbe(session) };
        if (AllowLaunchTests())
            results.Add(CompareTriangleXbeInfo(session));
        else
            results.Add(ParityCompare.Skip(
                LaunchCategory,
                "GetXbeInfo",
                "Set RXDK_PARITY_ALLOW_LAUNCH=1 or RXDK_PARITY_ALLOW_EXEC=1."));
        return results;
    }

    private static IEnumerable<ParityCheckResult> RunSuite(string name, Func<IReadOnlyList<ParityCheckResult>> runner)
    {
        ParityProgress.Phase($"=== {name} ===");
        IReadOnlyList<ParityCheckResult> results;
        try
        {
            results = runner();
        }
        catch (Exception ex)
        {
            results = [ParityCompare.Fail("Session", name, ex.Message)];
        }

        foreach (var result in results)
        {
            ParityProgress.Result(result);
            yield return result;
        }

        ParityProgress.Phase($"=== {name} done ===");
    }

    private static ParityCheckResult SyncConsoleTime(XbdmParitySession session)
    {
        try
        {
            ParityProgress.Phase("Syncing kit clock to local UTC");
            if (session.Managed is ManagedXbdmConnection managed)
                managed.SyncConsoleClock();
            else
                SendSetSystemTime(session.ManagedDebug);

            // Native file I/O uses a separate xbdm.dll SCI; setsystime is global on the kit.
            SendSetSystemTime(session.NativeDebug);

            Thread.Sleep(500);
            session.RefreshNativeFileTimeCorrection();

            var nativeTime = session.NativeDebug.GetSystemTime();
            var managedTime = session.ManagedDebug.GetSystemTime();
            var delta = Math.Abs((nativeTime - managedTime).TotalSeconds);
            if (delta > 5)
            {
                return ParityCompare.Fail(
                    ConnectionCategory,
                    "SyncConsoleTime",
                    "Kit clock diverged from local time after setsystime.",
                    nativeTime.ToString("O"),
                    managedTime.ToString("O"));
            }

            return ParityCompare.Pass(ConnectionCategory, "SyncConsoleTime", $"delta={delta:F1}s");
        }
        catch (Exception ex)
        {
            return ParityCompare.Fail(ConnectionCategory, "SyncConsoleTime", ex.Message);
        }
    }

    private static void SendSetSystemTime(IXbdmDebugConnection debug)
    {
        var ft = DateTime.UtcNow.ToFileTimeUtc();
        var high = (uint)(ft >> 32);
        var low = (uint)ft;
        debug.SendCommand($"setsystime clockhi=0x{high:x8} clocklo=0x{low:x8}");
    }

    private static ParityCheckResult CompareDrives(XbdmParitySession session)
    {
        var native = session.Native.ListDrives().OrderBy(c => c).ToArray();
        var managed = session.Managed.ListDrives().OrderBy(c => c).ToArray();
        if (native.SequenceEqual(managed))
            return ParityCompare.Pass(ConnectionCategory, "ListDrives", string.Join(',', native));
        return ParityCompare.Fail(
            ConnectionCategory,
            "ListDrives",
            "Drive lists differ.",
            string.Join(',', native),
            string.Join(',', managed));
    }

    private static ParityCheckResult CompareListDirectory(XbdmParitySession session)
    {
        var drive = XbdmParitySession.PickScratchDrive(session.Native);
        if (drive == default)
            return ParityCompare.Skip(ConnectionCategory, "ListDirectory", "No drives.");

        var path = $"{drive}:\\";
        var native = session.Native.ListDirectory(path).OrderBy(e => e.Name).ToArray();
        var managed = session.Managed.ListDirectory(path).OrderBy(e => e.Name).ToArray();
        if (native.Length != managed.Length)
        {
            return ParityCompare.Fail(
                ConnectionCategory,
                "ListDirectory",
                "Entry counts differ.",
                native.Length.ToString(),
                managed.Length.ToString());
        }

        for (var i = 0; i < native.Length; i++)
        {
            if (native[i].Name != managed[i].Name ||
                native[i].Attributes != managed[i].Attributes ||
                native[i].Size != managed[i].Size ||
                native[i].ChangeTimeUnix != managed[i].ChangeTimeUnix)
            {
                return ParityCompare.Fail(
                    ConnectionCategory,
                    "ListDirectory",
                    $"Mismatch at index {i}.",
                    $"{native[i].Name} attr=0x{native[i].Attributes:x} size={native[i].Size} chg={native[i].ChangeTimeUnix}",
                    $"{managed[i].Name} attr=0x{managed[i].Attributes:x} size={managed[i].Size} chg={managed[i].ChangeTimeUnix}");
            }
        }

        return ParityCompare.Pass(ConnectionCategory, "ListDirectory", $"{native.Length} entries at {path}");
    }

    private static ParityCheckResult CompareGetFileAttributes(XbdmParitySession session)
    {
        var drive = XbdmParitySession.PickScratchDrive(session.Native);
        if (drive == default)
            return ParityCompare.Skip(ConnectionCategory, "GetFileAttributes", "No drives.");

        var path = $"{drive}:\\";
        var native = session.Native.GetFileAttributes(path);
        var managed = session.Managed.GetFileAttributes(path);
        if (native.Name == managed.Name &&
            native.Attributes == managed.Attributes &&
            native.Size == managed.Size &&
            native.ChangeTimeUnix == managed.ChangeTimeUnix)
            return ParityCompare.Pass(ConnectionCategory, "GetFileAttributes", path);

        return ParityCompare.Fail(
            ConnectionCategory,
            "GetFileAttributes",
            native.ChangeTimeUnix == managed.ChangeTimeUnix
                ? "Attributes differ."
                : "Change time differs.",
            $"{native.Name} attr=0x{native.Attributes:x} size={native.Size} chg={native.ChangeTimeUnix}",
            $"{managed.Name} attr=0x{managed.Attributes:x} size={managed.Size} chg={managed.ChangeTimeUnix}");
    }

    private static ParityCheckResult CompareDiskFreeSpace(XbdmParitySession session)
    {
        var drive = XbdmParitySession.PickScratchDrive(session.Native);
        if (drive == default)
            return ParityCompare.Skip(ConnectionCategory, "GetDiskFreeSpace", "No drives.");

        var path = $"{drive}:\\";
        var native = session.Native.GetDiskFreeSpace(path);
        var managed = session.Managed.GetDiskFreeSpace(path);
        if (native.FreeBytes == managed.FreeBytes && native.TotalBytes == managed.TotalBytes)
            return ParityCompare.Pass(ConnectionCategory, "GetDiskFreeSpace", path);

        return ParityCompare.Fail(
            ConnectionCategory,
            "GetDiskFreeSpace",
            "Disk space differs.",
            $"free={native.FreeBytes} total={native.TotalBytes}",
            $"free={managed.FreeBytes} total={managed.TotalBytes}");
    }

    private static ParityCheckResult CompareResolveAddress(XbdmParitySession session)
    {
        if (System.Net.IPAddress.TryParse(Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE"), out _))
            return ParityCompare.Skip(ConnectionCategory, "TryResolveXboxAddress", "Console is already an IP address.");

        return ParityCompare.Equal(
            ConnectionCategory,
            "TryResolveXboxAddress",
            session.Native.TryResolveXboxAddress(),
            session.Managed.TryResolveXboxAddress());
    }

    private static ParityCheckResult CompareAltAddress(XbdmParitySession session) =>
        ParityCompare.Equal(
            ConnectionCategory,
            "TryGetAltAddress",
            session.Native.TryGetAltAddress(),
            session.Managed.TryGetAltAddress());

    private static ParityCheckResult CompareNameOfXbox(XbdmParitySession session) =>
        CompareNameOfXboxCore(session, resolvable: true, "GetNameOfXbox(resolvable)");

    private static ParityCheckResult CompareNameOfXboxLocal(XbdmParitySession session) =>
        CompareNameOfXboxCore(session, resolvable: false, "GetNameOfXbox(local)");

    private static ParityCheckResult CompareNameOfXboxCore(
        XbdmParitySession session,
        bool resolvable,
        string checkName)
    {
        XbdmException? nativeError = null;
        XbdmException? managedError = null;
        string? nativeValue = null;
        string? managedValue = null;

        try
        {
            nativeValue = session.Native.GetNameOfXbox(resolvable);
        }
        catch (XbdmException ex)
        {
            nativeError = ex;
        }

        try
        {
            managedValue = session.Managed.GetNameOfXbox(resolvable);
        }
        catch (XbdmException ex)
        {
            managedError = ex;
        }

        if (nativeError is not null && managedError is not null)
        {
            if (nativeError.HResultCode == managedError.HResultCode)
                return ParityCompare.Pass(ConnectionCategory, checkName, ParityCompare.BothThrewNote(nativeError.HResultCode));
            return ParityCompare.Fail(
                ConnectionCategory,
                checkName,
                "Both threw but HRESULT differs.",
                ParityCompare.FormatHResult(nativeError.HResultCode, nativeError.Message),
                ParityCompare.FormatHResult(managedError.HResultCode, managedError.Message));
        }

        if (nativeError is not null || managedError is not null)
        {
            return ParityCompare.Fail(
                ConnectionCategory,
                checkName,
                "Only one side threw.",
                ParityCompare.FormatSide(nativeError, nativeValue),
                ParityCompare.FormatSide(managedError, managedValue));
        }

        if (string.Equals(nativeValue, managedValue, StringComparison.OrdinalIgnoreCase))
            return ParityCompare.Pass(ConnectionCategory, checkName, nativeValue);

        return ParityCompare.Fail(
            ConnectionCategory,
            checkName,
            "Names differ.",
            nativeValue,
            managedValue);
    }

    private static ParityCheckResult CompareSecurityEnabled(XbdmParitySession session) =>
        ParityCompare.Equal(
            ConnectionCategory,
            "IsSecurityEnabled",
            session.Native.IsSecurityEnabled(),
            session.Managed.IsSecurityEnabled());

    private static ParityCheckResult CompareSupportsUserPrivileges(XbdmParitySession session) =>
        ParityCompare.Equal(
            ConnectionCategory,
            "SupportsUserPrivileges",
            session.Native.SupportsUserPrivileges(),
            session.Managed.SupportsUserPrivileges());

    private static ParityCheckResult CompareUserAccess(XbdmParitySession session)
    {
        if (!session.Native.SupportsUserPrivileges())
            return ParityCompare.Skip(ConnectionCategory, "GetUserAccess", "User privileges not supported.");

        return ParityCompare.Equal(
            ConnectionCategory,
            "GetUserAccess",
            session.Native.GetUserAccess(),
            session.Managed.GetUserAccess());
    }

    internal static ParityCheckResult CompareListUsersParity(
        XbdmParitySession session,
        string category,
        string step)
    {
        if (!session.Native.SupportsUserPrivileges())
            return ParityCompare.Skip(category, step, "User privileges not supported.");

        if (!session.Native.IsSecurityEnabled())
        {
            return ParityCompare.Skip(
                category,
                step,
                "Kit is unlocked; USERLIST requires lock mode (see Security/ListUsers(locked)).");
        }

        var native = session.Native.ListUsers().OrderBy(u => u.UserName).ToArray();
        var managed = session.Managed.ListUsers().OrderBy(u => u.UserName).ToArray();
        if (native.Length != managed.Length)
        {
            return ParityCompare.Fail(
                category,
                step,
                "User counts differ.",
                native.Length.ToString(),
                managed.Length.ToString());
        }

        for (var i = 0; i < native.Length; i++)
        {
            if (native[i].UserName != managed[i].UserName ||
                native[i].AccessPrivileges != managed[i].AccessPrivileges)
            {
                return ParityCompare.Fail(
                    category,
                    step,
                    $"Mismatch at index {i}.",
                    $"{native[i].UserName} 0x{native[i].AccessPrivileges:x}",
                    $"{managed[i].UserName} 0x{managed[i].AccessPrivileges:x}");
            }
        }

        return ParityCompare.Pass(category, step, $"{native.Length} users");
    }

    private static ParityCheckResult CompareScreenshot(XbdmParitySession session)
    {
        var nativePath = Path.Combine(Path.GetTempPath(), $"xbdm-native-{Guid.NewGuid():N}.bmp");
        var managedPath = Path.Combine(Path.GetTempPath(), $"xbdm-managed-{Guid.NewGuid():N}.bmp");
        try
        {
            session.Native.CaptureScreenshot(nativePath);
            session.Managed.CaptureScreenshot(managedPath);
            var nativeBmp = BmpTestHelper.ReadInfo(nativePath);
            var managedBmp = BmpTestHelper.ReadInfo(managedPath);
            if (nativeBmp.Width == managedBmp.Width &&
                nativeBmp.Height == managedBmp.Height &&
                nativeBmp.BitCount == managedBmp.BitCount &&
                nativeBmp.PixelDataSize == managedBmp.PixelDataSize &&
                nativeBmp.PixelHash == managedBmp.PixelHash)
            {
                return ParityCompare.Pass(
                    ConnectionCategory,
                    "CaptureScreenshot",
                    $"{nativeBmp.Width}x{nativeBmp.Height} {nativeBmp.BitCount}bpp hash=0x{nativeBmp.PixelHash:x8}");
            }

            return ParityCompare.Fail(
                ConnectionCategory,
                "CaptureScreenshot",
                "Bitmap metadata or pixels differ.",
                $"{nativeBmp.Width}x{nativeBmp.Height} hash=0x{nativeBmp.PixelHash:x8}",
                $"{managedBmp.Width}x{managedBmp.Height} hash=0x{managedBmp.PixelHash:x8}");
        }
        finally
        {
            if (File.Exists(nativePath))
                File.Delete(nativePath);
            if (File.Exists(managedPath))
                File.Delete(managedPath);
        }
    }

    private static ParityCheckResult CompareUseSharedConnection(XbdmParitySession session) =>
        ParityCompare.BothAction(
            ConnectionCategory,
            "UseSharedConnection",
            () =>
            {
                session.Native.UseSharedConnection(true);
                session.Native.UseSharedConnection(false);
            },
            () =>
            {
                session.Managed.UseSharedConnection(true);
                session.Managed.UseSharedConnection(false);
            });
}
