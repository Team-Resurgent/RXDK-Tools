using System.Text;
using System.Text.Json;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XboxDbgBridge;

internal sealed partial class DebugBridgeSession : IDisposable
{
    private const int DefaultLaunchTimeoutMs = 120_000;
    private const int DefaultWaitBreakTimeoutMs = 30_000;

    private readonly ManagedXbdmClient _client;

    internal DebugBridgeSession()
    {
        BridgeBootstrap.RegisterBackend();
        _client = (ManagedXbdmClient)XbdmClients.Create();
    }

    private readonly BreakpointManager _breakpoints = new();
    private readonly ManualResetEventSlim _breakEvent = new(false);
    private readonly ManualResetEventSlim _execPendingEvent = new(false);

    private IXbdmConnection? _connection;
    private IXbdmDebugConnection? _debug;
    private IXbdmNotificationSession? _notifySession;
    private bool _connectedToDebugger;
    private bool _active = true;
    private bool _awaitingTitleThread;
    private bool _autoRunLaunch;
    private bool _seenTitleMod;
    private string _launchTitleBase = string.Empty;
    private bool _needRearmHwBps;
    private uint _mainThread;
    private uint _stoppedThread;
    private nuint _stoppedAddress;
    private nuint _moduleBase;
    private int _execState = XbdmDebugConstants.DmnExecStart;

    internal void Initialize()
    {
        _client.Initialize();
        _debug?.UseSharedConnection(true);
        BridgeWriter.Emit("{\"type\":\"event\",\"event\":\"ready\"}");
    }

    internal bool IsActive => _active;

