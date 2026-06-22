using System.Text;
using Rxdk.Xbdm;

namespace Rxdk.XboxDbgBridge;

internal sealed partial class DebugBridgeSession
{
    private const int GoUserWaitMs = 4_000;
    private const int GoUserMaxAttempts = 12;
    private const uint TitleImageSpan = 0x50000;

    private bool _stepActive;
    private bool _autoRunResume;
    private bool _threadStopped;
    private bool _launchStopped;
    private bool _launched;

    /// <summary>
    /// Continue from the launch entry hold to the first user breakpoint. Event-driven port of the
    /// proven session.c CmdGoUser: resume all stopped threads (as exceptions), GO, resume the main
    /// thread's suspend count, then wait for a break notification, skipping non-title stops.
    /// </summary>
    private void GoUser(int id)
    {
        EnsureNotifications();
        EnsureStartupStopOnRelaxed();
        _autoRunResume = true;
        try
        {
            BridgeWriter.Log(
                $"goUser: entry pc=0x{_stoppedAddress:x} thread={_mainThread} breakpoints={_breakpoints.Active.Count}");

            for (var attempt = 0; attempt < GoUserMaxAttempts && _active; attempt++)
            {
                ResumeAllStoppedThreads();
                BypassStoppedHardwareBreakpoint();
                _breakEvent.Reset();
                if (!TryGo($"goUser attempt={attempt}"))
                    break;
                ResumeMainThreadSuspend("goUser");

                if (_breakEvent.Wait(GoUserWaitMs))
                {
                    if (_moduleBase != 0 && _stoppedAddress != 0 && IsTitleAddress(_stoppedAddress))
                        break;
                    if (SyncStoppedStateFromKit() && IsTitleAddress(_stoppedAddress))
                        break;
                    BridgeWriter.Log(
                        $"goUser: skipping non-title break at 0x{_stoppedAddress:x} (base=0x{_moduleBase:x} attempt={attempt})");
                    continue;
                }

                // Wait timed out — reconcile against the kit.
                if (SyncStoppedStateFromKit() && StoppedAtActiveBreakpoint() && IsTitleAddress(_stoppedAddress))
                    break;
                if (!AnyThreadStopped())
                {
                    BridgeWriter.Log("goUser: title running, no title breakpoint yet");
                    LeaveTitleRunning(id);
                    return;
                }
                // Still stopped at a non-title address — loop to skip it.
            }

            if (_moduleBase != 0 && _stoppedAddress != 0 && IsTitleAddress(_stoppedAddress))
            {
                if (_stoppedThread == 0)
                    _stoppedThread = _mainThread;
                BridgeWriter.Log($"goUser: user breakpoint at 0x{_stoppedAddress:x}");
                BridgeWriter.EmitResult(id, true,
                    $"\"threadId\":{_stoppedThread},\"address\":\"0x{_stoppedAddress:x}\"");
                return;
            }

            BridgeWriter.Log("goUser: no title breakpoint; leaving title running");
            LeaveTitleRunning(id);
        }
        finally
        {
            _autoRunResume = false;
            _holdForBreakpointSetup = false;
        }
    }

    /// <summary>
    /// Decrement the main thread's suspend count after GO. The kernel suspends threads when it stops
    /// them on a notification (CREATETHREAD / break); ContinueThread only acknowledges the debug
    /// event, so without this RESUME the thread stays parked and never reaches title code.
    /// </summary>
    private void ResumeMainThreadSuspend(string phase)
    {
        if (_debug is null || _mainThread == 0)
            return;
        if (!IsThreadStoppedOnKit(_mainThread))
            return;
        try
        {
            _debug.ResumeThread(_mainThread);
            BridgeWriter.Log($"{phase}: ResumeThread({_mainThread})");
        }
        catch (XbdmException ex)
        {
            BridgeWriter.Log($"{phase}: ResumeThread({_mainThread}) failed: {ex.Message}");
        }
    }

