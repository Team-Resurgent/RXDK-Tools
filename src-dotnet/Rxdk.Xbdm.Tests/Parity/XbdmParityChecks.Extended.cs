using Rxdk.Native;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    private const string ExtendedCategory = "Extended";

    public static IReadOnlyList<ParityCheckResult> RunExtendedChecks(XbdmParitySession session)
    {
        var results = new List<ParityCheckResult>
        {
            SafeCheck(ExtendedCategory, () => CompareThreadInfo(session)),
            SafeCheck(ExtendedCategory, () => CompareStopOnToggle(session)),
            SafeCheck(ExtendedCategory, () => CompareMonitorFrameBuffer(session)),
            SafeCheck(ExtendedCategory, () => CompareNotificationExecStop(session)),
            SafeCheck(ExtendedCategory, () => CompareEnableGpuCounter(session)),
        };

        return results;
    }

    public static IReadOnlyList<ParityCheckResult> RunRebootChecks()
    {
        if (!AllowRebootTests())
        {
            return
            [
                ParityCompare.Skip(
                    ConnectionCategory,
                    "Reboot(warm+wait)",
                    "Set RXDK_PARITY_ALLOW_REBOOT=1 — reboots the kit (~2 min). No interaction needed."),
            ];
        }

        return [CompareRebootWarmWait()];
    }

    internal static string FormatModuleSections(IReadOnlyList<XbdmSectionLoadNotification> sections) =>
        string.Join(';', sections.OrderBy(s => s.Index).Select(s => $"{s.Name}@0x{s.BaseAddress:x}+0x{s.Size:x}"));

    internal static ParityCheckResult CompareModuleSectionsForTitle(
        XbdmParitySession session,
        string moduleName) =>
        ParityCompare.RequireSuccessOrEqual(
            ExtendedCategory,
            "WalkModuleSections",
            () => FormatModuleSections(session.NativeDebug.WalkModuleSections(moduleName)),
            () => FormatModuleSections(session.ManagedDebug.WalkModuleSections(moduleName)));

    internal static ParityCheckResult CompareModuleLongNameForTitle(
        XbdmParitySession session,
        string moduleName) =>
        ParityCompare.RequireSuccessOrEqual(
            ExtendedCategory,
            "GetModuleLongName",
            () => session.NativeDebug.GetModuleLongName(moduleName),
            () => session.ManagedDebug.GetModuleLongName(moduleName));

    internal static ParityCheckResult CompareRunningXbeInfo(XbdmParitySession session) =>
        ParityCompare.RequireSuccessOrEqual(
            ExtendedCategory,
            "Debug.GetXbeInfo(default)",
            () => session.NativeDebug.GetXbeInfo(string.Empty),
            () => session.ManagedDebug.GetXbeInfo(string.Empty),
            info => $"ts=0x{info.TimeStamp:x} sum=0x{info.CheckSum:x} stack=0x{info.StackSize:x}");

    private static ParityCheckResult CompareThreadInfo(XbdmParitySession session)
    {
        var threadId = session.NativeDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return ParityCompare.Skip(ExtendedCategory, "GetThreadInfo", "No threads.");

        var native = session.NativeDebug.GetThreadInfo(threadId);
        var managed = session.ManagedDebug.GetThreadInfo(threadId);
        if (native.SuspendCount == managed.SuspendCount &&
            native.Priority == managed.Priority &&
            native.TlsBase == managed.TlsBase)
        {
            return ParityCompare.Pass(
                ExtendedCategory,
                "GetThreadInfo",
                $"tid={threadId} suspend={native.SuspendCount}");
        }

        return ParityCompare.Fail(
            ExtendedCategory,
            "GetThreadInfo",
            "Thread info differs.",
            $"suspend={native.SuspendCount} pri={native.Priority} tls=0x{native.TlsBase:x}",
            $"suspend={managed.SuspendCount} pri={managed.Priority} tls=0x{managed.TlsBase:x}");
    }

    private static ParityCheckResult CompareStopOnToggle(XbdmParitySession session) =>
        ParityCompare.BothAction(
            ExtendedCategory,
            "StopOn(toggle)",
            () =>
            {
                session.NativeDebug.StopOn(XbdmDebugConstants.DmstopDebugStr, stop: true);
                session.NativeDebug.StopOn(XbdmDebugConstants.DmstopDebugStr, stop: false);
            },
            () =>
            {
                session.ManagedDebug.StopOn(XbdmDebugConstants.DmstopDebugStr, stop: true);
                session.ManagedDebug.StopOn(XbdmDebugConstants.DmstopDebugStr, stop: false);
            });

    internal static ParityCheckResult CompareQueryPerformanceCounterForRunningTitle(XbdmParitySession session)
    {
        var counter = session.NativeDebug.WalkPerformanceCounters().FirstOrDefault();
        if (counter is null)
        {
            return ParityCompare.Fail(
                ExtendedCategory,
                "QueryPerformanceCounter",
                "No performance counters.");
        }

        try
        {
            session.NativeDebug.EnableGpuCounter(true);
            return ParityCompare.RequireSuccessOrEqual(
                ExtendedCategory,
                "QueryPerformanceCounter",
                () => session.NativeDebug.QueryPerformanceCounter(counter.Name, counter.Type),
                () => session.ManagedDebug.QueryPerformanceCounter(counter.Name, counter.Type),
                data => $"type={data.CountType} val={data.CountValue} rate={data.RateValue}");
        }
        finally
        {
            try
            {
                session.NativeDebug.EnableGpuCounter(false);
            }
            catch (XbdmException)
            {
            }
        }
    }

    private static ParityCheckResult CompareMonitorFrameBuffer(XbdmParitySession session)
    {
        byte[]? native = null;
        byte[]? managed = null;
        XbdmException? nativeError = null;
        XbdmException? managedError = null;

        try
        {
            native = session.NativeDebug.MonitorFrameBuffer();
        }
        catch (XbdmException ex)
        {
            nativeError = ex;
        }

        try
        {
            managed = session.ManagedDebug.MonitorFrameBuffer();
        }
        catch (XbdmException ex)
        {
            managedError = ex;
        }

        if (nativeError is not null && managedError is not null)
        {
            return nativeError.HResultCode == managedError.HResultCode
                ? ParityCompare.Pass(ExtendedCategory, "MonitorFrameBuffer", ParityCompare.BothThrewNote(nativeError.HResultCode))
                : ParityCompare.Fail(
                    ExtendedCategory,
                    "MonitorFrameBuffer",
                    "Both threw but HRESULT differs.",
                    ParityCompare.FormatHResult(nativeError.HResultCode, nativeError.Message),
                    ParityCompare.FormatHResult(managedError.HResultCode, managedError.Message));
        }

        if (nativeError is not null || managedError is not null)
        {
            return ParityCompare.Fail(
                ExtendedCategory,
                "MonitorFrameBuffer",
                "Only one side threw.",
                ParityCompare.FormatSide(nativeError, $"{native!.Length} bytes"),
                ParityCompare.FormatSide(managedError, $"{managed!.Length} bytes"));
        }

        if (native!.Length == managed!.Length)
            return ParityCompare.Pass(ExtendedCategory, "MonitorFrameBuffer", $"{native.Length} bytes");

        var delta = Math.Abs(native.Length - managed.Length);
        if (delta <= Math.Max(64, native.Length / 100))
        {
            return ParityCompare.Pass(
                ExtendedCategory,
                "MonitorFrameBuffer",
                $"sizes within tolerance ({native.Length} vs {managed.Length})");
        }

        return ParityCompare.Fail(
            ExtendedCategory,
            "MonitorFrameBuffer",
            "Framebuffer sizes differ.",
            native.Length.ToString(),
            managed.Length.ToString());
    }

    /// <summary>
    /// PSSnap mutates D3D snapshot state on the kit; only one call succeeds per title run.
    /// </summary>
    internal static ParityCheckResult ComparePixelShaderSnapshot(XbdmParitySession session)
    {
        try
        {
            var length = session.ManagedDebug.PixelShaderSnapshot(0, 0, 0, 0).Length;
            if (length <= 0)
            {
                return ParityCompare.Fail(
                    ExtendedCategory,
                    "PixelShaderSnapshot",
                    "Empty snapshot buffer.");
            }

            return ParityCompare.Pass(
                ExtendedCategory,
                "PixelShaderSnapshot",
                $"{length} bytes",
                backendOverride: ParityBackend.ManagedOnly);
        }
        catch (Exception ex)
        {
            return ParityCompare.Fail(ExtendedCategory, "PixelShaderSnapshot", ex.Message);
        }
    }

    private static ParityCheckResult CompareNotificationExecStop(XbdmParitySession session)
    {
        using var nativeSession = session.NativeDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent);
        using var managedSession = session.ManagedDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent);

        var nativeSeen = new ManualResetEventSlim(false);
        var managedSeen = new ManualResetEventSlim(false);

        nativeSession.Notify(XbdmDebugConstants.DmExec, (_, _) => nativeSeen.Set());
        managedSession.Notify(XbdmDebugConstants.DmExec, (_, _) => managedSeen.Set());

        session.NativeDebug.Stop();

        var deadline = TimeSpan.FromSeconds(10);
        var nativeOk = nativeSeen.Wait(deadline);
        var managedOk = managedSeen.Wait(deadline);

        if (nativeOk && managedOk)
            return ParityCompare.Pass(ExtendedCategory, "Notification(DmExec)", "both received exec stop");

        return ParityCompare.Fail(
            ExtendedCategory,
            "Notification(DmExec)",
            "Exec notification not received on both sides.",
            nativeOk ? "received" : "timeout",
            managedOk ? "received" : "timeout");
    }

    private static ParityCheckResult CompareEnableGpuCounter(XbdmParitySession session) =>
        ParityCompare.BothAction(
            ExtendedCategory,
            "EnableGpuCounter(toggle)",
            () =>
            {
                session.NativeDebug.EnableGpuCounter(true);
                session.NativeDebug.EnableGpuCounter(false);
            },
            () =>
            {
                session.ManagedDebug.EnableGpuCounter(true);
                session.ManagedDebug.EnableGpuCounter(false);
            });

    private static ParityCheckResult CompareRebootWarmWait()
    {
        var console = Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(console))
            return ParityCompare.Skip(ConnectionCategory, "Reboot(warm+wait)", "RXDK_TEST_CONSOLE not set.");

        using var nativeClient = new NativeXbdmClient();
        using var managedClient = new ManagedXbdmClient();
        nativeClient.Initialize();
        managedClient.Initialize();

        try
        {
            ParityProgress.Phase("Reboot: sending warm reboot (no WAIT — polling for kit)");
            using (var native = XbdmParitySession.Connect(nativeClient, console))
            {
                native.Debug.SetConnectionTimeout(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(10));
                native.Reboot(XbdmDebugConstants.DmbootWarm);
            }

            ParityProgress.Phase("Reboot: command sent, waiting for kit to return");
            Thread.Sleep(TimeSpan.FromSeconds(2));

            var rebootTimeout = TimeSpan.FromSeconds(
                int.TryParse(Environment.GetEnvironmentVariable("RXDK_PARITY_REBOOT_TIMEOUT_SEC"), out var seconds) && seconds > 0
                    ? seconds
                    : 90);
            var probeTimeout = TimeSpan.FromSeconds(2);

            if (!XbdmParityWait.Until(
                    () => XbdmParitySession.TryProbe(console, probeTimeout),
                    rebootTimeout,
                    progressLabel: "Waiting for kit after reboot"))
            {
                return ParityCompare.Fail(
                    ConnectionCategory,
                    "Reboot(warm+wait)",
                    $"Kit did not come back within {rebootTimeout.TotalSeconds:F0}s.",
                    "reboot sent",
                    "reconnect timeout");
            }

            ParityProgress.Phase("Reboot: kit is back, comparing drive lists");
            using var nativeAfter = XbdmParitySession.Connect(nativeClient, console);
            using var managedAfter = XbdmParitySession.Connect(managedClient, console);
            var nativeDrives = string.Join(',', nativeAfter.ListDrives().OrderBy(c => c));
            var managedDrives = string.Join(',', managedAfter.ListDrives().OrderBy(c => c));
            if (nativeDrives == managedDrives)
                return ParityCompare.Pass(ConnectionCategory, "Reboot(warm+wait)", nativeDrives);

            return ParityCompare.Fail(
                ConnectionCategory,
                "Reboot(warm+wait)",
                "Drive lists differ after reboot.",
                nativeDrives,
                managedDrives);
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Fail(ConnectionCategory, "Reboot(warm+wait)", ex.Message);
        }
    }

}
