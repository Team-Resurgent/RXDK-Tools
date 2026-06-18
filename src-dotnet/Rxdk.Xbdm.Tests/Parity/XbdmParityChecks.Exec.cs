using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    public static IReadOnlyList<ParityCheckResult> RunExecutionChecks(XbdmParitySession session)
    {
        if (!AllowExecTests())
        {
            return
            [
                ParityCompare.Skip(
                    DebugCategory,
                    "Execution suite",
                    "Set RXDK_PARITY_ALLOW_EXEC=1 — launches TriangleXDK on the kit (~2 min). Kit should be idle at dashboard."),
            ];
        }

        var localXbe = Path.Combine(XbdmParitySession.TestFilesDirectory(), "TriangleXDK.xbe");
        if (!File.Exists(localXbe))
        {
            return
            [
                ParityCompare.Skip(DebugCategory, "Execution suite", $"Missing {localXbe}"),
            ];
        }

        var results = new List<ParityCheckResult>();
        try
        {
            foreach (var result in ExecuteTriangleLaunchParity(session, localXbe))
                results.Add(result);
        }
        catch (Exception ex)
        {
            ParityProgress.Phase($"Execution failed: {ex.Message}");
            results.Add(ParityCompare.Fail(DebugCategory, "Execution suite", ex.Message));
        }
        finally
        {
            if (AllowBridgeTests())
            {
                ParityProgress.Phase("Execution: returning kit to dashboard for bridge");
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

    private static IEnumerable<ParityCheckResult> ExecuteTriangleLaunchParity(
        XbdmParitySession session,
        string localXbe)
    {
        var wireDir = ExecWireDirectory(session.Native);
        var wireXbe = $"{wireDir}\\TriangleXDK.xbe";

        if (!session.RebootToPendingExec())
        {
            yield return ParityCompare.Fail(
                DebugCategory,
                "Execution suite",
                $"Pending exec did not return within {XbdmParitySession.GetKitWaitTimeout().TotalSeconds:F0}s.");
            yield break;
        }

        ParityProgress.Phase($"Execution: uploading TriangleXDK to {wireXbe}");
        XbdmParitySession.EnsureTriangleXbeOnKit(session.Native, localXbe, wireXbe);
        yield return ParityCompare.Pass(LaunchCategory, "Exec.UploadTriangleXbe", wireXbe);

        ParityProgress.Phase("Execution: launching TriangleXDK (clean autoRun — SetTitle → Go)");
        XbdmModLoadNotification? nativeModule;
        XbdmModLoadNotification? managedModule;
        var launchTimeout = XbdmParityWait.LaunchTimeout;
        using (var launch = new TriangleLaunchHelper(session.NativeDebug, "TriangleXDK.xbe"))
        {
            var nativeLoaded = launch.LaunchAndWaitForRunning(
                wireDir, "TriangleXDK.xbe", launchTimeout, out nativeModule);
            var managedLoaded = XbdmParityWait.ForTitleModule(
                session.ManagedDebug, TimeSpan.FromSeconds(15), out managedModule);

            if (!nativeLoaded || !managedLoaded)
            {
                yield return ParityCompare.Fail(
                    DebugCategory,
                    "SetTitle(launch)",
                    $"TriangleXDK did not load within {launchTimeout.TotalSeconds:F0}s.",
                    nativeModule?.Name ?? "timeout",
                    managedModule?.Name ?? "timeout");
                yield break;
            }
        }

        ParityProgress.Phase($"Execution: loaded {nativeModule!.Name} @ 0x{nativeModule.BaseAddress:x} — running clean (no debugger)");

        yield return ParityCompare.Equal(
            DebugCategory,
            "SetTitle(launch)",
            nativeModule!.BaseAddress,
            managedModule!.BaseAddress,
            nativeModule.Name);

        yield return ParityCompare.RequireSuccessOrEqual(
            LaunchCategory,
            "GetXbeLaunchPath(running)",
            () => NormalizeWirePath(session.NativeDebug.GetXbeInfo(string.Empty).LaunchPath),
            () => NormalizeWirePath(session.ManagedDebug.GetXbeInfo(string.Empty).LaunchPath));

        // Title is running freely now; let it render the triangle for the dwell window.
        XbdmParityWait.DwellWithProgress("Execution: TriangleXDK running");

        yield return CompareQueryPerformanceCounterForRunningTitle(session);
        yield return ComparePixelShaderSnapshot(session);

        var connectStop = ConnectDebuggerAndStop(session);
        yield return connectStop;
        if (connectStop.Status != ParityStatus.Passed)
            yield break;

        yield return CompareRunningXbeInfo(session);
        yield return CompareModuleSectionsForTitle(session, nativeModule!.Name);
        yield return CompareModuleLongNameForTitle(session, nativeModule!.Name);

        yield return CompareDebuggerConnectToggle(session);

        yield return CompareThreadListsAfterNativeAction(session, "Stop");

        var breakpointAddress = nativeModule.BaseAddress;
        session.NativeDebug.SetBreakpoint(breakpointAddress);
        yield return ParityCompare.Equal(
            DebugCategory,
            "SetBreakpoint",
            session.NativeDebug.GetBreakpointType(breakpointAddress),
            session.ManagedDebug.GetBreakpointType(breakpointAddress),
            $"0x{breakpointAddress:x}");

        var candidates = new List<nuint>();
        foreach (var address in XbeImageProbe.EnumerateWritableProbeAddresses(localXbe, nativeModule.BaseAddress))
            candidates.Add(address);

        var stackProbe = TryGetStackProbeAddress(session);
        if (stackProbe is not null && !candidates.Contains(stackProbe.Value))
            candidates.Add(stackProbe.Value);

        if (candidates.Count == 0)
        {
            yield return ParityCompare.Fail(
                DebugCategory,
                "SetMemory",
                "No writable probe address (XBE section or stack).");
        }
        else
        {
            yield return CompareSetMemoryRoundTrip(session, candidates);
        }

        session.NativeDebug.RemoveBreakpoint(breakpointAddress);
        yield return ParityCompare.Equal(
            DebugCategory,
            "RemoveBreakpoint",
            session.NativeDebug.GetBreakpointType(breakpointAddress),
            session.ManagedDebug.GetBreakpointType(breakpointAddress),
            $"0x{breakpointAddress:x}");

        ParityCheckResult? goStopFailure = null;
        try
        {
            session.NativeDebug.Go();
            Thread.Sleep(500);
            session.NativeDebug.Stop();
        }
        catch (XbdmException ex)
        {
            goStopFailure = ParityCompare.Fail(DebugCategory, "Go+Stop", ex.Message);
        }

        if (goStopFailure is not null)
        {
            yield return goStopFailure;
            yield break;
        }

        yield return CompareThreadListsAfterNativeAction(session, "Go+Stop");

        yield return CompareThreadContext(session);
        yield return CompareThreadSuspendResume(session);
        yield return CompareTryGetThreadStop(session);
    }

    private static ParityCheckResult ConnectDebuggerAndStop(XbdmParitySession session)
    {
        ParityProgress.Phase("Execution: connecting debugger and stopping TriangleXDK");

        string? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (attempt > 1)
                ParityProgress.Phase($"Execution: connect+stop retry {attempt}/3 ({lastError})");

            if (attempt > 1)
                XbdmParitySession.RecycleDebugSession(session);

            if (attempt == 3)
            {
                try
                {
                    session.ForceReconnectKit();
                }
                catch (Exception ex)
                {
                    return ParityCompare.Fail(DebugCategory, "ConnectDebugger+Stop", ex.Message);
                }
            }

            lastError = TryNativeConnectAndStop(session);
            if (lastError is null)
                return CompareDebugThreadParity(session, "ConnectDebugger+Stop");
        }

        return ParityCompare.Fail(
            DebugCategory,
            "ConnectDebugger+Stop",
            lastError ?? "Could not attach debugger.");
    }

    /// <summary>
    /// Native debug proxy and managed debug share one XBDM session per console. Only native
    /// should send DEBUGGER CONNECT/STOP; managed verifies the halted state via reads.
    /// </summary>
    private static string? TryNativeConnectAndStop(XbdmParitySession session)
    {
        try
        {
            session.NativeDebug.ConnectDebugger(true);
            session.NativeDebug.Stop();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static ParityCheckResult CompareDebugThreadParity(XbdmParitySession session, string step)
    {
        try
        {
            var native = session.NativeDebug.GetThreadList().OrderBy(id => id).ToArray();
            var managed = session.ManagedDebug.GetThreadList().OrderBy(id => id).ToArray();
            if (native.SequenceEqual(managed))
                return ParityCompare.Pass(DebugCategory, step, $"{native.Length} threads");

            return ParityCompare.Fail(
                DebugCategory,
                step,
                "Thread lists differ after native attach/stop.",
                string.Join(',', native),
                string.Join(',', managed));
        }
        catch (Exception ex)
        {
            return ParityCompare.Fail(DebugCategory, step, ex.Message);
        }
    }

    private static ParityCheckResult CompareDebuggerConnectToggle(XbdmParitySession session)
    {
        try
        {
            session.NativeDebug.ConnectDebugger(false);
            session.NativeDebug.ConnectDebugger(true);
        }
        catch (Exception ex)
        {
            return ParityCompare.Fail(DebugCategory, "ConnectDebugger(toggle)", ex.Message);
        }

        return CompareDebugThreadParity(session, "ConnectDebugger(toggle)");
    }

    private static ParityCheckResult CompareThreadSuspendResume(XbdmParitySession session)
    {
        var threadId = session.NativeDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return ParityCompare.Skip(DebugCategory, "SuspendThread/ResumeThread", "No threads.");

        var nativeBefore = session.NativeDebug.GetThreadInfo(threadId).SuspendCount;
        var managedBefore = session.ManagedDebug.GetThreadInfo(threadId).SuspendCount;

        session.NativeDebug.SuspendThread(threadId);

        var nativeAfterSuspend = session.NativeDebug.GetThreadInfo(threadId).SuspendCount;
        var managedAfterSuspend = session.ManagedDebug.GetThreadInfo(threadId).SuspendCount;

        session.NativeDebug.ResumeThread(threadId);

        var nativeAfterResume = session.NativeDebug.GetThreadInfo(threadId).SuspendCount;
        var managedAfterResume = session.ManagedDebug.GetThreadInfo(threadId).SuspendCount;

        if (nativeAfterSuspend > nativeBefore &&
            managedAfterSuspend > managedBefore &&
            nativeAfterSuspend == managedAfterSuspend &&
            nativeAfterResume == managedAfterResume &&
            nativeAfterResume == nativeBefore &&
            managedAfterResume == managedBefore)
        {
            return ParityCompare.Pass(DebugCategory, "SuspendThread/ResumeThread", $"tid={threadId}");
        }

        return ParityCompare.Fail(
            DebugCategory,
            "SuspendThread/ResumeThread",
            "Suspend counts differ.",
            $"before={nativeBefore} suspended={nativeAfterSuspend} resumed={nativeAfterResume}",
            $"before={managedBefore} suspended={managedAfterSuspend} resumed={managedAfterResume}");
    }

    private static ParityCheckResult CompareTryGetThreadStop(XbdmParitySession session)
    {
        var threadId = session.NativeDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return ParityCompare.Skip(DebugCategory, "TryGetThreadStop", "No threads.");

        var nativeStop = session.NativeDebug.TryGetThreadStop(threadId);
        var managedStop = session.ManagedDebug.TryGetThreadStop(threadId);

        if (nativeStop is null && managedStop is null)
            return ParityCompare.Pass(DebugCategory, "TryGetThreadStop", "both null while running");

        if (nativeStop is not null && managedStop is not null &&
            nativeStop.NotifiedReason == managedStop.NotifiedReason)
        {
            return ParityCompare.Pass(DebugCategory, "TryGetThreadStop", $"reason={nativeStop.NotifiedReason}");
        }

        return ParityCompare.Fail(
            DebugCategory,
            "TryGetThreadStop",
            "Stop state differs.",
            nativeStop?.NotifiedReason.ToString() ?? "null",
            managedStop?.NotifiedReason.ToString() ?? "null");
    }

    private static ParityCheckResult CompareThreadListsAfterNativeAction(XbdmParitySession session, string label)
    {
        XbdmParitySession.RecycleDebugSession(session);

        IReadOnlyList<uint> native;
        IReadOnlyList<uint> managed;
        try
        {
            native = session.NativeDebug.GetThreadList();
            managed = session.ManagedDebug.GetThreadList();
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Fail(DebugCategory, $"Threads after {label}", ex.Message);
        }

        native = native.OrderBy(id => id).ToArray();
        managed = managed.OrderBy(id => id).ToArray();
        if (native.SequenceEqual(managed))
            return ParityCompare.Pass(DebugCategory, $"Threads after {label}", $"{native.Count} threads");

        return ParityCompare.Fail(
            DebugCategory,
            $"Threads after {label}",
            "Thread lists differ.",
            string.Join(',', native),
            string.Join(',', managed));
    }

    private static nuint? TryGetStackProbeAddress(XbdmParitySession session)
    {
        var threadId = session.NativeDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return null;

        var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
        try
        {
            session.NativeDebug.GetThreadContext(threadId, ref context);
        }
        catch (XbdmException)
        {
            return null;
        }

        if (context.Esp < 0x100)
            return null;

        // Stack below ESP is writable while the title is halted under the debugger.
        return context.Esp - 0x80;
    }

    private static ParityCheckResult CompareSetMemoryRoundTrip(
        XbdmParitySession session,
        IReadOnlyList<nuint> candidates)
    {
        ParityCheckResult? lastFailure = null;
        foreach (var address in candidates)
        {
            var result = TrySetMemoryRoundTrip(session, address);
            if (result.Status == ParityStatus.Passed)
                return result;
            lastFailure = result;
        }

        return lastFailure ?? ParityCompare.Fail(DebugCategory, "SetMemory", "No probe addresses tried.");
    }

    private static ParityCheckResult TrySetMemoryRoundTrip(XbdmParitySession session, nuint address)
    {
        Span<byte> original = stackalloc byte[4];
        try
        {
            session.ManagedDebug.GetMemory(address, original);
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Fail(DebugCategory, "SetMemory", $"Cannot read probe address 0x{address:x}: {ex.Message}");
        }

        try
        {
            var written = session.ManagedDebug.SetMemory(address, original);
            if (written != original.Length)
            {
                return ParityCompare.Fail(
                    DebugCategory,
                    "SetMemory",
                    $"Write incomplete at 0x{address:x}: {written}/{original.Length} bytes.");
            }
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Fail(DebugCategory, "SetMemory", $"Cannot write probe address 0x{address:x}: {ex.Message}");
        }

        Span<byte> nativeRead = stackalloc byte[4];
        Span<byte> managedRead = stackalloc byte[4];
        session.NativeDebug.GetMemory(address, nativeRead);
        session.ManagedDebug.GetMemory(address, managedRead);

        if (nativeRead.SequenceEqual(original) && managedRead.SequenceEqual(original))
            return ParityCompare.Pass(DebugCategory, "SetMemory", $"0x{address:x} round-trip");

        return ParityCompare.Fail(
            DebugCategory,
            "SetMemory",
            "Readback differs after managed write.",
            Convert.ToHexString(nativeRead),
            Convert.ToHexString(managedRead));
    }

    private static ParityCheckResult CompareThreadContext(XbdmParitySession session)
    {
        var threadId = session.NativeDebug.GetThreadList().FirstOrDefault();
        if (threadId == 0)
            return ParityCompare.Skip(DebugCategory, "GetThreadContext", "No threads.");

        var nativeContext = new XbdmContext
        {
            ContextFlags = XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger,
        };
        var managedContext = new XbdmContext
        {
            ContextFlags = XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger,
        };

        session.NativeDebug.GetThreadContext(threadId, ref nativeContext);
        session.ManagedDebug.GetThreadContext(threadId, ref managedContext);

        if (nativeContext.Eip == managedContext.Eip &&
            nativeContext.Esp == managedContext.Esp &&
            nativeContext.Ebp == managedContext.Ebp)
        {
            return ParityCompare.Pass(
                DebugCategory,
                "GetThreadContext",
                $"tid={threadId} eip=0x{nativeContext.Eip:x}");
        }

        return ParityCompare.Fail(
            DebugCategory,
            "GetThreadContext",
            "Register state differs.",
            $"eip=0x{nativeContext.Eip:x} esp=0x{nativeContext.Esp:x}",
            $"eip=0x{managedContext.Eip:x} esp=0x{managedContext.Esp:x}");
    }

    private static ParityCheckResult TryRestoreDashboard(XbdmParitySession session)
    {
        try
        {
            ParityProgress.Phase("Execution: rebooting kit back to dashboard");
            session.NativeDebug.Reboot(XbdmDebugConstants.DmbootWarm | XbdmDebugConstants.DmbootWait);
            return ParityCompare.Pass(DebugCategory, "Restore(dashboard)", "warm reboot sent");
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Skip(
                DebugCategory,
                "Restore(dashboard)",
                $"Could not reboot to dashboard: {ex.Message}. Reboot manually if the title is still running.");
        }
    }

    private static string ExecWireDirectory(IXbdmConnection connection)
    {
        var drives = connection.ListDrives();
        var drive = drives.Contains('E') ? 'E' : XbdmParitySession.PickScratchDrive(connection);
        return $"{drive}:\\rxdk-parity-exec";
    }
}
