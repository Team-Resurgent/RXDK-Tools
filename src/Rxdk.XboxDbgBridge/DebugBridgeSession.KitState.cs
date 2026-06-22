using System.Text;
using Rxdk.Xbdm;

namespace Rxdk.XboxDbgBridge;

internal sealed partial class DebugBridgeSession
{
    private const int GoUserTimeoutMs = 60_000;
    private const uint TitleImageSpan = 0x50000;

    private bool _stepActive;
    private bool _autoRunResume;
    private bool _threadStopped;
    private bool _launchStopped;
    private bool _launched;

    private void GoUser(int id)
    {
        EnsureNotifications();

        try
        {
            _debug!.Stop();
            _threadStopped = true;
            SyncStoppedStateFromKit();
        }
        catch (XbdmException ex)
        {
            BridgeWriter.Log($"goUser: DmStop failed: {ex.Message}");
        }

        if (!StoppedAtActiveBreakpoint())
            _stoppedAddress = 0;

        _autoRunResume = true;
        try
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                ResumeAllStoppedThreads();
                _breakEvent.Reset();
                _debug!.Go();

                if (_breakEvent.Wait(GoUserTimeoutMs))
                {
                    if (StoppedAtActiveBreakpoint() && IsTitleAddress(_stoppedAddress))
                        break;
                    if (SyncStoppedStateFromKit() && StoppedAtActiveBreakpoint() && IsTitleAddress(_stoppedAddress))
                        break;

                    var inTitle = _moduleBase != 0 && IsTitleAddress(_stoppedAddress);
                    BridgeWriter.Log(
                        $"Skipping break at 0x{_stoppedAddress:x}{(inTitle ? " (title init)" : " (non-title)")} attempt={attempt}");
                    continue;
                }

                if (SyncStoppedStateFromKit() && StoppedAtActiveBreakpoint() && IsTitleAddress(_stoppedAddress))
                {
                    BridgeWriter.Log("goUser sync stop after timeout");
                    BridgeWriter.EmitResult(id, true,
                        $"\"threadId\":{_stoppedThread},\"address\":\"0x{_stoppedAddress:x}\"");
                    return;
                }

                BridgeWriter.Log("goUser: wait timed out; ensuring title is running");
                LeaveTitleRunning(id);
                return;
            }

            if (StoppedAtActiveBreakpoint() && _moduleBase != 0 && IsTitleAddress(_stoppedAddress))
            {
                BridgeWriter.Log("goUser hit breakpoint");
                BridgeWriter.EmitResult(id, true,
                    $"\"threadId\":{_stoppedThread},\"address\":\"0x{_stoppedAddress:x}\"");
                return;
            }

            BridgeWriter.Log("goUser: no user breakpoint; leaving title running");
            LeaveTitleRunning(id);
        }
        finally
        {
            _autoRunResume = false;
        }
    }

    private void LeaveTitleRunning(int id)
    {
        ResumeAllStoppedThreads();
        BypassStoppedHardwareBreakpoint();
        _debug!.Go();

        if (AnyThreadStopped())
        {
            if (SyncStoppedStateFromKit() && StoppedAtActiveBreakpoint())
            {
                BridgeWriter.Log("leaveRunning sync breakpoint");
                BridgeWriter.EmitResult(id, true,
                    $"\"threadId\":{_stoppedThread},\"address\":\"0x{_stoppedAddress:x}\"");
                return;
            }

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

        var builder = new StringBuilder();
        builder.Append("{\"type\":\"result\",\"id\":").Append(id).Append(",\"success\":true");
        builder.Append(",\"mainThread\":").Append(_mainThread);
        builder.Append(",\"stoppedThread\":").Append(_stoppedThread);
        builder.Append(",\"moduleBase\":\"0x").Append(_moduleBase.ToString("x")).Append('"');
        builder.Append(",\"stoppedAddr\":\"0x").Append(_stoppedAddress.ToString("x")).Append('"');
        builder.Append(",\"mainEip\":\"0x").Append(mainEip.ToString("x8")).Append('"');
        builder.Append(",\"execState\":").Append(_execState);
        builder.Append(",\"threadStopped\":").Append(Bool(_threadStopped));
        builder.Append(",\"launchStopped\":").Append(Bool(_launchStopped));
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
        if (_mainThread == 0 || !IsThreadStoppedOnKit(_mainThread))
            return false;

        var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
        _debug!.GetThreadContext(_mainThread, ref context);
        if (context.Eip == 0)
            return false;

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

        var modRm = code[1];
        var reg = (modRm >> 3) & 7;
        if (reg != 2)
            return false;

        var mod = modRm >> 6;
        var rm = modRm & 7;
        var len = 2;
        if (mod == 3)
            len = 2;
        else if (mod == 0 && rm == 5)
            len = 6;
        else if (mod == 1)
            len = rm == 4 ? 4 : 3;
        else if (mod == 2)
            len = rm == 4 ? 7 : 6;
        else if (mod == 0 && rm == 4)
        {
            if (_debug.GetMemory(eip, code[..3]) < 3)
                return false;
            len = (code[2] & 7) == 5 ? 7 : 3;
        }

        if (_debug.GetMemory(eip, code[..len]) < len)
            return false;

        returnAddress = eip + (nuint)len;
        return true;
    }
}