    private bool EmitUserBreakpointStop(int id, nuint address, string via)
    {
        if (!StoppedAtActiveBreakpoint() && !BreakpointMatchesAddress(address))
            return false;

        _stoppedAddress = address;
        if (_stoppedThread == 0)
            _stoppedThread = _mainThread;
        BridgeWriter.Log($"runAfterBreakpoints: user breakpoint at 0x{address:x} ({via})");
        BridgeWriter.EmitResult(id, true,
            $"\"threadId\":{_stoppedThread},\"address\":\"0x{address:x}\"");
        return true;
    }

    private bool BreakpointMatchesAddress(nuint address)
    {
        if (address == 0)
            return false;

        foreach (var bp in _breakpoints.Active)
        {
            if (bp.Address == address || bp.Address + 1 == address)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Poll main-thread EIP — this kit often reports ISSTOPPED=false even at a soft breakpoint.
    /// </summary>
    private bool PollUserBreakpoint(out nuint address)
    {
        address = 0;
        if (_debug is null || _mainThread == 0 || _breakpoints.Active.Count == 0)
            return false;

        var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
        try
        {
            _debug.GetThreadContext(_mainThread, ref context);
        }
        catch (XbdmException ex)
        {
            BridgeWriter.Log($"PollUserBreakpoint: GetThreadContext failed: {ex.Message}");
            return false;
        }

        var eip = (nuint)context.Eip;
        if (eip == 0 && _stoppedAddress != 0)
            eip = _stoppedAddress;
        if (eip == 0)
            return false;

        if (IsSoftBreakpointAt(eip) && _breakpoints.IsActive(eip - 1))
            eip = eip - 1;

        foreach (var bp in _breakpoints.Active)
        {
            if (bp.Address != eip && bp.Address + 1 != eip)
                continue;
            address = bp.Address;
            _stoppedThread = _mainThread;
            _threadStopped = true;
            _stoppedAddress = address;
            return true;
        }

        return false;
    }

    private bool ResumeStoppedThreadsIfNeeded(string phase)
    {
        if (_debug is null)
            return false;

        var continued = 0;
        foreach (var threadId in _debug.GetThreadList())
        {
            if (!IsThreadStoppedOnKit(threadId))
                continue;

            var exception = _autoRunResume || ShouldContinueAsException(threadId) || IsStoppedAtSoftwareBreakpoint();
            try
            {
                _debug.ContinueThread(threadId, exception);
                continued++;
                BridgeWriter.Log($"{phase}: ContinueThread({threadId}, exception={exception})");
            }
            catch (XbdmException ex)
            {
                BridgeWriter.Log($"{phase}: ContinueThread({threadId}) failed: {ex.Message}");
            }
        }

        _threadStopped = false;
        _launchStopped = false;
        return continued > 0;
    }

    private bool TryGo(string phase)
    {
        if (_debug is null)
            return false;

        try
        {
            _debug.Go();
            return true;
        }
        catch (XbdmException ex)
        {
            if (AnyThreadStopped())
            {
                BridgeWriter.Log($"{phase}: Go failed while stopped: {ex.Message}");
                return false;
            }

            BridgeWriter.Log($"{phase}: Go skipped ({ex.Message}); title already running");
            return true;
        }
    }

    private void LeaveTitleRunning(int id)
    {
        if (ResumeStoppedThreadsIfNeeded("leaveRunning"))
        {
            BypassStoppedHardwareBreakpoint();
            if (!TryGo("leaveRunning"))
            {
                BridgeWriter.EmitResult(id, false, "\"error\":\"go\"");
                return;
            }
        }

        if (AnyThreadStopped())
        {
            if (PollUserBreakpoint(out var addr) && EmitUserBreakpointStop(id, addr, "leaveRunning poll"))
                return;

            BridgeWriter.Log("leaveRunning still stopped");
            BridgeWriter.EmitResult(id, false, "\"error\":\"stillStopped\"");
            return;
        }

        BridgeWriter.Log("leaveRunning");
        BridgeWriter.EmitResult(id, true, "\"running\":true");
    }

    private void EmitKitDiag(int id)
    {
        EnsureNotifications();
        nuint mainEip = 0;
        if (_mainThread != 0 && IsThreadStoppedOnKit(_mainThread))
        {
            var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
            _debug!.GetThreadContext(_mainThread, ref context);
            mainEip = context.Eip;
        }

        if (_mainThread != 0 && IsThreadStoppedOnKit(_mainThread) && _stoppedAddress == 0)
            SyncStoppedStateFromKit();

        if (mainEip == 0 && _mainThread != 0)
        {
            var ctx = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
            _debug!.GetThreadContext(_mainThread, ref ctx);
            mainEip = ctx.Eip;
        }

        var atUserBp = PollUserBreakpoint(out var polledAddr);
        if (atUserBp)
            mainEip = polledAddr;

        var builder = new StringBuilder();
        builder.Append("{\"type\":\"result\",\"id\":").Append(id).Append(",\"success\":true");
        builder.Append(",\"mainThread\":").Append(_mainThread);
        builder.Append(",\"stoppedThread\":").Append(_stoppedThread);
        builder.Append(",\"moduleBase\":\"0x").Append(_moduleBase.ToString("x")).Append('"');
        builder.Append(",\"stoppedAddr\":\"0x").Append(_stoppedAddress.ToString("x")).Append('"');
        builder.Append(",\"mainEip\":\"0x").Append(mainEip.ToString("x8")).Append('"');
        builder.Append(",\"atUserBreakpoint\":").Append(Bool(atUserBp));
        builder.Append(",\"execState\":").Append(_execState);
        builder.Append(",\"threadStopped\":").Append(Bool(_threadStopped));
        builder.Append(",\"launchStopped\":").Append(Bool(_launchStopped));
        builder.Append(",\"holdForBreakpointSetup\":").Append(Bool(_holdForBreakpointSetup));
        builder.Append(",\"mainStoppedOnKit\":").Append(Bool(_mainThread != 0 && IsThreadStoppedOnKit(_mainThread)));
        builder.Append(",\"launched\":").Append(Bool(_launched));
        builder.Append(",\"connected\":").Append(Bool(_connectedToDebugger));
        builder.Append(",\"activeBreakpoints\":").Append(_breakpoints.Active.Count);
        builder.Append(",\"pendingBreakpoints\":").Append(_breakpoints.Pending.Count);
        builder.Append(",\"symbolDiag\":\"").Append(Escape(_symbols.Diag())).Append('"');
        builder.Append(",\"threads\":[");

        var threads = _debug!.GetThreadList();
        for (var i = 0; i < threads.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            var threadId = threads[i];
            var stopped = _debug.TryGetThreadStop(threadId) is not null;
            builder.Append("{\"id\":").Append(threadId).Append(",\"stopped\":").Append(Bool(stopped)).Append('}');
        }

        builder.Append("]}");
        BridgeWriter.Emit(builder.ToString());
    }

    private bool IsTitleAddress(nuint address)
    {
        if (_moduleBase == 0 || address < _moduleBase || address >= _moduleBase + TitleImageSpan)
            return false;
        return true;
    }

    private bool AnyThreadStopped()
    {
        if (_debug is null)
            return _threadStopped || _launchStopped;

        foreach (var threadId in _debug.GetThreadList())
        {
            if (_debug.TryGetThreadStop(threadId) is not null)
                return true;
        }

        return _threadStopped || _launchStopped;
    }

    private bool IsThreadStoppedOnKit(uint threadId) =>
        _debug?.TryGetThreadStop(threadId) is not null;

    private bool StoppedAtActiveBreakpoint()
    {
        if (_stoppedAddress == 0)
            return false;

        foreach (var bp in _breakpoints.Active)
        {
            if (bp.Address == _stoppedAddress || bp.Address + 1 == _stoppedAddress)
                return true;
        }

        return SoftBreakpointAddress() is not null;
    }

    private bool SyncStoppedStateFromKit()
    {
        if (_mainThread == 0)
            return false;

        if (PollUserBreakpoint(out var polled))
        {
            _stoppedThread = _mainThread;
            _threadStopped = true;
            _stoppedAddress = polled;
            return true;
        }

        if (!IsThreadStoppedOnKit(_mainThread))
            return false;

        var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
        _debug!.GetThreadContext(_mainThread, ref context);
        if (context.Eip == 0)
            return _stoppedAddress != 0;

        _stoppedThread = _mainThread;
        _threadStopped = true;

        if (IsSoftBreakpointAt(context.Eip))
        {
            _stoppedAddress = context.Eip;
            return true;
        }

        if (context.Eip > 0 && IsSoftBreakpointAt(context.Eip - 1))
        {
            _stoppedAddress = context.Eip - 1;
            return true;
        }

        foreach (var bp in _breakpoints.Active)
        {
            if (bp.Address == context.Eip || bp.Address + 1 == context.Eip)
            {
                _stoppedAddress = bp.Address;
                return true;
            }
        }

        _stoppedAddress = context.Eip;
        return true;
    }

    private bool IsSoftBreakpointAt(nuint address)
    {
        if (_debug is null || address == 0)
            return false;

        var bpType = _debug.GetBreakpointType(address);
        return bpType == XbdmDebugConstants.DmbreakFixed;
    }

    private void EnsureStartupStopOnRelaxed()
    {
        if (_startupStopOnRelaxed || _debug is null)
            return;

        _debug.StopOn(
            XbdmDebugConstants.DmstopCreateThread | XbdmDebugConstants.DmstopFce | XbdmDebugConstants.DmstopDebugStr,
            stop: false);
        _startupStopOnRelaxed = true;
    }

    private void HoldMainThreadIfRunning(string phase)
    {
        if (_debug is null || _mainThread == 0)
            return;

        if (IsThreadStoppedOnKit(_mainThread))
        {
            _threadStopped = true;
            SyncStoppedStateFromKit();
            BridgeWriter.Log($"{phase}: main thread already stopped (pc=0x{_stoppedAddress:x})");
            return;
        }

        try
        {
            _debug.HaltThread(_mainThread);
            _threadStopped = true;
            SyncStoppedStateFromKit();
            BridgeWriter.Log($"{phase}: halted main thread (pc=0x{_stoppedAddress:x})");
        }
        catch (XbdmException ex)
        {
            BridgeWriter.Log($"{phase}: HaltThread failed: {ex.Message}");
        }
    }

    private void OnModuleBaseChanged(nuint oldBase, string? moduleName = null)
    {
        if (_moduleBase == 0 || _moduleBase == oldBase)
            return;

        var label = string.IsNullOrWhiteSpace(moduleName) ? string.Empty : $" ({moduleName})";
        BridgeWriter.Log($"Module base updated 0x{oldBase:x} -> 0x{_moduleBase:x}{label}");
        if (_breakpoints.Pending.Count > 0)
            ApplyPendingBreakpoints();
        if (_breakpoints.Active.Count > 0)
        {
            _breakpoints.ReapplyActiveBreakpoints(
                _debug!,
                (file, line) => _symbols.TryResolveLine(file, line, out var address) ? address : null,
                NormalizeBpAddress,
                IsKitBpAddress);
        }
    }

    private bool TryGetCallReturnAddress(uint threadId, out nuint returnAddress)
    {
        returnAddress = 0;
        if (_debug is null)
            return false;

        var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
        _debug.GetThreadContext(threadId, ref context);
        var eip = context.Eip;

        Span<byte> code = stackalloc byte[16];
        if (_debug.GetMemory(eip, code) < 2)
            return false;

        if (code[0] == 0xE8)
        {
            if (_debug.GetMemory(eip, code[..5]) < 5)
                return false;
            returnAddress = eip + 5;
            return true;
        }

        if (code[0] != 0xFF)
            return false;

        if (_debug.GetMemory(eip, code[..2]) < 2)
            return false;

        var modrm = code[1];
        if ((modrm & 0x38) != 0x10)
            return false;

        if (_debug.GetMemory(eip, code[..6]) < 6)
            return false;

        returnAddress = eip + 6;
        return true;
    }
}
