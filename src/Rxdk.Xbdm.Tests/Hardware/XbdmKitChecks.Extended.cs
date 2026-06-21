using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private const string ExtendedCategory = "Extended";

    public static IReadOnlyList<KitCheckResult> RunExtendedChecks(XbdmKitSession session)
    {
        return
        [
            SafeCheck(ExtendedCategory, () => CompareThreadInfo(session)),
            SafeCheck(ExtendedCategory, () => CompareStopOnToggle(session)),
            SafeCheck(ExtendedCategory, () => CompareMonitorFrameBuffer(session)),
            SafeCheck(ExtendedCategory, () => CompareNotificationExecStop(session)),
            SafeCheck(ExtendedCategory, () => CompareEnableGpuCounter(session)),
        ];
    }

    public static IReadOnlyList<KitCheckResult> RunRebootChecks()
    {
        if (!AllowRebootTests())
        {
            return
            [
                KitCheck.Skip(
                    ConnectionCategory,
                    "Reboot(warm+wait)",
                    "Set RXDK_KIT_ALLOW_REBOOT=1 — reboots the kit (~2 min). No interaction needed."),
            ];
        }

        return [CompareRebootWarmWait()];
    }

    internal static string FormatModuleSections(IReadOnlyList<XbdmSectionLoadNotification> sections) =>
        string.Join(';', sections.OrderBy(s => s.Index).Select(s => $"{s.Name}@0x{s.BaseAddress:x}+0x{s.Size:x}"));

    internal static KitCheckResult CompareModuleSectionsForTitle(
        XbdmKitSession session,
        string moduleName) =>
        KitCheck.ManagedCheck(
            ExtendedCategory,
            "WalkModuleSections",
            () => FormatModuleSections(session.ManagedDebug.WalkModuleSections(moduleName)));

    internal static KitCheckResult CompareModuleLongNameForTitle(
        XbdmKitSession session,
        string moduleName) =>
        KitCheck.ManagedCheck(
            ExtendedCategory,
            "GetModuleLongName",
            () => session.ManagedDebug.GetModuleLongName(moduleName));

    internal static KitCheckResult CompareRunningXbeInfo(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            ExtendedCategory,
            "Debug.GetXbeInfo(default)",
            () => session.ManagedDebug.GetXbeInfo(string.Empty),
            info => $"ts=0x{info.TimeStamp:x} sum=0x{info.CheckSum:x} stack=0x{info.StackSize:x}");

    private static KitCheckResult CompareThreadInfo(XbdmKitSession session)
    {
        var threadId = session.ManagedDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return KitCheck.Skip(ExtendedCategory, "GetThreadInfo", "No threads.");

        var info = session.ManagedDebug.GetThreadInfo(threadId);
        return KitCheck.Pass(
            ExtendedCategory,
            "GetThreadInfo",
            $"tid={threadId} suspend={info.SuspendCount} pri={info.Priority} tls=0x{info.TlsBase:x}");
    }

    private static KitCheckResult CompareStopOnToggle(XbdmKitSession session) =>
        KitCheck.ManagedAction(
            ExtendedCategory,
            "StopOn(toggle)",
            () =>
            {
                session.ManagedDebug.StopOn(XbdmDebugConstants.DmstopDebugStr, stop: true);
                session.ManagedDebug.StopOn(XbdmDebugConstants.DmstopDebugStr, stop: false);
            });

    internal static KitCheckResult CompareQueryPerformanceCounterForRunningTitle(XbdmKitSession session)
    {
        var counter = session.ManagedDebug.WalkPerformanceCounters().FirstOrDefault();
        if (counter is null)
            return KitCheck.Fail(ExtendedCategory, "QueryPerformanceCounter", "No performance counters.");

        try
        {
            session.ManagedDebug.EnableGpuCounter(true);
            var data = session.ManagedDebug.QueryPerformanceCounter(counter.Name, counter.Type);
            return KitCheck.Pass(
                ExtendedCategory,
                "QueryPerformanceCounter",
                $"type={data.CountType} val={data.CountValue} rate={data.RateValue}");
        }
        catch (Exception ex)
        {
            return KitCheck.Fail(ExtendedCategory, "QueryPerformanceCounter", ex.Message);
        }
        finally
        {
            try
            {
                session.ManagedDebug.EnableGpuCounter(false);
            }
            catch (XbdmException)
            {
            }
        }
    }

    private static KitCheckResult CompareMonitorFrameBuffer(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            ExtendedCategory,
            "MonitorFrameBuffer",
            () => session.ManagedDebug.MonitorFrameBuffer(),
            buffer => $"{buffer.Length} bytes");

    internal static KitCheckResult ComparePixelShaderSnapshot(XbdmKitSession session)
    {
        try
        {
            var length = session.ManagedDebug.PixelShaderSnapshot(0, 0, 0, 0).Length;
            return length > 0
                ? KitCheck.Pass(ExtendedCategory, "PixelShaderSnapshot", $"{length} bytes")
                : KitCheck.Fail(ExtendedCategory, "PixelShaderSnapshot", "Empty snapshot buffer.");
        }
        catch (Exception ex)
        {
            return KitCheck.Fail(ExtendedCategory, "PixelShaderSnapshot", ex.Message);
        }
    }

    private static KitCheckResult CompareNotificationExecStop(XbdmKitSession session)
    {
        using var notify = session.ManagedDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent);
        var seen = new ManualResetEventSlim(false);
        notify.Notify(XbdmDebugConstants.DmExec, (_, _) => seen.Set());
        session.ManagedDebug.Stop();

        return seen.Wait(TimeSpan.FromSeconds(10))
            ? KitCheck.Pass(ExtendedCategory, "Notification(DmExec)", "received exec stop")
            : KitCheck.Fail(ExtendedCategory, "Notification(DmExec)", "Exec notification not received.");
    }

    private static KitCheckResult CompareEnableGpuCounter(XbdmKitSession session) =>
        KitCheck.ManagedAction(
            ExtendedCategory,
            "EnableGpuCounter(toggle)",
            () =>
            {
                session.ManagedDebug.EnableGpuCounter(true);
                session.ManagedDebug.EnableGpuCounter(false);
            });

    private static KitCheckResult CompareRebootWarmWait()
    {
        var console = Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(console))
            return KitCheck.Skip(ConnectionCategory, "Reboot(warm+wait)", "RXDK_TEST_CONSOLE not set.");

        using var client = new ManagedXbdmClient();
        client.Initialize();

        try
        {
            KitTestProgress.Phase("Reboot: sending warm reboot (no WAIT — polling for kit)");
            using (var connection = XbdmKitSession.Connect(client, console))
            {
                connection.Debug.SetConnectionTimeout(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(10));
                connection.Reboot(XbdmDebugConstants.DmbootWarm);
            }

            KitTestProgress.Phase("Reboot: command sent, waiting for kit to return");
            Thread.Sleep(TimeSpan.FromSeconds(2));

            var rebootTimeout = TimeSpan.FromSeconds(KitTestConfig.TimeoutSeconds("REBOOT_TIMEOUT_SEC", 90));
            var probeTimeout = TimeSpan.FromSeconds(2);

            if (!XbdmKitWait.Until(
                    () => XbdmKitSession.TryProbe(console, probeTimeout),
                    rebootTimeout,
                    progressLabel: "Waiting for kit after reboot"))
            {
                return KitCheck.Fail(
                    ConnectionCategory,
                    "Reboot(warm+wait)",
                    $"Kit did not come back within {rebootTimeout.TotalSeconds:F0}s.");
            }

            KitTestProgress.Phase("Reboot: kit is back, listing drives");
            using var after = XbdmKitSession.Connect(client, console);
            var drives = string.Join(',', after.ListDrives().OrderBy(c => c));
            return KitCheck.Pass(ConnectionCategory, "Reboot(warm+wait)", drives);
        }
        catch (XbdmException ex)
        {
            return KitCheck.Fail(ConnectionCategory, "Reboot(warm+wait)", ex.Message);
        }
    }
}
