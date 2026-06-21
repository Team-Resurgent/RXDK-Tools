using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private const string ConnectionCategory = "Connection";
    private const string FileCategory = "File";
    private const string DebugCategory = "Debug";
    private const string LaunchCategory = "Launch";

    public static KitTestReportSummary RunAll(string console)
    {
        if (!XbdmKitSession.TryCreate(console, out var session, out var skipReason))
        {
            return new KitTestReportSummary
            {
                ConsoleName = console,
                PasswordConfigured = HasPassword(),
                Results = [KitCheck.Skip("Session", "Open", skipReason)],
            };
        }

        var results = new List<KitCheckResult>();

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

        return new KitTestReportSummary
        {
            ConsoleName = console,
            PasswordConfigured = HasPassword(),
            Results = results,
        };
    }

    public static IReadOnlyList<KitCheckResult> RunConnectionChecks(XbdmKitSession session)
    {
        var results = new List<KitCheckResult>
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

    public static IReadOnlyList<KitCheckResult> RunSecurityChecks(XbdmKitSession session)
    {
        if (!AllowSecurityTests())
        {
            return
            [
                KitCheck.Skip(
                    SecurityCategory,
                    "SecurityRoundTrip",
                    "Set RXDK_KIT_ALLOW_SECURITY=1 — locks kit briefly, then restores unlocked."),
            ];
        }

        return RunSecurityRoundTrip(session).ToList();
    }

    public static IReadOnlyList<KitCheckResult> RunFileChecks(XbdmKitSession session)
    {
        var results = new List<KitCheckResult>();
        try
        {
            results.AddRange(ExecuteFileRoundTrip(session));
        }
        catch (Exception ex)
        {
            results.Add(KitCheck.Fail(FileCategory, "FileRoundTrip", ex.Message));
        }

        return results;
    }

    public static IReadOnlyList<KitCheckResult> RunDebugChecks(XbdmKitSession session)
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

    private static KitCheckResult SafeCheck(Func<KitCheckResult> check) =>
        SafeCheck(DebugCategory, check);

    private static KitCheckResult SafeCheck(string category, Func<KitCheckResult> check)
    {
        try
        {
            return check();
        }
        catch (XbdmException ex)
        {
            return KitCheck.Fail(category, "Unhandled", ex.Message);
        }
    }

    public static IReadOnlyList<KitCheckResult> RunLaunchChecks(XbdmKitSession session)
    {
        var results = new List<KitCheckResult> { CompareUploadTriangleXbe(session) };
        if (AllowLaunchTests())
            results.Add(CompareTriangleXbeInfo(session));
        else
            results.Add(KitCheck.Skip(
                LaunchCategory,
                "GetXbeInfo",
                "Set RXDK_KIT_ALLOW_LAUNCH=1 or RXDK_KIT_ALLOW_EXEC=1."));
        return results;
    }

    private static IEnumerable<KitCheckResult> RunSuite(string name, Func<IReadOnlyList<KitCheckResult>> runner)
    {
        KitTestProgress.Phase($"=== {name} ===");
        IReadOnlyList<KitCheckResult> results;
        try
        {
            results = runner();
        }
        catch (Exception ex)
        {
            results = [KitCheck.Fail("Session", name, ex.Message)];
        }

        foreach (var result in results)
        {
            KitTestProgress.Result(result);
            yield return result;
        }

        KitTestProgress.Phase($"=== {name} done ===");
    }
}
