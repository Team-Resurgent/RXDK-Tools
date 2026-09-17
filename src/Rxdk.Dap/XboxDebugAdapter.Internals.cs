using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace Rxdk.Dap;

// State-machine internals of the RXDK debug adapter — port of the private methods of
// debugSession.ts. Kept in a partial file to separate the DAP request handlers (public
// surface) from the bridge orchestration (launch sequencing, breakpoint install, stop
// detection, symbol/path resolution).
public sealed partial class XboxDebugAdapter
{
    private readonly record struct InstallResult(bool Verified, string Address, string? Message);

    // ---- session / bridge bring-up ----

    private async Task StartBridgeAsync(string? consoleName)
    {
        var bridgePath = BridgePath.Resolve(string.IsNullOrEmpty(_bridgePathOverride) ? null : _bridgePathOverride);
        // Export the session's console to the bridge (RXDK_XBOX) so pre-launch breakpoint commands
        // target it, not the machine-wide default console (which VS Code, unlike VS, exercises because
        // it issues setBreakpoints before the launch that would otherwise carry the console).
        _bridge = new BridgeClient(bridgePath, consoleName);
        _bridge.Log += msg =>
        {
            foreach (var line in msg.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) Console_($"bridge: {trimmed}\n");
            }
        };

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEvent(BridgeMessage ev)
        {
            if (ev.Event == "ready") ready.TrySetResult();
            OnBridgeEvent(ev);
        }
        _bridge.BridgeEvent += OnEvent;
        _bridge.Start();
        Console_($"xbox-dap: bridge {bridgePath}\n");
        await ready.Task;
    }

    private async Task PrepareSessionAsync(JObject args)
    {
        await StartBridgeAsync(GetStr(args, "consoleName"));
        var exe = GetStr(args, "program");
        var xbe = GetStr(args, "xbe");
        var map = GetStr(args, "map");
        var pdb = GetStr(args, "pdb");
        if (!string.IsNullOrEmpty(exe) || !string.IsNullOrEmpty(xbe))
            await LoadSymbolsAsync(exe, xbe, pdb, map);
    }

    private async Task ExecuteHardwareLaunchAsync(JObject args)
    {
        var xbePath = (GetStr(args, "xbePath") ?? "").Replace('/', '\\');
        var slash = xbePath.LastIndexOf('\\');
        var dir = GetStr(args, "xbeDir") ?? (slash >= 0 ? xbePath[..slash] : "xe:\\");
        if (!dir.EndsWith('\\')) dir += "\\";
        var title = GetStr(args, "xbeTitle") ?? (slash >= 0 ? xbePath[(slash + 1)..] : xbePath);
        var bpCount = CountUserBreakpoints();
        Console_($"xbox-dap: launch plan breakpoints={bpCount}\n");

        // Always stop at the title thread's creation (before any of the title's own code --
        // including InitHardware/D3D CreateDevice -- runs) and connect the debugger there. The
        // bridge's "clean start" (autoRun) path used to be chosen whenever no breakpoints were set
        // yet, skipping ConnectDebugger entirely -- on the theory that DmConnectDebugger(TRUE)
        // hangs CreateDevice. That hang is real but was about connecting LATE, after the title was
        // already running; connecting at this pre-CreateDevice halt is exactly what the
        // breakpoints-already-set path already did safely (and what RXDK-360's debug adapter always
        // does, regardless of breakpoint count). Skipping the connect just because no breakpoints
        // existed yet left the debugger permanently unconnected for the rest of the session, so any
        // breakpoint set LIVE afterward armed (BREAK ADDR= succeeds) but never actually triggered --
        // the INT3 has nowhere connected to report the stop to.
        var launch = await _bridge.RequestAsync("launch", Args(
            ("dir", dir), ("title", title), ("reboot", GetBool(args, "reboot")),
            ("timeout", 120000), ("console", GetStr(args, "consoleName")), ("autoRun", false)));
        var threadId = (int)launch.GetNumber("threadId");
        if (threadId > 0) _stoppedThreadId = threadId;
        _launchFinished = true;
        Console_($"xbox-dap: launch result threadId={threadId} moduleBase={launch.GetString("moduleBase") ?? "?"}\n");
        await PrintDiagAsync("after launch");
    }

    private async Task RunStepAndWaitAsync(int threadId, bool stepOver = false)
    {
        _stoppedThreadId = threadId;
        _stepInProgress = true;
        try
        {
            var result = await _bridge.RequestAsync("step", Args(("threadId", threadId), ("over", stepOver)));
            var tid = (int)result.GetNumber("threadId");
            if (tid > 0) _stoppedThreadId = tid;
            NotifyStopped("step", _stoppedThreadId);
        }
        catch (Exception e)
        {
            _ignoreBridgeStopUntil = NowMs() + 1000;
            Console_($"step failed: {e.Message}\n");
        }
        finally
        {
            _stepInProgress = false;
        }
    }

    private void OnBridgeEvent(BridgeMessage ev)
    {
        if (ev.Event is "break" or "singlestep")
        {
            if (_launchStartupInProgress || _startupGoInProgress || _stepInProgress || NowMs() < _ignoreBridgeStopUntil)
                return;
            var tid = (int)ev.GetNumber("threadId");
            if (tid > 0) _stoppedThreadId = tid;
            // This runs on the bridge's stdout-reader thread (BridgeClient OutputDataReceived). Sending
            // the StoppedEvent from here means VS's follow-up threads/stackTrace/variables requests are
            // serviced while we're still inside the reader callback, which can stall their bridge
            // responses so the stop never surfaces in the IDE (a Continue that "doesn't re-hit"). Marshal
            // the notify off the reader thread, mirroring RXDK-VSCode's setTimeout(0) deferral.
            var reason = ev.Event == "singlestep" ? "step" : "breakpoint";
            _ = Task.Run(() => NotifyStopped(reason, _stoppedThreadId));
        }
        else if (ev.Event == "debugstr")
        {
            var text = (ev.GetString("text") ?? "").Trim();
            if (text.Length > 0)
            {
                WriteTitleOutput(text.EndsWith('\n') ? text : text + "\n");
                Console_($"title: {text}\n");
            }
        }
        else if (ev.Event is "terminated" or "rip")
        {
            Protocol.SendEvent(new TerminatedEvent());
        }
    }

    // ---- breakpoint install ----

    private async Task<InstallResult> InstallBreakpointAsync(string sourcePath, int line, bool queue)
    {
        if (_bridge is null) return new InstallResult(false, "", "debugger not ready");
        try
        {
            var resolved = await _bridge.RequestAsync("resolveLine", Args(("file", sourcePath), ("line", line)));
            var addr = resolved.GetString("address") ?? "";
            var moduleBase = resolved.GetString("moduleBase") ?? "";
            if (string.IsNullOrEmpty(addr) || addr is "0x00000000" or "0x0")
                throw new InvalidOperationException($"no code at line {line} (try a nearby statement line)");
            var set = await _bridge.RequestAsync("setBreakpoint", Args(("file", sourcePath), ("line", line), ("queue", queue), ("address", addr)));
            if (set.GetBool("pending"))
                return new InstallResult(true, set.GetString("address") ?? addr, $"pending (module base {(string.IsNullOrEmpty(moduleBase) ? "unknown" : moduleBase)})");
            var armed = !(set.TryGet("armed", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.False);
            Console_($"breakpoint {Path.GetFileName(sourcePath)}:{line} -> {addr}{(armed ? "" : " (NOT ARMED on devkit)")}\n");
            return new InstallResult(true, addr, addr);
        }
        catch (Exception e)
        {
            Console_($"breakpoint failed {Path.GetFileName(sourcePath)}:{line}: {e.Message}\n");
            return new InstallResult(false, "", e.Message);
        }
    }

    // ---- launch sequencing (configurationDone gating) ----

    private void ArmConfigurationDoneFallback()
    {
        _configurationFallbackTimer?.Dispose();
        _configurationFallbackTimer = new Timer(_ =>
        {
            _configurationFallbackTimer?.Dispose();
            _configurationFallbackTimer = null;
            if (!_configurationDone && !_startupFinished && _pendingLaunchArgs is not null)
            {
                Console_("xbox-dap: configurationDone not received; continuing anyway\n");
                _configurationDone = true;
                ScheduleRunAfterConfigured();
            }
        }, null, 1000, System.Threading.Timeout.Infinite);
    }

    private void ScheduleRunAfterConfigured()
    {
        _runAfterConfiguredTimer?.Dispose();
        _runAfterConfiguredTimer = new Timer(_ =>
        {
            _runAfterConfiguredTimer?.Dispose();
            _runAfterConfiguredTimer = null;
            _ = RunAfterConfiguredAsync();
        }, null, 50, System.Threading.Timeout.Infinite);
    }

    private async Task RunAfterConfiguredAsync()
    {
        if (!_configurationDone || Volatile.Read(ref _breakpointSetupInFlight) > 0) return;
        if (!_launchFinished)
        {
            if (_pendingLaunchArgs is null) return;
            var args = _pendingLaunchArgs;
            _pendingLaunchArgs = null;
            try
            {
                await ExecuteHardwareLaunchAsync(args);
            }
            catch (Exception e)
            {
                _launchStartupInProgress = false;
                Console_($"xbox-dap: launch failed: {e.Message}\n");
                Protocol.SendEvent(new TerminatedEvent());
                return;
            }
        }
        if (!_launchFinished || _postLaunchHandled) return;
        _postLaunchHandled = true;
        _startupFinished = true;
        _launchStartupInProgress = false;
        _configurationFallbackTimer?.Dispose();
        _configurationFallbackTimer = null;

        var bpCount = CountUserBreakpoints();
        Console_($"xbox-dap: startup path breakpoints={bpCount}\n");

        await ApplyAllBreakpointsAsync(false);

        // Release the entry hold the same way whether or not the user set breakpoints. goUser
        // unconditionally resumes the held main thread; plain "go" only releases that startup
        // suspend when it was the entry hold, so a no-breakpoint launch left the title parked at
        // entry and never ran. goUser then continues to the first user breakpoint, or — with none —
        // leaves the title running (LeaveTitleRunning). The only real difference between the two
        // cases is whether breakpoints were armed above; the resume itself is identical.
        _startupGoInProgress = true;
        try
        {
            if (await ContinueToFirstBreakpointAsync("after launch")) return;
            Console_(HasUserBreakpoints()
                ? "xbox-dap: timed out waiting for breakpoint — title may have run past main.\n"
                : "xbox-dap: no breakpoints — title running.\n");
            await PrintDiagAsync("after continue");
        }
        catch (Exception e) { Console_($"continue failed: {e.Message}\n"); }
        finally { _startupGoInProgress = false; }
    }

    // ---- stop detection ----

    private int CountUserBreakpoints()
    {
        var n = _fileBreakpointAddrs.Values.Sum(m => m.Count);
        if (n == 0) n = _breakpointMap.Count;
        return n;
    }

    private bool HasUserBreakpoints() => CountUserBreakpoints() > 0;

    private static int ParseBridgeAddress(object? addr)
    {
        var text = (addr?.ToString() ?? "0").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var n) ? n : 0;
    }

    private bool AddressMatchesUserBreakpoint(object? addr)
    {
        var needle = ParseBridgeAddress(addr);
        if (needle == 0) return false;
        foreach (var lineMap in _fileBreakpointAddrs.Values)
            foreach (var bpAddr in lineMap.Values)
                if (ParseBridgeAddress(bpAddr) == needle) return true;
        foreach (var a in _breakpointMap.Values)
            if (ParseBridgeAddress(a) == needle) return true;
        return false;
    }

    private bool IsBridgeUserBreakpointStop(BridgeMessage ev, string addr) =>
        ev.GetBool("atUserBreakpoint") ? !string.IsNullOrEmpty(addr)
        : !string.IsNullOrEmpty(addr) && AddressMatchesUserBreakpoint(addr);

    private async Task<bool> ContinueToFirstBreakpointAsync(string label)
    {
        BridgeMessage run;
        try { run = await _bridge.RequestAsync("goUser"); }
        catch (Exception e) { Console_($"xbox-dap: goUser failed: {e.Message}\n"); return false; }

        var threadId = (int)run.GetNumber("threadId");
        if (threadId > 0) _stoppedThreadId = threadId;
        var addr = run.GetString("address") ?? "";
        if (!string.IsNullOrEmpty(addr) && IsBridgeUserBreakpointStop(run, addr))
        {
            Console_($"xbox-dap: stopped at {addr}.\n");
            NotifyStopped("breakpoint", _stoppedThreadId);
            await PrintDiagAsync($"{label} breakpoint");
            return true;
        }
        // Only spin waiting for a breakpoint when the user actually set one. With none, goUser has
        // left the title running (running:true) and there is nothing to wait for — return "no stop"
        // so the caller reports the title running rather than blocking for the full timeout.
        if ((run.GetBool("running") || !string.IsNullOrEmpty(addr)) && HasUserBreakpoints())
        {
            Console_("xbox-dap: title running — waiting for a breakpoint...\n");
            return await WaitForBreakpointLoopAsync();
        }
        return false;
    }

    private async Task<bool> WaitForBreakpointLoopAsync()
    {
        var deadline = NowMs() + 120_000;
        var lastSkip = "";
        var skipRepeats = 0;
        while (NowMs() < deadline && !_shuttingDown)
        {
            BridgeMessage wb;
            try { wb = await _bridge.RequestAsync("waitBreak", Args(("timeout", 2000))); }
            catch { continue; }
            var addr = wb.GetString("address") ?? "";
            if (string.IsNullOrEmpty(addr)) continue;
            if (IsBridgeUserBreakpointStop(wb, addr))
            {
                var threadId = (int)wb.GetNumber("threadId");
                if (threadId > 0) _stoppedThreadId = threadId;
                Console_($"xbox-dap: stopped at {addr}.\n");
                NotifyStopped("breakpoint", _stoppedThreadId);
                return true;
            }

            if (addr == lastSkip) skipRepeats++;
            else { lastSkip = addr; skipRepeats = 0; Console_($"xbox-dap: skipping stop at {addr} — continuing to your breakpoint...\n"); }
            if (skipRepeats >= 8)
            {
                Console_($"xbox-dap: stuck at {addr} after repeated continue attempts. Set a breakpoint in title code (e.g. the first line of main) and try again.\n");
                return false;
            }

            try
            {
                var run = await _bridge.RequestAsync("goUser");
                if (run.GetBool("running")) continue;
                var runAddr = run.GetString("address") ?? "";
                if (!string.IsNullOrEmpty(runAddr) && IsBridgeUserBreakpointStop(run, runAddr))
                {
                    var threadId = (int)run.GetNumber("threadId");
                    if (threadId > 0) _stoppedThreadId = threadId;
                    Console_($"xbox-dap: stopped at {runAddr}.\n");
                    NotifyStopped("breakpoint", _stoppedThreadId);
                    return true;
                }
            }
            catch (Exception e) { Console_($"xbox-dap: goUser failed: {e.Message}\n"); }
        }
        return false;
    }


    private async Task PrintDiagAsync(string label)
    {
        if (_bridge is null) return;
        try
        {
            var d = await _bridge.RequestAsync("diag");
            Console_($"xbox-dap diag [{label}]: {d.Raw}\n");
        }
        catch (Exception e) { Console_($"xbox-dap diag [{label}] failed: {e.Message}\n"); }
    }

    private async Task ApplyAllBreakpointsAsync(bool queue)
    {
        var id = 1;
        foreach (var (sourcePath, lineMap) in _fileBreakpointAddrs)
        {
            foreach (var line in lineMap.Keys.ToList())
            {
                var installed = await InstallBreakpointAsync(sourcePath, line, queue);
                var key = BpKey(sourcePath, line);
                if (!string.IsNullOrEmpty(installed.Address))
                {
                    _breakpointMap[key] = installed.Address;
                    lineMap[line] = installed.Address;
                }
                if (installed.Verified)
                {
                    var bp = new Breakpoint(verified: true) { Id = id++, Line = line };
                    Protocol.SendEvent(new BreakpointEvent(BreakpointEvent.ReasonValue.Changed, bp));
                }
            }
        }
    }

    // ---- path / symbol resolution ----

    private void NotifyStopped(string reason, int threadId)
    {
        var tid = threadId > 0 ? threadId : 1;
        _varChildren.Clear();
        _nextChildRef = 100;
        Console_($"xbox-dap: StoppedEvent reason={reason} thread={tid}\n");
        var reasonValue = reason switch
        {
            "step" => StoppedEvent.ReasonValue.Step,
            "pause" => StoppedEvent.ReasonValue.Pause,
            _ => StoppedEvent.ReasonValue.Breakpoint,
        };
        Protocol.SendEvent(new StoppedEvent(reasonValue) { ThreadId = tid, AllThreadsStopped = false });
    }

    private async Task LoadSymbolsAsync(string? exe, string? xbe, string? pdb, string? map)
    {
        var (kind, imagePath) = !string.IsNullOrEmpty(exe) ? ("exe", Path.GetFullPath(exe))
            : !string.IsNullOrEmpty(xbe) ? ("xbe", Path.GetFullPath(xbe))
            : throw new InvalidOperationException("No program or xbe provided for symbol loading.");
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"{(kind == "exe" ? "Program" : "XBE")} not found: {imagePath}");

        static string StripExt(string p) => System.Text.RegularExpressions.Regex.Replace(p, @"\.(exe|xbe)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var pdbPath = !string.IsNullOrEmpty(pdb) ? Path.GetFullPath(pdb) : $"{StripExt(imagePath)}.pdb";
        if (!File.Exists(pdbPath))
            throw new FileNotFoundException($"PDB not found: {pdbPath}. Rebuild with debug info (Debug or ReleaseSafe).");

        var reqArgs = new List<(string, object?)> { ("pdb", pdbPath) };
        reqArgs.Add(kind == "exe" ? ("exe", imagePath) : ("xbe", imagePath));
        var mapPath = !string.IsNullOrEmpty(map) ? Path.GetFullPath(map) : $"{StripExt(imagePath)}.map";
        if (File.Exists(mapPath)) reqArgs.Add(("map", mapPath));
        await _bridge.RequestAsync("loadSymbols", Args(reqArgs.ToArray()));
    }

    private string NormalizeSourcePath(string sourcePath)
    {
        var p = sourcePath;
        if (p.StartsWith("file:///")) p = p["file:///".Length..];
        else if (p.StartsWith("file://")) p = p["file://".Length..];
        try { p = Uri.UnescapeDataString(p); } catch { /* keep raw */ }
        if (p.Length >= 2 && p[1] == ':')
            return $"{char.ToUpperInvariant(p[0])}:{p[2..].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)}";
        return p.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private string ResolveWorkspacePath(string file)
    {
        var norm = NormalizeSourcePath(file);
        if (File.Exists(norm)) return norm;
        var baseName = Path.GetFileName(norm);
        foreach (var root in new[] { _workspaceRoot, _srcRoot }.Where(r => !string.IsNullOrEmpty(r)))
        {
            var underRoot = Path.Combine(root, baseName);
            if (File.Exists(underRoot)) return underRoot;
            var samplesRoot = Path.Combine(root, "samples");
            if (Directory.Exists(samplesRoot))
            {
                foreach (var name in Directory.EnumerateDirectories(samplesRoot))
                {
                    var candidate = Path.Combine(name, baseName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        var fromSrcRoot = FindInSrcRoot(norm, baseName);
        return !string.IsNullOrEmpty(fromSrcRoot) ? fromSrcRoot : norm;
    }

    private string FindInSrcRoot(string requested, string baseName)
    {
        if (string.IsNullOrEmpty(_srcRoot)) return "";
        var index = GetSrcRootIndex();
        if (!index.TryGetValue(baseName.ToLowerInvariant(), out var candidates) || candidates.Count == 0) return "";
        if (candidates.Count == 1) return candidates[0];
        var reqSegs = requested.ToLowerInvariant().Split('\\', '/').Where(s => s.Length > 0).Reverse().ToList();
        var best = candidates[0];
        var bestScore = -1;
        foreach (var candidate in candidates)
        {
            var candSegs = candidate.ToLowerInvariant().Split('\\', '/').Where(s => s.Length > 0).Reverse().ToList();
            var score = 0;
            while (score < reqSegs.Count && score < candSegs.Count && reqSegs[score] == candSegs[score]) score++;
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    private Dictionary<string, List<string>> GetSrcRootIndex()
    {
        if (_srcRootIndex is not null) return _srcRootIndex;
        var index = new Dictionary<string, List<string>>();
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", ".vs", ".vscode", "out", "bin", "obj", ".cache" };
        void Walk(string dir, int depth)
        {
            if (depth > 12) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); } catch { return; }
            foreach (var full in entries)
            {
                if (Directory.Exists(full))
                {
                    if (!skipDirs.Contains(Path.GetFileName(full))) Walk(full, depth + 1);
                }
                else
                {
                    var key = Path.GetFileName(full).ToLowerInvariant();
                    if (index.TryGetValue(key, out var list)) list.Add(full);
                    else index[key] = new List<string> { full };
                }
            }
        }
        Walk(_srcRoot, 0);
        _srcRootIndex = index;
        return index;
    }

    private void WriteTitleOutput(string text)
    {
        if (string.IsNullOrEmpty(_titleOutputFile)) return;
        try { File.AppendAllText(_titleOutputFile, text); } catch { /* ignore */ }
    }

    private static List<JObject> DedupeStackFrames(List<JObject> raw)
    {
        var seen = new HashSet<string>();
        var outList = new List<JObject>();
        foreach (var f in raw)
        {
            var name = f.Value<string>("name") ?? "???";
            var file = Path.GetFileName((f.Value<string>("file") ?? "").Replace('\\', '/'));
            var line = f.Value<int?>("line") ?? 0;
            var key = $"{name}\0{file}\0{line}";
            if (seen.Add(key)) outList.Add(f);
        }
        return outList;
    }

    // ---- small utilities ----

    private static string BpKey(string sourcePath, int line) => $"{sourcePath}|{line}";

    private static string ImageDir(JObject args)
    {
        if (GetStr(args, "program") is { Length: > 0 } prog) return Path.GetDirectoryName(Path.GetFullPath(prog)) ?? "";
        if (GetStr(args, "xbe") is { Length: > 0 } xbe) return Path.GetDirectoryName(Path.GetFullPath(xbe)) ?? "";
        return "";
    }

    private string ExpandVars(string s) =>
        s.Replace("${workspaceFolder}", _workspaceRoot).Replace("${extensionInstallPath}", _extensionPath);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

    private static IReadOnlyDictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs) if (v is not null) d[k] = v;
        return d;
    }

    private static List<JObject> ToObjects(BridgeMessage msg, string field)
    {
        var outList = new List<JObject>();
        if (msg.TryGet(field, out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
                outList.Add(JObject.Parse(el.GetRawText()));
        return outList;
    }

    // The VSCodeDebugProtocol library exposes custom launch/attach attributes as an
    // IDictionary<string, JToken>. `new JObject(dict)` throws ("could not determine JSON
    // object type for KeyValuePair"), so build the JObject by copying entries.
    private static JObject ConfigProps(IEnumerable<KeyValuePair<string, JToken>>? props)
    {
        var o = new JObject();
        if (props is not null)
            foreach (var kv in props)
                o[kv.Key] = kv.Value;
        return o;
    }

    private static string? GetStr(JObject o, string key) => o.TryGetValue(key, out var v) && v.Type != JTokenType.Null ? v.ToString() : null;
    private static double GetNum(JObject o, string key) => o.TryGetValue(key, out var v) && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? v.Value<double>() : 0;
    private static bool GetBool(JObject o, string key) => o.TryGetValue(key, out var v) && v.Type == JTokenType.Boolean && v.Value<bool>();
}
