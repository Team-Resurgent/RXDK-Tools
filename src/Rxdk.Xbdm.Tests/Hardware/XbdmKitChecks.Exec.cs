using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    public static IReadOnlyList<KitCheckResult> RunExecutionChecks(XbdmKitSession session)
    {
        if (!AllowExecTests())
        {
            return
            [
                KitCheck.Skip(
                    DebugCategory,
                    "Execution suite",
                    "Set RXDK_KIT_ALLOW_EXEC=1 — launches TriangleXDK on the kit (~2 min). Kit should be idle at dashboard."),
            ];
        }

        var localXbe = Path.Combine(XbdmKitSession.TestFilesDirectory(), "TriangleXDK.xbe");
        if (!File.Exists(localXbe))
        {
            return
            [
                KitCheck.Skip(DebugCategory, "Execution suite", $"Missing {localXbe}"),
            ];
        }

        var results = new List<KitCheckResult>();
        try
        {
            foreach (var result in ExecuteTriangleLaunch(session, localXbe))
                results.Add(result);
        }
        catch (Exception ex)
        {
            KitTestProgress.Phase($"Execution failed: {ex.Message}");
            results.Add(KitCheck.Fail(DebugCategory, "Execution suite", ex.Message));
        }
        finally
        {
            if (AllowBridgeTests())
            {
                KitTestProgress.Phase("Execution: returning kit to dashboard for bridge");
                if (session.WarmRebootToDashboard())
                    session.ReconnectKit();
            }
            else if (RestoreAfterExec() && !AllowRebootTests())
            {
                results.Add(TryRestoreDashboard(session));
            }
        }

        return results;
    }

    private static IEnumerable<KitCheckResult> ExecuteTriangleLaunch(
        XbdmKitSession session,
        string localXbe)
    {
        var wireDir = ExecWireDirectory(session.Managed);
        var wireXbe = $"{wireDir}\\TriangleXDK.xbe";

        if (!session.RebootToPendingExec())
        {
            yield return KitCheck.Fail(
                DebugCategory,
                "Execution suite",
                $"Pending exec did not return within {XbdmKitSession.GetKitWaitTimeout().TotalSeconds:F0}s.");
            yield break;
        }

        KitTestProgress.Phase($"Execution: uploading TriangleXDK to {wireXbe}");
        XbdmKitSession.EnsureTriangleXbeOnKit(session.Managed, localXbe, wireXbe);
        yield return KitCheck.Pass(LaunchCategory, "Exec.UploadTriangleXbe", wireXbe);

        KitTestProgress.Phase("Execution: launching TriangleXDK (clean autoRun — SetTitle → Go)");
        XbdmModLoadNotification? module;
        var launchTimeout = XbdmKitWait.LaunchTimeout;
        using (var launch = new TriangleLaunchHelper(session.ManagedDebug, "TriangleXDK.xbe"))
        {
            if (!launch.LaunchAndWaitForRunning(wireDir, "TriangleXDK.xbe", launchTimeout, out module))
            {
                yield return KitCheck.Fail(
                    DebugCategory,
                    "SetTitle(launch)",
                    $"TriangleXDK did not load within {launchTimeout.TotalSeconds:F0}s.",
                    module?.Name ?? "timeout");
                yield break;
            }
        }

        KitTestProgress.Phase($"Execution: loaded {module!.Name} @ 0x{module.BaseAddress:x} — running clean (no debugger)");
        yield return KitCheck.Pass(DebugCategory, "SetTitle(launch)", $"{module.Name} @ 0x{module.BaseAddress:x}");

        yield return KitCheck.ManagedCheck(
            LaunchCategory,
            "GetXbeLaunchPath(running)",
            () => NormalizeWirePath(session.ManagedDebug.GetXbeInfo(string.Empty).LaunchPath));

        XbdmKitWait.DwellWithProgress("Execution: TriangleXDK running");

        yield return CompareQueryPerformanceCounterForRunningTitle(session);
        yield return ComparePixelShaderSnapshot(session);

        var connectStop = ConnectDebuggerAndStop(session);
        yield return connectStop;
        if (connectStop.Status != KitCheckStatus.Passed)
            yield break;

        yield return CompareRunningXbeInfo(session);
        yield return CompareModuleSectionsForTitle(session, module.Name);
        yield return CompareModuleLongNameForTitle(session, module.Name);
        yield return CompareDebuggerConnectToggle(session);
        yield return CompareThreadListAfterAction(session, "Stop");

        var breakpointAddress = module.BaseAddress;
        session.ManagedDebug.SetBreakpoint(breakpointAddress);
        yield return KitCheck.ManagedCheck(
            DebugCategory,
            "SetBreakpoint",
            () => session.ManagedDebug.GetBreakpointType(breakpointAddress),
            type => $"0x{breakpointAddress:x} type={type}");

        var candidates = new List<nuint>();
        foreach (var address in XbeImageProbe.EnumerateWritableProbeAddresses(localXbe, module.BaseAddress))
            candidates.Add(address);

        var stackProbe = TryGetStackProbeAddress(session);
        if (stackProbe is not null && !candidates.Contains(stackProbe.Value))
            candidates.Add(stackProbe.Value);

        yield return candidates.Count == 0
            ? KitCheck.Fail(DebugCategory, "SetMemory", "No writable probe address (XBE section or stack).")
            : CompareSetMemoryRoundTrip(session, candidates);

        session.ManagedDebug.RemoveBreakpoint(breakpointAddress);
        yield return KitCheck.ManagedCheck(
            DebugCategory,
            "RemoveBreakpoint",
            () => session.ManagedDebug.GetBreakpointType(breakpointAddress),
            type => $"0x{breakpointAddress:x} type={type}");

        KitCheckResult? goStopFailure = null;
        try
        {
            session.ManagedDebug.Go();
            Thread.Sleep(500);
            session.ManagedDebug.Stop();
        }
        catch (XbdmException ex)
        {
            goStopFailure = KitCheck.Fail(DebugCategory, "Go+Stop", ex.Message);
        }

        if (goStopFailure is not null)
        {
            yield return goStopFailure;
            yield break;
        }

        yield return CompareThreadListAfterAction(session, "Go+Stop");
        yield return CompareThreadContext(session);
        yield return CompareThreadSuspendResume(session);
        yield return CompareTryGetThreadStop(session);
    }

    private static KitCheckResult ConnectDebuggerAndStop(XbdmKitSession session)
    {
        KitTestProgress.Phase("Execution: connecting debugger and stopping TriangleXDK");

        string? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (attempt > 1)
                KitTestProgress.Phase($"Execution: connect+stop retry {attempt}/3 ({lastError})");

            if (attempt > 1)
                XbdmKitSession.RecycleDebugSession(session);

            if (attempt == 3)
            {
                try
                {
                    session.ForceReconnectKit();
                }
                catch (Exception ex)
                {
                    return KitCheck.Fail(DebugCategory, "ConnectDebugger+Stop", ex.Message);
                }
            }

            lastError = TryConnectAndStop(session);
            if (lastError is null)
                return CompareThreadListAfterAction(session, "ConnectDebugger+Stop");
        }

        return KitCheck.Fail(
            DebugCategory,
            "ConnectDebugger+Stop",
            lastError ?? "Could not attach debugger.");
    }

    private static string? TryConnectAndStop(XbdmKitSession session)
    {
        try
        {
            session.ManagedDebug.ConnectDebugger(true);
            session.ManagedDebug.Stop();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static KitCheckResult CompareDebuggerConnectToggle(XbdmKitSession session)
    {
        try
        {
            session.ManagedDebug.ConnectDebugger(false);
            session.ManagedDebug.ConnectDebugger(true);
        }
        catch (Exception ex)
        {
            return KitCheck.Fail(DebugCategory, "ConnectDebugger(toggle)", ex.Message);
        }

        return CompareThreadListAfterAction(session, "ConnectDebugger(toggle)");
    }

    private static KitCheckResult CompareThreadSuspendResume(XbdmKitSession session)
    {
        var threadId = session.ManagedDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return KitCheck.Skip(DebugCategory, "SuspendThread/ResumeThread", "No threads.");

        var before = session.ManagedDebug.GetThreadInfo(threadId).SuspendCount;
        session.ManagedDebug.SuspendThread(threadId);
        var afterSuspend = session.ManagedDebug.GetThreadInfo(threadId).SuspendCount;
        session.ManagedDebug.ResumeThread(threadId);
        var afterResume = session.ManagedDebug.GetThreadInfo(threadId).SuspendCount;

        return afterSuspend > before && afterResume == before
            ? KitCheck.Pass(DebugCategory, "SuspendThread/ResumeThread", $"tid={threadId}")
            : KitCheck.Fail(
                DebugCategory,
                "SuspendThread/ResumeThread",
                "Suspend counts unexpected.",
                $"before={before} suspended={afterSuspend} resumed={afterResume}");
    }

    private static KitCheckResult CompareTryGetThreadStop(XbdmKitSession session)
    {
        var threadId = session.ManagedDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return KitCheck.Skip(DebugCategory, "TryGetThreadStop", "No threads.");

        var stop = session.ManagedDebug.TryGetThreadStop(threadId);
        return stop is null
            ? KitCheck.Pass(DebugCategory, "TryGetThreadStop", "null while running")
            : KitCheck.Pass(DebugCategory, "TryGetThreadStop", $"reason={stop.NotifiedReason}");
    }

    private static KitCheckResult CompareThreadListAfterAction(XbdmKitSession session, string label)
    {
        XbdmKitSession.RecycleDebugSession(session);

        try
        {
            var threads = session.ManagedDebug.GetThreadList().OrderBy(id => id).ToArray();
            return KitCheck.Pass(DebugCategory, $"Threads after {label}", $"{threads.Length} threads");
        }
        catch (XbdmException ex)
        {
            return KitCheck.Fail(DebugCategory, $"Threads after {label}", ex.Message);
        }
    }

    private static nuint? TryGetStackProbeAddress(XbdmKitSession session)
    {
        var threadId = session.ManagedDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return null;

        var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
        try
        {
            session.ManagedDebug.GetThreadContext(threadId, ref context);
        }
        catch (XbdmException)
        {
            return null;
        }

        return context.Esp < 0x100 ? null : context.Esp - 0x80;
    }

    private static KitCheckResult CompareSetMemoryRoundTrip(
        XbdmKitSession session,
        IReadOnlyList<nuint> candidates)
    {
        KitCheckResult? lastFailure = null;
        foreach (var address in candidates)
        {
            var result = TrySetMemoryRoundTrip(session, address);
            if (result.Status == KitCheckStatus.Passed)
                return result;
            lastFailure = result;
        }

        return lastFailure ?? KitCheck.Fail(DebugCategory, "SetMemory", "No probe addresses tried.");
    }

    private static KitCheckResult TrySetMemoryRoundTrip(XbdmKitSession session, nuint address)
    {
        Span<byte> original = stackalloc byte[4];
        try
        {
            session.ManagedDebug.GetMemory(address, original);
        }
        catch (XbdmException ex)
        {
            return KitCheck.Fail(DebugCategory, "SetMemory", $"Cannot read probe address 0x{address:x}: {ex.Message}");
        }

        try
        {
            var written = session.ManagedDebug.SetMemory(address, original);
            if (written != original.Length)
            {
                return KitCheck.Fail(
                    DebugCategory,
                    "SetMemory",
                    $"Write incomplete at 0x{address:x}: {written}/{original.Length} bytes.");
            }
        }
        catch (XbdmException ex)
        {
            return KitCheck.Fail(DebugCategory, "SetMemory", $"Cannot write probe address 0x{address:x}: {ex.Message}");
        }

        Span<byte> readback = stackalloc byte[4];
        session.ManagedDebug.GetMemory(address, readback);
        return readback.SequenceEqual(original)
            ? KitCheck.Pass(DebugCategory, "SetMemory", $"0x{address:x} round-trip")
            : KitCheck.Fail(
                DebugCategory,
                "SetMemory",
                "Readback differs after write.",
                Convert.ToHexString(readback));
    }

    private static KitCheckResult CompareThreadContext(XbdmKitSession session)
    {
        var threadId = session.ManagedDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return KitCheck.Skip(DebugCategory, "GetThreadContext", "No threads.");

        var context = new XbdmContext
        {
            ContextFlags = XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger,
        };
        session.ManagedDebug.GetThreadContext(threadId, ref context);
        return KitCheck.Pass(
            DebugCategory,
            "GetThreadContext",
            $"tid={threadId} eip=0x{context.Eip:x} esp=0x{context.Esp:x}");
    }

    private static KitCheckResult TryRestoreDashboard(XbdmKitSession session)
    {
        try
        {
            KitTestProgress.Phase("Execution: rebooting kit back to dashboard");
            session.ManagedDebug.Reboot(XbdmDebugConstants.DmbootWarm | XbdmDebugConstants.DmbootWait);
            return KitCheck.Pass(DebugCategory, "Restore(dashboard)", "warm reboot sent");
        }
        catch (XbdmException ex)
        {
            return KitCheck.Skip(
                DebugCategory,
                "Restore(dashboard)",
                $"Could not reboot to dashboard: {ex.Message}. Reboot manually if the title is still running.");
        }
    }

    private static string ExecWireDirectory(IXbdmConnection connection)
    {
        var drives = connection.ListDrives();
        var drive = drives.Contains('E') ? 'E' : XbdmKitSession.PickScratchDrive(connection);
        return $"{drive}:\\rxdk-kit-exec";
    }
}
