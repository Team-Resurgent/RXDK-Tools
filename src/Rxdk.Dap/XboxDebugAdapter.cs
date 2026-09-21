using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using Thread = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Thread;

namespace Rxdk.Dap;

/// <summary>
/// The RXDK debug adapter: translates DAP requests into xboxdbg-bridge commands. C# port of
/// RXDK-VSCode debug/src/debugSession.ts, on top of Microsoft's VSCodeDebugProtocol
/// DebugAdapterBase (the same base VS's Debug Adapter Host hosts). Launch is deferred until
/// configurationDone so breakpoints are counted first; the bridge either stops at entry
/// (breakpoints present) or auto-runs (none).
/// </summary>
public sealed partial class XboxDebugAdapter : DebugAdapterBase
{
    private BridgeClient _bridge = null!;
    private readonly Dictionary<string, string> _breakpointMap = new();
    private int _stoppedThreadId = 1;
    private string _workspaceRoot = "";
    private string _srcRoot = "";
    private Dictionary<string, List<string>>? _srcRootIndex;
    private string _extensionPath = "";
    private string _bridgePathOverride = "";
    private string _titleOutputFile = "";
    private volatile bool _configurationDone;
    private bool _launchFinished;
    private bool _startupFinished;
    private volatile bool _startupGoInProgress;
    private volatile bool _stepInProgress;
    private long _ignoreBridgeStopUntil;
    private Timer? _configurationFallbackTimer;
    private Task? _sessionReady;
    private readonly Dictionary<int, string> _varChildren = new();
    private int _nextChildRef = 100;
    private readonly Dictionary<string, Dictionary<int, string>> _fileBreakpointAddrs = new();
    private volatile bool _launchStartupInProgress;
    private JObject? _pendingLaunchArgs;
    private int _breakpointSetupInFlight;
    private bool _postLaunchHandled;
    private Timer? _runAfterConfiguredTimer;
    private volatile bool _shuttingDown;

    public XboxDebugAdapter(Stream stdin, Stream stdout)
    {
        InitializeProtocolClient(stdin, stdout);
    }