    internal void HandleCommand(string jsonLine, int id)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonLine);
        }
        catch (JsonException)
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"invalid json\"");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!BridgeJson.TryGetString(root, "cmd", out var cmd))
            {
                BridgeWriter.EmitResult(id, false, "\"error\":\"missing cmd\"");
                return;
            }

            try
            {
                switch (cmd.ToLowerInvariant())
                {
                    case "ping":
                        BridgeWriter.EmitResult(id, true, "\"pong\":true");
                        break;
                    case "attach":
                        Attach(id);
                        break;
                    case "launch":
                        Launch(root, id);
                        break;
                    case "go":
                        Go(id);
                        break;
                    case "gouser":
                        GoUser(id);
                        break;
                    case "stop":
                        Stop(id);
                        break;
                    case "waitbreak":
                        WaitBreak(root, id);
                        break;
                    case "step":
                        Step(root, id);
                        break;
                    case "setbreakpoint":
                        SetBreakpoint(root, id);
                        break;
                    case "clearbreakpoints":
                        ClearBreakpoints(id);
                        break;
                    case "removebreakpoint":
                        RemoveBreakpoint(root, id);
                        break;
                    case "isbreakpoint":
                        IsBreakpoint(root, id);
                        break;
                    case "getmemory":
                        GetMemory(root, id);
                        break;
                    case "getthreads":
                        GetThreads(id);
                        break;
                    case "getstack":
                        GetStack(root, id);
                        break;
                    case "loadsymbols":
                        LoadSymbols(root, id);
                        break;
                    case "resolveline":
                        ResolveLine(root, id);
                        break;
                    case "symbolinfo":
                        SymbolInfo(id);
                        break;
                    case "getvariables":
                        GetVariables(root, id);
                        break;
                    case "evaluate":
                        Evaluate(root, id);
                        break;
                    case "getmembers":
                        GetMembers(root, id);
                        break;
                    case "diag":
                        Diag(id);
                        break;
                    case "shutdown":
                        var rebootDashboard = true;
                        BridgeJson.TryGetBool(root, "rebootDashboard", out rebootDashboard);
                        if (rebootDashboard)
                            RebootToDashboard();
                        BridgeWriter.EmitResult(id, true);
                        Shutdown();
                        break;
                    default:
                        BridgeWriter.EmitResult(id, false, "\"error\":\"unknown cmd\"");
                        break;
                }
            }
            catch (XbdmException ex)
            {
                BridgeWriter.Log($"command '{cmd}' failed: {ex.Message}");
                BridgeWriter.EmitResult(id, false, $"\"error\":\"{Escape(ex.Message)}\"");
            }
            catch (Exception ex)
            {
                BridgeWriter.Log($"command '{cmd}' failed: {ex}");
                // Include type + originating frame in the result itself so the caller sees the
                // cause without relying on the (separately buffered) stderr stream.
                BridgeWriter.EmitResult(id, false, $"\"error\":\"{Escape(DescribeException(ex))}\"");
            }
        }
    }

    /// <summary>
    /// Formats an exception as "Type: message @ first-stack-frame" so failures surfaced over the
    /// stdout result channel name the originating code without relying on buffered stderr.
    /// </summary>
    internal static string DescribeException(Exception ex)
    {
        var site = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
        return $"{ex.GetType().Name}: {ex.Message}" +
            (string.IsNullOrEmpty(site) ? string.Empty : $" @ {site}");
    }

    private void EnsureConnection(string? consoleOverride = null)
    {
        if (_connection is not null)
            return;

        var console = consoleOverride;
        if (string.IsNullOrWhiteSpace(console))
            console = Environment.GetEnvironmentVariable("RXDK_XBOX");
        if (string.IsNullOrWhiteSpace(console))
            console = _client.GetDefaultConsoleName();
        else
            _client.SetDefaultConsoleName(console);

        _connection = XbdmConnectHelper.Connect(_client, console);
        _debug = _connection.Debug;
        _debug.UseSharedConnection(true);
    }

    private void EnsureNotifications()
    {
        if (_notifySession is not null)
            return;

        EnsureConnection();
        _notifySession = _debug!.OpenNotificationSession(XbdmDebugConstants.DmPersistent);
        RegisterNotifications();
    }

    private void RegisterNotifications()
    {
        if (_notifySession is null)
            return;

        void Register(int code) => _notifySession.Notify((uint)code, OnNotification);

        Register(XbdmDebugConstants.DmCreateThread);
        Register(XbdmDebugConstants.DmBreak);
        Register(XbdmDebugConstants.DmDataBreak);
        Register(XbdmDebugConstants.DmSingleStep);
        Register(XbdmDebugConstants.DmModLoad);
        Register(XbdmDebugConstants.DmDebugStr);
        Register(XbdmDebugConstants.DmException);
        Register(XbdmDebugConstants.DmAssert);
        Register(XbdmDebugConstants.DmRip);
        Register(XbdmDebugConstants.DmExec);
    }

    private void OnNotification(uint notification, object? data)
    {
        var code = (int)(notification & XbdmDebugConstants.NotificationMask);
        var stopped = (notification & XbdmDebugConstants.StopThread) != 0;
        var name = NotifyName(code);

        switch (code)
        {
            case XbdmDebugConstants.DmCreateThread when data is XbdmCreateThreadNotification created:
                if (_awaitingTitleThread && !_seenTitleMod)
                    break;
                if (_mainThread == 0)
                    _mainThread = created.ThreadId;
                _stoppedThread = created.ThreadId;
                if (stopped)
                {
                    _launchStopped = true;
                    _threadStopped = true;
                }
                BridgeWriter.EmitEvent(name,
                    $"\"threadId\":{created.ThreadId},\"startAddress\":\"0x{created.StartAddress:x}\",\"stopped\":{Bool(stopped)}");
                if (stopped)
                    SignalBreak(created.ThreadId, created.StartAddress);
                if (_awaitingTitleThread && _seenTitleMod)
                    _breakEvent.Set();
                break;

            case XbdmDebugConstants.DmBreak or XbdmDebugConstants.DmDataBreak or XbdmDebugConstants.DmSingleStep
                when data is XbdmBreakNotification brk:
                if (stopped)
                {
                    _launchStopped = true;
                    _threadStopped = true;
                }
                _stoppedThread = brk.ThreadId;
                _stoppedAddress = brk.Address;
                if (!_stepActive && !_autoRunResume)
                {
                    BridgeWriter.EmitEvent(name,
                        $"\"threadId\":{brk.ThreadId},\"address\":\"0x{brk.Address:x}\",\"stopped\":{Bool(stopped)}");
                }
                if (stopped)
                    SignalBreak(brk.ThreadId, brk.Address);
                if (_awaitingTitleThread && _seenTitleMod)
                    _breakEvent.Set();
                else if (!_awaitingTitleThread)
                    _breakEvent.Set();
                break;

            case XbdmDebugConstants.DmModLoad when data is XbdmModLoadNotification mod:
                var oldBase = _moduleBase;
                var titleMod = ModuleMatchesLaunchTitle(mod.Name);
                if (titleMod || (_awaitingTitleThread && string.IsNullOrEmpty(_launchTitleBase)))
                {
                    _moduleBase = mod.BaseAddress;
                    OnModuleBaseSet();
                }
                else if (_moduleBase == 0)
                {
                    _moduleBase = mod.BaseAddress;
                    OnModuleBaseSet();
                }

                BridgeWriter.EmitEvent(name,
                    $"\"name\":\"{Escape(mod.Name)}\",\"base\":\"0x{mod.BaseAddress:x}\",\"baseAddress\":\"0x{mod.BaseAddress:x}\",\"size\":{mod.Size}");
                if (_awaitingTitleThread && titleMod)
                {
                    _seenTitleMod = true;
                    if (_autoRunLaunch)
                        _breakEvent.Set();
                }

                OnModuleBaseChanged(oldBase);
                break;

            case XbdmDebugConstants.DmDebugStr when data is XbdmDebugStringNotification dbg:
                BridgeWriter.EmitEvent(name,
                    $"\"threadId\":{dbg.ThreadId},\"text\":\"{Escape(dbg.Text)}\",\"stopped\":{Bool(stopped)}");
                break;

            case XbdmDebugConstants.DmException when data is XbdmExceptionNotification ex:
                _stoppedThread = ex.ThreadId;
                _stoppedAddress = ex.Address;
                BridgeWriter.EmitEvent(name,
                    $"\"threadId\":{ex.ThreadId},\"code\":{ex.Code},\"address\":\"0x{ex.Address:x}\",\"stopped\":{Bool(stopped)}");
                if (stopped)
                    SignalBreak(ex.ThreadId, ex.Address);
                break;

            case XbdmDebugConstants.DmAssert when data is XbdmDebugStringNotification assert:
                BridgeWriter.EmitEvent(name,
                    $"\"threadId\":{assert.ThreadId},\"text\":\"{Escape(assert.Text)}\",\"stopped\":{Bool(stopped)}");
                break;

            case XbdmDebugConstants.DmRip:
                BridgeWriter.EmitEvent("terminated", "\"reason\":\"rip\"");
                break;

            case XbdmDebugConstants.DmExec when data is int execState:
                if (execState != _execState)
                {
                    _execState = execState;
                    BridgeWriter.EmitEvent(name, $"\"state\":{execState}");
                }
                if (execState == XbdmDebugConstants.DmnExecStart && _needRearmHwBps && _debug is not null)
                {
                    _breakpoints.RearmHardware(_debug);
                    _needRearmHwBps = false;
                }
                if (execState == XbdmDebugConstants.DmnExecPending)
                    _execPendingEvent.Set();
                break;

            default:
                BridgeWriter.EmitEvent(name, $"\"code\":{code},\"stopped\":{Bool(stopped)}");
                break;
        }
    }

    private void SignalBreak(uint threadId, nuint address)
    {
        _stoppedThread = threadId;
        _stoppedAddress = address;
        _breakEvent.Set();
    }

    private void Attach(int id)
    {
        EnsureNotifications();
        _debug!.ConnectDebugger(true);
        _connectedToDebugger = true;
        _debug.StopOn(XbdmDebugConstants.DmstopCreateThread | XbdmDebugConstants.DmstopFce | XbdmDebugConstants.DmstopDebugStr, stop: false);
        BridgeWriter.EmitResult(id, true);
    }

    private void Launch(JsonElement root, int id)
    {
        if (!BridgeJson.TryGetString(root, "dir", out var dir) ||
            !BridgeJson.TryGetString(root, "title", out var title))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing dir/title\"");
            return;
        }

        BridgeJson.TryGetString(root, "cmdline", out var cmdline);
        BridgeJson.TryGetString(root, "console", out var console);
        var timeoutMs = DefaultLaunchTimeoutMs;
        if (BridgeJson.TryGetUInt32(root, "timeout", out var timeout))
            timeoutMs = (int)Math.Min(timeout, int.MaxValue);
        BridgeJson.TryGetBool(root, "autoRun", out var autoRun);
        BridgeJson.TryGetBool(root, "reboot", out var reboot);

        EnsureConnection(string.IsNullOrWhiteSpace(console) ? null : console);
        EnsureNotifications();

        if (!dir.EndsWith('\\'))
            dir += '\\';

        _mainThread = 0;
        _moduleBase = 0;
        _symbols.ModuleBase = 0;
        _breakpoints.Clear(_debug!);
        _breakpoints.ClearPending();
        _breakEvent.Reset();

        if (autoRun)
        {
            try
            {
                EnsureLaunchReboot(reboot, timeoutMs);
                ClearBootWaitIfPending();
            }
            catch (XbdmException)
            {
                BridgeWriter.EmitResult(id, false, "\"error\":\"clearBootWait\"");
                return;
            }

            // The reboot above invalidates the shared connection; rebuild the persistent
            // notification session so the title MODLOAD can fire (see session.c CmdLaunch).
            try
            {
                RecycleNotificationSession();
            }
            catch (XbdmException)
            {
                BridgeWriter.EmitResult(id, false, "\"error\":\"notifyRecycle\"");
                return;
            }

            // Clean start: no debugger connect, no stopon — DmConnectDebugger(TRUE) hangs D3D
            // CreateDevice after InitHardware (see RXDK-VSCode session.c CmdLaunch autoRun path).
            BridgeWriter.Log("Launch autoRun: clean start (no debugger connect)");
            _debug!.SetTitle(dir, title, null);

            _mainThread = 0;
            _moduleBase = 0;
            _symbols.ModuleBase = 0;
            _seenTitleMod = false;
            _autoRunLaunch = true;
            _awaitingTitleThread = true;
            SetLaunchBaseFromTitle(title);
            _breakEvent.Reset();

            _debug.Go();
            if (!_breakEvent.Wait(timeoutMs))
            {
                _autoRunLaunch = false;
                _awaitingTitleThread = false;
                BridgeWriter.EmitResult(id, false, "\"error\":\"launchTimeout\"");
                return;
            }

            _autoRunLaunch = false;
            _awaitingTitleThread = false;
            PickMainThreadFromList();
            _launched = true;
            BridgeWriter.EmitResult(id, true,
                $"\"threadId\":{_mainThread},\"moduleBase\":\"0x{_moduleBase:x}\",\"running\":true");
            return;
        }

        if (_execState != XbdmDebugConstants.DmnExecPending)
        {
            try
            {
                EnsurePendingExec(timeoutMs);
            }
            catch (XbdmException)
            {
                BridgeWriter.EmitResult(id, false, "\"error\":\"pendingExec\"");
                return;
            }
        }

        try
        {
            RecycleNotificationSession();
        }
        catch (XbdmException)
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"notifyRecycle\"");
            return;
        }

        _debug!.SetTitle(dir, title, string.IsNullOrEmpty(cmdline) ? null : cmdline);
        _debug.SetInitialBreakpoint();
        _debug.StopOn(XbdmDebugConstants.DmstopCreateThread, stop: true);

        _seenTitleMod = false;
        _autoRunLaunch = false;
        _awaitingTitleThread = true;
        SetLaunchBaseFromTitle(title);
        _breakEvent.Reset();

        _debug.Go();
        if (!_breakEvent.Wait(timeoutMs))
        {
            _awaitingTitleThread = false;
            BridgeWriter.EmitResult(id, false, "\"error\":\"launchTimeout\"");
            return;
        }

        _awaitingTitleThread = false;

        if (_mainThread == 0)
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"titleRebooted\"");
            return;
        }

        if (_stoppedThread == 0)
            _stoppedThread = _mainThread;

        _debug.Stop();
        _debug.ConnectDebugger(true);
        _connectedToDebugger = true;
        _debug.StopOn(XbdmDebugConstants.DmstopCreateThread | XbdmDebugConstants.DmstopFce | XbdmDebugConstants.DmstopDebugStr, stop: false);
        ApplyPendingBreakpoints();

        _launched = true;
        BridgeWriter.EmitResult(id, true, $"\"threadId\":{_mainThread},\"moduleBase\":\"0x{_moduleBase:x}\"");
    }

    private void Go(int id)
    {
        EnsureNotifications();
        if (IsStoppedAtSoftwareBreakpoint())
            ResumeStoppedThread(exception: true);
        else
            ResumeAllStoppedThreads();

        BypassStoppedHardwareBreakpoint();
        _debug!.Go();
        BridgeWriter.EmitResult(id, true);
    }

    private void Stop(int id)
    {
        EnsureNotifications();
        _debug!.Stop();
        BridgeWriter.EmitResult(id, true);
    }

    private void WaitBreak(JsonElement root, int id)
    {
        EnsureNotifications();
        var timeoutMs = DefaultWaitBreakTimeoutMs;
        if (BridgeJson.TryGetUInt32(root, "timeout", out var timeout))
            timeoutMs = (int)timeout;

        // A fast (per-frame) breakpoint can already have halted the title before we start waiting,
        // so check the kit first instead of unconditionally resetting and waiting for the next hit.
        if (TryReportActiveBreakpointStop(id))
            return;

        _breakEvent.Reset();
        if (!_breakEvent.Wait(timeoutMs))
        {
            // The hit may have raced with the reset above; reconcile against the kit before failing.
            if (TryReportActiveBreakpointStop(id))
                return;

            BridgeWriter.EmitResult(id, false, "\"error\":\"waitTimeout\"");
            return;
        }

        BridgeWriter.EmitResult(id, true, $"\"threadId\":{_stoppedThread},\"address\":\"0x{_stoppedAddress:x}\"");
    }

    /// <summary>
    /// If the title is currently halted at one of our active breakpoints, emit a success result and
    /// return true. Used to make WAITBREAK robust against breakpoints that fire before/while we wait.
    /// </summary>
    private bool TryReportActiveBreakpointStop(int id)
    {
        if (!AnyThreadStopped() || !SyncStoppedStateFromKit() || !StoppedAtActiveBreakpoint())
            return false;

        BridgeWriter.EmitResult(id, true, $"\"threadId\":{_stoppedThread},\"address\":\"0x{_stoppedAddress:x}\"");
        return true;
    }

    private void Step(JsonElement root, int id)
    {
        EnsureNotifications();
        var threadId = _stoppedThread != 0 ? _stoppedThread : _mainThread;
        if (BridgeJson.TryGetUInt32(root, "threadId", out var requested))
            threadId = requested;
        BridgeJson.TryGetBool(root, "over", out var stepOver);

        if (threadId == 0)
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"noThread\"");
            return;
        }

        nuint? softBp = SoftBreakpointAddress();
        var removedSoftBp = false;
        nuint? tempStepOverBp = null;
        _stepActive = true;

        if (softBp is not null)
        {
            _debug!.RemoveBreakpoint(softBp.Value);
            _breakpoints.Untrack(softBp.Value);
            removedSoftBp = true;
        }

        try
        {
            if (stepOver && TryGetCallReturnAddress(threadId, out var returnAddress))
            {
                var result = _breakpoints.Install(_debug!, returnAddress, null, 0, NormalizeBpAddress, IsKitBpAddress);
                if (result != BreakpointManager.InstallResult.Success)
                    throw new XbdmException("installBreakpoint", -1);
                tempStepOverBp = returnAddress;
            }
            else
            {
                var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
                _debug!.GetThreadContext(threadId, ref context);
                context.EFlags |= 0x100;
                _debug.SetThreadContext(threadId, ref context);
            }

            _breakEvent.Reset();
            ResumeStoppedThread(exception: false);
            BypassStoppedHardwareBreakpoint();
            _debug!.Go();
            if (!_breakEvent.Wait(DefaultWaitBreakTimeoutMs))
            {
                if (tempStepOverBp is not null)
                    _breakpoints.Remove(_debug, tempStepOverBp.Value);
                if (removedSoftBp && softBp is not null)
                    _breakpoints.Install(_debug, softBp.Value, null, 0, NormalizeBpAddress, IsKitBpAddress);
                if (_needRearmHwBps)
                {
                    _breakpoints.RearmHardware(_debug);
                    _needRearmHwBps = false;
                }

                BridgeWriter.EmitResult(id, false, "\"error\":\"stepTimeout\"");
                return;
            }

            if (tempStepOverBp is not null)
                _breakpoints.Remove(_debug, tempStepOverBp.Value);
            if (removedSoftBp && softBp is not null)
                _breakpoints.Install(_debug, softBp.Value, null, 0, NormalizeBpAddress, IsKitBpAddress);
            if (_needRearmHwBps)
            {
                _breakpoints.RearmHardware(_debug);
                _needRearmHwBps = false;
            }

            BridgeWriter.EmitResult(id, true, $"\"threadId\":{_stoppedThread},\"address\":\"0x{_stoppedAddress:x}\"");
        }
        catch (XbdmException)
        {
            if (tempStepOverBp is not null)
                _breakpoints.Remove(_debug!, tempStepOverBp.Value);
            if (removedSoftBp && softBp is not null)
                _breakpoints.Install(_debug!, softBp.Value, null, 0, NormalizeBpAddress, IsKitBpAddress);
            throw;
        }
        finally
        {
            _stepActive = false;
        }
    }

    private void SetBreakpoint(JsonElement root, int id)
    {
        EnsureNotifications();
        BridgeJson.TryGetBool(root, "queue", out var queue);
        if (!queue)
            queue = true;

        BridgeJson.TryGetString(root, "file", out var file);
        BridgeJson.TryGetUInt32(root, "line", out var line);
        nuint address = 0;
        var hasAddress = BridgeJson.TryGetAddress(root, "address", out address);

        if (hasAddress)
        {
            address = NormalizeBpAddress(address);
            if (!IsKitBpAddress(address))
            {
                if (queue && !string.IsNullOrEmpty(file) && line != 0)
                {
                    _breakpoints.QueuePending(file, line);
                    BridgeWriter.EmitResult(id, true, $"\"address\":\"0x{address:x}\",\"pending\":true");
                    return;
                }

                BridgeWriter.EmitResult(id, false, "\"error\":\"badAddress\"");
                return;
            }

            var install = _breakpoints.Install(_debug!, address, string.IsNullOrEmpty(file) ? null : file, line,
                NormalizeBpAddress, IsKitBpAddress);
            EmitBreakpointResult(id, address, install);
            return;
        }

        if (!string.IsNullOrEmpty(file) && line != 0)
        {
            if (_moduleBase == 0 && queue)
            {
                _breakpoints.QueuePending(file, line);
                if (!_symbols.TryResolveLine(file, line, out address))
                {
                    BridgeWriter.EmitResult(id, false, "\"error\":\"resolveLine\"");
                    return;
                }

                BridgeWriter.EmitResult(id, true, $"\"address\":\"0x{address:x}\",\"pending\":true");
                return;
            }

            if (!_symbols.TryResolveLine(file, line, out address))
            {
                BridgeWriter.EmitResult(id, false, "\"error\":\"resolveLine\"");
                return;
            }

            var install = _breakpoints.Install(_debug!, address, file, line, NormalizeBpAddress, IsKitBpAddress);
            EmitBreakpointResult(id, address, install);
            return;
        }

        BridgeWriter.EmitResult(id, false, "\"error\":\"missing address or file/line\"");
    }

    private void EmitBreakpointResult(int id, nuint address, BreakpointManager.InstallResult install)
    {
        var armed = install == BreakpointManager.InstallResult.Success &&
                    _debug!.GetBreakpointType(address) != XbdmDebugConstants.DmbreakNone;
        var extra = $"\"address\":\"0x{address:x}\",\"armed\":{Bool(armed)}";
        if (install == BreakpointManager.InstallResult.HardwareFull)
            extra += ",\"error\":\"hwBpFull\"";
        else if (install == BreakpointManager.InstallResult.InvalidAddress)
            extra += ",\"error\":\"badAddress\"";
        else if (install != BreakpointManager.InstallResult.Success)
            extra += ",\"error\":\"installBreakpoint\"";
        BridgeWriter.EmitResult(id, install == BreakpointManager.InstallResult.Success, extra);
    }

    private void ClearBreakpoints(int id)
    {
        EnsureNotifications();
        _breakpoints.Clear(_debug!);
        _breakpoints.ClearPending();
        BridgeWriter.EmitResult(id, true);
    }

    private void RemoveBreakpoint(JsonElement root, int id)
    {
        EnsureNotifications();
        if (!BridgeJson.TryGetAddress(root, "address", out var address))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing address\"");
            return;
        }

        _breakpoints.Remove(_debug!, address);
        BridgeWriter.EmitResult(id, true);
    }

    private void IsBreakpoint(JsonElement root, int id)
    {
        EnsureNotifications();
        if (!BridgeJson.TryGetAddress(root, "address", out var address))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing address\"");
            return;
        }

        var type = _debug!.GetBreakpointType(address);
        BridgeWriter.EmitResult(id, true, $"\"type\":{type}");
    }

    private void GetMemory(JsonElement root, int id)
    {
        EnsureNotifications();
        if (!BridgeJson.TryGetAddress(root, "address", out var address))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing address\"");
            return;
        }

        var length = 16u;
        BridgeJson.TryGetUInt32(root, "length", out length);
        if (length == 0 || length > 64)
            length = 16;

        var buffer = new byte[length];
        var read = _debug!.GetMemory(address, buffer);
        var hex = Convert.ToHexString(buffer.AsSpan(0, read)).ToLowerInvariant();
        BridgeWriter.Emit(
            $"{{\"type\":\"result\",\"id\":{id},\"success\":true,\"bytes\":\"{hex}\",\"length\":{read}}}");
    }

    private void GetThreads(int id)
    {
        EnsureNotifications();
        var threads = _debug!.GetThreadList();
        var payload = string.Join(',', threads);
        BridgeWriter.Emit($"{{\"type\":\"result\",\"id\":{id},\"success\":true,\"threads\":[{payload}]}}");
    }

    private void GetStack(JsonElement root, int id)
    {
        EnsureNotifications();
        var threadId = _stoppedThread != 0 ? _stoppedThread : _mainThread;
        if (BridgeJson.TryGetUInt32(root, "threadId", out var requested))
            threadId = requested;

        var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl };
        _debug!.GetThreadContext(threadId, ref context);

        var builder = new StringBuilder();
        builder.Append("{\"type\":\"result\",\"id\":").Append(id).Append(",\"success\":true,\"frames\":[");
        var address = (nuint)context.Eip;
        var ebp = context.Ebp;
        Span<byte> buffer = stackalloc byte[4];
        for (var frame = 0; frame < 8; frame++)
        {
            if (frame > 0)
                builder.Append(',');
            _symbols.TryAddressToLine(address, out var file, out var line, out var function);
            builder.Append("{\"index\":").Append(frame)
                .Append(",\"address\":\"0x").Append(address.ToString("x")).Append("\",\"name\":");
            BridgeJsonWriter.AppendEscaped(builder, string.IsNullOrEmpty(function) ? "???" : function);
            builder.Append(",\"file\":");
            BridgeJsonWriter.AppendEscaped(builder, file);
            builder.Append(",\"line\":").Append(line).Append('}');

            if (_debug.GetMemory((nuint)(ebp + 4), buffer) != 4)
                break;
            address = BitConverter.ToUInt32(buffer);
            if (_debug.GetMemory((nuint)ebp, buffer) != 4)
                break;
            ebp = BitConverter.ToUInt32(buffer);
        }

        builder.Append("]}");
        BridgeWriter.Emit(builder.ToString());
    }

    private void RebootToDashboard()
    {
        if (_debug is null)
            return;

        try
        {
            _debug.StopOn(XbdmDebugConstants.DmstopCreateThread | XbdmDebugConstants.DmstopFce | XbdmDebugConstants.DmstopDebugStr, stop: false);
            _debug.Stop();
            if (_connectedToDebugger)
            {
                _debug.ConnectDebugger(false);
                _connectedToDebugger = false;
            }

            _breakpoints.Clear(_debug);
            _mainThread = 0;
            _stoppedThread = 0;
            _stoppedAddress = 0;
            _moduleBase = 0;
            _debug.Reboot(XbdmDebugConstants.DmbootWarm);
        }
        catch (XbdmException ex)
        {
            BridgeWriter.Log($"Dashboard reboot failed: {ex.Message}");
        }
    }

    private void Shutdown()
    {
        _active = false;
        _launched = false;
        if (_connectedToDebugger && _debug is not null)
        {
            try
            {
                _debug.ConnectDebugger(false);
            }
            catch (XbdmException)
            {
            }

            _connectedToDebugger = false;
        }

        _notifySession?.Dispose();
        _notifySession = null;
        _connection?.Dispose();
        _connection = null;
        _debug = null;
    }

    public void Dispose()
    {
        Shutdown();
        _symbols.Dispose();
        _breakEvent.Dispose();
        _execPendingEvent.Dispose();
        _client.Dispose();
    }

    private void EnsureLaunchReboot(bool reboot, int timeoutMs)
    {
        // Match xboxdbg-bridge (session.c): only skip the reboot when the caller did not
        // request one AND the kit is already waiting for a title. Otherwise always reboot,
        // because GO fails unless the kit is in DMN_EXEC_PENDING.
        if (!reboot && _execState == XbdmDebugConstants.DmnExecPending)
            return;

        _execPendingEvent.Reset();
        BridgeWriter.Log("Rebooting Xbox (WARM|WAIT)...");
        _debug!.Reboot(XbdmDebugConstants.DmbootWarm | XbdmDebugConstants.DmbootWait);
        if (!_execPendingEvent.Wait(timeoutMs))
            throw new XbdmException("launchReboot timeout", -1);
        if (_execState != XbdmDebugConstants.DmnExecPending)
            throw new XbdmException("launchReboot", -1);
    }

    private void ClearBootWaitIfPending()
    {
        if (_execState != XbdmDebugConstants.DmnExecPending)
            return;

        BridgeWriter.Log("Kit DMN_EXEC_PENDING — brief debugger connect to clear boot wait");
        _debug!.ConnectDebugger(true);
        _connectedToDebugger = true;
        _debug.ConnectDebugger(false);
        _connectedToDebugger = false;
    }

    private void SetLaunchBaseFromTitle(string title)
    {
        _launchTitleBase = Path.GetFileNameWithoutExtension(title).ToLowerInvariant();
    }

    private bool ModuleMatchesLaunchTitle(string name)
    {
        if (string.IsNullOrEmpty(_launchTitleBase))
            return false;
        var baseName = Path.GetFileName(name.Replace('/', '\\')).ToLowerInvariant();
        return baseName.Contains(_launchTitleBase, StringComparison.Ordinal);
    }

    private void PickMainThreadFromList()
    {
        if (_mainThread != 0 || _debug is null)
            return;
        var threads = _debug.GetThreadList();
        if (threads.Count > 0)
            _mainThread = threads[0];
    }

    private void EnsurePendingExec(int timeoutMs)
    {
        if (_execState == XbdmDebugConstants.DmnExecPending)
            return;

        _execPendingEvent.Reset();
        BridgeWriter.Log("Rebooting Xbox to pending exec (STOP|WARM)...");
        _debug!.Reboot(XbdmDebugConstants.DmbootStop | XbdmDebugConstants.DmbootWarm);
        if (!_execPendingEvent.Wait(timeoutMs))
            throw new XbdmException("pendingExec timeout", -1);
        if (_execState != XbdmDebugConstants.DmnExecPending)
            throw new XbdmException("pendingExec", -1);
    }

    private void RecycleNotificationSession()
    {
        _notifySession?.Dispose();
        _notifySession = null;
        _debug!.UseSharedConnection(false);
        _debug.UseSharedConnection(true);
        EnsureNotifications();
    }

    private void ResumeAllStoppedThreads()
    {
        if (_debug is null)
            return;
        foreach (var threadId in _debug.GetThreadList())
        {
            if (_debug.TryGetThreadStop(threadId) is not null)
                _debug.ContinueThread(threadId, exception: false);
        }
    }

    private void ResumeStoppedThread(bool exception)
    {
        if (_debug is null || _stoppedThread == 0)
            return;
        var needContinue = _threadStopped || _launchStopped || IsThreadStoppedOnKit(_stoppedThread);
        if (!needContinue)
            return;
        _debug.ContinueThread(_stoppedThread, exception);
        _threadStopped = false;
        _launchStopped = false;
    }

    private void BypassStoppedHardwareBreakpoint()
    {
        if (_debug is null || _stoppedAddress == 0 || !_breakpoints.IsHardware(_stoppedAddress))
            return;
        _debug.SetDataBreakpoint(_stoppedAddress, XbdmDebugConstants.DmbreakNone, 1);
        _needRearmHwBps = true;
    }

    private bool IsStoppedAtSoftwareBreakpoint()
    {
        if (_debug is null || _stoppedAddress == 0)
            return false;
        var bpType = _debug.GetBreakpointType(_stoppedAddress);
        return bpType != XbdmDebugConstants.DmbreakNone &&
               bpType != XbdmDebugConstants.DmbreakExecute &&
               _breakpoints.IsActive(_stoppedAddress) &&
               !_breakpoints.IsHardware(_stoppedAddress);
    }

    private nuint? SoftBreakpointAddress()
    {
        if (_stoppedAddress == 0 || _debug is null)
            return null;
        var bpType = _debug.GetBreakpointType(_stoppedAddress);
        if (bpType == XbdmDebugConstants.DmbreakNone)
            return null;
        if (bpType == XbdmDebugConstants.DmbreakExecute)
            return null;
        return _breakpoints.IsActive(_stoppedAddress) ? _stoppedAddress : null;
    }

    private static string NotifyName(int code) => code switch
    {
        XbdmDebugConstants.DmBreak => "break",
        XbdmDebugConstants.DmDataBreak => "break",
        XbdmDebugConstants.DmSingleStep => "singlestep",
        XbdmDebugConstants.DmCreateThread => "createthread",
        XbdmDebugConstants.DmModLoad => "modload",
        XbdmDebugConstants.DmDebugStr => "debugstr",
        XbdmDebugConstants.DmException => "exception",
        XbdmDebugConstants.DmAssert => "assert",
        XbdmDebugConstants.DmRip => "rip",
        XbdmDebugConstants.DmExec => "exec",
        _ => "notify",
    };

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