    public void Run()
    {
        Protocol.Run();
        Protocol.WaitForReader();
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Console_(string text) =>
        Protocol.SendEvent(new OutputEvent(text) { Category = OutputEvent.CategoryValue.Console });

    // ---- lifecycle ----

    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
    {
        Protocol.SendEvent(new InitializedEvent());
        return new InitializeResponse
        {
            SupportsConfigurationDoneRequest = true,
            SupportsEvaluateForHovers = true,
            SupportsSetVariable = false,
            SupportsConditionalBreakpoints = false,
            SupportsHitConditionalBreakpoints = false,
            SupportsLogPoints = false,
            SupportsStepBack = false,
            SupportsDelayedStackTraceLoading = false,
            SupportsSingleThreadExecutionRequests = true,
        };
    }

    protected override async void HandleConfigurationDoneRequestAsync(IRequestResponder<ConfigurationDoneArguments> responder)
    {
        _configurationDone = true;
        responder.SetResponse(new ConfigurationDoneResponse());
        ScheduleRunAfterConfigured();
        await Task.CompletedTask;
    }

    protected override async void HandleLaunchRequestAsync(IRequestResponder<LaunchArguments> responder)
    {
        var args = ConfigProps(responder.Arguments.ConfigurationProperties);
        if (GetBool(args, "buildOnly"))
        {
            // preLaunchTask (the build) already ran; nothing to do.
            responder.SetResponse(new LaunchResponse());
            Protocol.SendEvent(new TerminatedEvent());
            return;
        }
        try
        {
            _launchStartupInProgress = true;
            _srcRoot = GetStr(args, "srcRoot") is { Length: > 0 } sr ? Path.GetFullPath(sr) : "";
            _workspaceRoot = FirstNonEmpty(GetStr(args, "__workspaceFolder"), _srcRoot, ImageDir(args), Directory.GetCurrentDirectory());
            _extensionPath = GetStr(args, "__extensionPath") ?? "";
            _titleOutputFile = GetStr(args, "__titleOutputFile") ?? "";
            if (GetStr(args, "bridgePath") is { Length: > 0 } bp)
                _bridgePathOverride = ExpandVars(bp);
            _pendingLaunchArgs = args;
            _sessionReady = PrepareSessionAsync(args);
            await _sessionReady;
            Console_("xbox-dap: launch deferred until configurationDone (breakpoints counted first)\n");
            responder.SetResponse(new LaunchResponse());
            ArmConfigurationDoneFallback();
            if (_configurationDone) ScheduleRunAfterConfigured();
        }
        catch (Exception e)
        {
            _launchStartupInProgress = false;
            _pendingLaunchArgs = null;
            responder.SetError(new ProtocolException(e.Message));
        }
    }

    protected override async void HandleAttachRequestAsync(IRequestResponder<AttachArguments> responder)
    {
        var args = ConfigProps(responder.Arguments.ConfigurationProperties);
        try
        {
            _titleOutputFile = GetStr(args, "__titleOutputFile") ?? "";
            _extensionPath = GetStr(args, "__extensionPath") ?? "";
            if (GetStr(args, "bridgePath") is { Length: > 0 } bp)
                _bridgePathOverride = ExpandVars(bp);
            if (GetStr(args, "__workspaceFolder") is { Length: > 0 } wf) _workspaceRoot = wf;
            else if (GetStr(args, "program") is { Length: > 0 } prog) _workspaceRoot = Path.GetDirectoryName(Path.GetFullPath(prog)) ?? "";

            _sessionReady = PrepareSessionAsync(args);
            await _sessionReady;
            await _bridge.RequestAsync("attach", Args(("console", GetStr(args, "consoleName"))));
            responder.SetResponse(new AttachResponse());
        }
        catch (Exception e)
        {
            responder.SetError(new ProtocolException(e.Message));
        }
    }

    protected override async void HandleDisconnectRequestAsync(IRequestResponder<DisconnectArguments> responder)
    {
        _shuttingDown = true;
        try
        {
            Console_("xbox-dap: stopping — rebooting devkit to dashboard...\n");
            if (_bridge is not null) await _bridge.ShutdownAsync(true);
        }
        catch (Exception e)
        {
            Console_($"xbox-dap: shutdown warning: {e.Message}\n");
        }
        responder.SetResponse(new DisconnectResponse());
    }

    // ---- breakpoints ----

    protected override async void HandleSetBreakpointsRequestAsync(IRequestResponder<SetBreakpointsArguments, SetBreakpointsResponse> responder)
    {
        Interlocked.Increment(ref _breakpointSetupInFlight);
        try
        {
            var resp = await SetBreakpointsCoreAsync(responder.Arguments);
            responder.SetResponse(resp);
        }
        catch (Exception e)
        {
            responder.SetError(new ProtocolException(e.Message));
        }
        finally
        {
            Interlocked.Decrement(ref _breakpointSetupInFlight);
            if (_configurationDone && !_postLaunchHandled) ScheduleRunAfterConfigured();
        }
    }

    private async Task<SetBreakpointsResponse> SetBreakpointsCoreAsync(SetBreakpointsArguments args)
    {
        if (_sessionReady is not null)
        {
            try { await _sessionReady; } catch { /* ignore */ }
        }
        var sourcePath = NormalizeSourcePath(args.Source?.Path ?? "");
        var lines = args.Breakpoints ?? new List<SourceBreakpoint>();
        var verified = new List<Breakpoint>();
        var prev = _fileBreakpointAddrs.GetValueOrDefault(sourcePath) ?? new Dictionary<int, string>();
        var nextLines = new HashSet<int>(lines.Select(b => b.Line));

        foreach (var (line, addr) in prev)
        {
            if (!nextLines.Contains(line) && !string.IsNullOrEmpty(addr))
            {
                try { await _bridge.RequestAsync("removeBreakpoint", Args(("address", addr))); } catch { /* stale */ }
                _breakpointMap.Remove(BpKey(sourcePath, line));
            }
        }

        var nextMap = new Dictionary<int, string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var bp = lines[i];
            var key = BpKey(sourcePath, bp.Line);
            var installed = await InstallBreakpointAsync(sourcePath, bp.Line, true);
            if (!string.IsNullOrEmpty(installed.Address))
            {
                _breakpointMap[key] = installed.Address;
                nextMap[bp.Line] = installed.Address;
            }
            else
            {
                _breakpointMap.Remove(key);
            }
            verified.Add(new Breakpoint(verified: installed.Verified) { Id = i + 1, Line = bp.Line, Message = installed.Message });
        }
        _fileBreakpointAddrs[sourcePath] = nextMap;
        return new SetBreakpointsResponse(verified);
    }

    protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments) =>
        new SetExceptionBreakpointsResponse();

    // ---- execution ----

    protected override async void HandleContinueRequestAsync(IRequestResponder<ContinueArguments, ContinueResponse> responder)
    {
        // Answer the continue request FIRST, then resume. The bridge's "go" returns as soon as the
        // thread is resumed (not when it next stops), so the StoppedEvent for the next breakpoint can
        // fire almost immediately on the notification thread — and a StoppedEvent that reaches VS while
        // the continue request is still unanswered is DROPPED, which made a 2nd breakpoint look like it
        // was never hit. Responding up front guarantees the ContinueResponse precedes any StoppedEvent.
        responder.SetResponse(new ContinueResponse { AllThreadsContinued = true });
        try
        {
            await _bridge.RequestAsync("go");
        }
        catch (Exception e)
        {
            Console_($"continue failed: {e.Message}\n");
        }
    }

    protected override async void HandlePauseRequestAsync(IRequestResponder<PauseArguments> responder)
    {
        await _bridge.RequestAsync("stop");
        responder.SetResponse(new PauseResponse());
        NotifyStopped("pause", _stoppedThreadId);
    }

    protected override async void HandleNextRequestAsync(IRequestResponder<NextArguments> responder)
    {
        await RunStepAndWaitAsync(responder.Arguments.ThreadId, stepOver: true);
        responder.SetResponse(new NextResponse());
    }

    protected override async void HandleStepInRequestAsync(IRequestResponder<StepInArguments> responder)
    {
        await RunStepAndWaitAsync(responder.Arguments.ThreadId);
        responder.SetResponse(new StepInResponse());
    }

    protected override async void HandleStepOutRequestAsync(IRequestResponder<StepOutArguments> responder)
    {
        await RunStepAndWaitAsync(responder.Arguments.ThreadId);
        responder.SetResponse(new StepOutResponse());
    }

    // ---- inspection ----

    protected override async void HandleThreadsRequestAsync(IRequestResponder<ThreadsArguments, ThreadsResponse> responder)
    {
        var fallbackId = _stoppedThreadId > 0 ? _stoppedThreadId : 1;
        try
        {
            var result = await _bridge.RequestAsync("getThreads");
            var ids = new List<int>();
            if (result.TryGet("threads", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                ids = arr.EnumerateArray().Select(x => x.GetInt32()).Distinct().ToList();
            if (_stoppedThreadId > 0) ids = new List<int> { _stoppedThreadId };
            else if (ids.Count == 0) ids = new List<int> { fallbackId };
            else if (!ids.Contains(fallbackId)) ids.Insert(0, fallbackId);
            responder.SetResponse(new ThreadsResponse(ids.Select(id => new Thread(id, $"Thread {id}")).ToList()));
        }
        catch
        {
            responder.SetResponse(new ThreadsResponse(new List<Thread> { new Thread(fallbackId, $"Thread {fallbackId}") }));
        }
    }

    protected override async void HandleStackTraceRequestAsync(IRequestResponder<StackTraceArguments, StackTraceResponse> responder)
    {
        var args = responder.Arguments;
        var startFrame = Math.Max(0, args.StartFrame ?? 0);
        var levels = args.Levels ?? 0;
        var framesRaw = new List<JObject>();
        try
        {
            var result = await _bridge.RequestAsync("getStack", Args(("threadId", args.ThreadId)), 15000);
            framesRaw = DedupeStackFrames(ToObjects(result, "frames"));
        }
        catch (Exception e)
        {
            Console_($"stackTrace failed: {e.Message}\n");
        }
        var end = levels > 0 ? startFrame + levels : framesRaw.Count;
        end = Math.Min(end, framesRaw.Count);
        var slice = startFrame < framesRaw.Count ? framesRaw.GetRange(startFrame, Math.Max(0, end - startFrame)) : new List<JObject>();
        var stackFrames = new List<StackFrame>();
        for (var i = 0; i < slice.Count; i++)
        {
            var f = slice[i];
            var name = f.Value<string>("name") ?? "???";
            var file = f.Value<string>("file") ?? "";
            var resolved = string.IsNullOrEmpty(file) ? "" : ResolveWorkspacePath(file);
            var line = f.Value<int?>("line") ?? 0;
            var idx = startFrame + i;
            var frame = new StackFrame(idx, name, line, 0);
            if (!string.IsNullOrEmpty(resolved))
                frame.Source = new Source { Name = Path.GetFileName(resolved), Path = resolved };
            stackFrames.Add(frame);
        }
        if (stackFrames.Count == 0 && startFrame == 0)
            stackFrames.Add(new StackFrame(0, "main", 0, 0));
        responder.SetResponse(new StackTraceResponse(stackFrames) { TotalFrames = framesRaw.Count > 0 ? framesRaw.Count : 1 });
    }

    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments) =>
        new ScopesResponse(new List<Scope>
        {
            new Scope("Locals", 1, false),
            new Scope("Globals", 2, false),
            new Scope("Registers", 3, false),
        });

    protected override async void HandleVariablesRequestAsync(IRequestResponder<VariablesArguments, VariablesResponse> responder)
    {
        var reference = responder.Arguments.VariablesReference;
        var scope = reference switch { 1 => "locals", 2 => "globals", 3 => "registers", _ => null };
        var childBase = _varChildren.GetValueOrDefault(reference);
        var variables = new List<Variable>();
        try
        {
            if (childBase is not null)
            {
                var result = await _bridge.RequestAsync("getMembers", Args(("name", childBase), ("threadId", _stoppedThreadId)));
                var raw = ToObjects(result, "variables");
                for (var i = 0; i < raw.Count; i++)
                {
                    var name = raw[i].Value<string>("name") ?? $"field{i}";
                    var value = raw[i].Value<string>("value") ?? "???";
                    // An aggregate member (struct/array) is itself expandable: give it a child ref
                    // keyed by its full path so it can be drilled into further, mirroring top-level
                    // variables. Without this, nested members (e.g. g_AntiAliasModes[0]) were leaves.
                    if (raw[i].Value<bool?>("expandable") == true)
                    {
                        var childRef = _nextChildRef++;
                        _varChildren[childRef] = raw[i].Value<string>("base") ?? name;
                        variables.Add(new Variable(name, value, childRef));
                    }
                    else
                    {
                        variables.Add(new Variable(name, value, 0));
                    }
                }
            }
            else if (scope is not null)
            {
                var reqArgs = new List<(string, object?)> { ("scope", scope), ("threadId", _stoppedThreadId) };
                var result = await _bridge.RequestAsync("getVariables", Args(reqArgs.ToArray()), scope == "globals" ? 30000 : 15000);
                var raw = ToObjects(result, "variables");
                for (var i = 0; i < raw.Count; i++)
                {
                    var name = raw[i].Value<string>("name") ?? $"var{i}";
                    var value = raw[i].Value<string>("value") ?? "???";
                    if (raw[i].Value<bool?>("expandable") == true)
                    {
                        var childRef = _nextChildRef++;
                        _varChildren[childRef] = raw[i].Value<string>("base") ?? name;
                        variables.Add(new Variable(name, value, childRef));
                    }
                    else
                    {
                        variables.Add(new Variable(name, value, 0));
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console_($"variables failed: {e.Message}\n");
        }
        responder.SetResponse(new VariablesResponse(variables));
    }

    protected override async void HandleEvaluateRequestAsync(IRequestResponder<EvaluateArguments, EvaluateResponse> responder)
    {
        // A data-tip HOVER sends whatever token is under the cursor while debugging — a comment, a
        // preprocessor macro, a keyword — most of which isn't a runtime expression. VS shows a data tip
        // only when the evaluate SUCCEEDS, so on failure we fail the request quietly (no tooltip) rather
        // than returning a "badExpression"/"symbol not found" string that VS renders as an error box.
        // Watch / Immediate (repl) keep the descriptive reason so the user can see why it didn't resolve.
        var isHover = responder.Arguments.Context == EvaluateArguments.ContextValue.Hover;
        try
        {
            var result = await _bridge.RequestAsync("evaluate", Args(("expression", responder.Arguments.Expression), ("threadId", _stoppedThreadId)));
            // An aggregate result (struct/array) is drillable: hand back a variablesReference keyed by
            // the base the bridge reported, so VS shows an expand chevron on the hover/Watch value and
            // fetches children via getMembers — the same path Locals uses.
            var childRef = 0;
            if (result.GetBool("expandable"))
            {
                childRef = _nextChildRef++;
                _varChildren[childRef] = result.GetString("base") ?? responder.Arguments.Expression;
            }
            responder.SetResponse(new EvaluateResponse(result.GetString("value") ?? "???", childRef));
        }
        catch (Exception e)
        {
            if (isHover)
            {
                responder.SetError(new ProtocolException(e.Message));
                return;
            }
            var msg = e.Message;
            var resultText = msg.Contains("memberNotFound") ? "member not found (try expanding struct in Locals, or d3pp.SwapEffect)"
                : msg.Contains("symbolNotFound") ? "symbol not found"
                : msg.Contains("readFailed") ? "could not read memory"
                : $"error: {msg}";
            responder.SetResponse(new EvaluateResponse(resultText, 0));
        }
    }
}
