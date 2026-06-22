using System.Diagnostics;
using System.Text.Json;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private const string BridgeCategory = "Bridge";

    public static IReadOnlyList<KitCheckResult> RunBridgeChecks(string console, XbdmKitSession session)
    {
        if (!AllowBridgeTests())
        {
            return
            [
                KitCheck.Skip(
                    BridgeCategory,
                    "Bridge suite",
                    "Set RXDK_KIT_ALLOW_BRIDGE=1 or RXDK_KIT_ALLOW_EXEC=1."),
            ];
        }

        var bridgeLaunch = ResolveBridgeLaunch();
        if (bridgeLaunch is null)
        {
            return
            [
                KitCheck.Skip(
                    BridgeCategory,
                    "Bridge suite",
                    "xboxdbg-bridge not found. Build Rxdk.XboxDbgBridge or set RXDK_BRIDGE_EXE."),
            ];
        }

        var results = new List<KitCheckResult>();

        string? execWireDir = null;
        if (AllowExecTests())
        {
            var localXbe = Path.Combine(XbdmKitSession.TestFilesDirectory(), "TriangleXDK.xbe");
            if (File.Exists(localXbe))
            {
                execWireDir = ExecWireDirectory(session.Managed);
                var wireXbe = $"{execWireDir}\\TriangleXDK.xbe";
                XbdmKitSession.EnsureTriangleXbeOnKit(session.Managed, localXbe, wireXbe);
            }
        }

        session.ReleaseKitConnections();
        KitTestProgress.Phase("Bridge: kit test session released (bridge owns kit connection)");

        try
        {
            using var client = BridgeTestClient.Start(bridgeLaunch, console);
            results.Add(SafeBridgeCheck("ping", () => client.Ping()));

            var launched = false;
            if (execWireDir is not null)
            {
                KitTestProgress.Phase("Bridge: launching TriangleXDK");
                var launchResult = SafeBridgeCheck(
                    "launch",
                    () => client.Launch(execWireDir, "TriangleXDK.xbe", autoRun: true));
                results.Add(launchResult);
                launched = launchResult.Status == KitCheckStatus.Passed;
                if (launched)
                {
                    // autoRun already issued GO, so the title is spinning. Let it run for one visible
                    // segment before the breakpoint freezes it.
                    XbdmKitWait.DwellWithProgress("Bridge: TriangleXDK running", XbdmKitWait.VisibleSegment);
                }
            }

            if (launched || !AllowExecTests())
            {
                results.Add(SafeBridgeCheck("attach", () => client.Attach()));
                results.Add(SafeBridgeCheck("diag", () => client.Diag()));
            }
            else
            {
                results.Add(KitCheck.Skip(
                    BridgeCategory,
                    "attach",
                    "TriangleXDK launch failed; debugger attach requires a running title."));
                results.Add(KitCheck.Skip(
                    BridgeCategory,
                    "diag",
                    "TriangleXDK launch failed; debugger attach requires a running title."));
            }

            // Symbols must load before any file/line breakpoint can resolve.
            var testFiles = XbdmKitSession.TestFilesDirectory();
            var exePath = Path.Combine(testFiles, "TriangleXDK.exe");
            var pdbPath = Path.Combine(testFiles, "TriangleXDK.pdb");
            var symbolsLoaded = false;
            if (AllowExecTests() && File.Exists(exePath) && File.Exists(pdbPath))
            {
                var loadResult = SafeBridgeCheck("loadsymbols", () => client.LoadSymbols(exePath, pdbPath));
                results.Add(loadResult);
                symbolsLoaded = loadResult.Status == KitCheckStatus.Passed;
                if (launched)
                    results.Add(SafeBridgeCheck("resolveline", () => client.ResolveLine("TriangleXDK.cpp", 206)));
                else
                {
                    results.Add(KitCheck.Skip(
                        BridgeCategory,
                        "resolveline",
                        "TriangleXDK launch failed; line resolution requires a running title."));
                }
            }
            else
            {
                results.Add(KitCheck.Skip(
                    BridgeCategory,
                    "LoadSymbols+ResolveLine",
                    "Set RXDK_KIT_ALLOW_EXEC=1 with TriangleXDK.exe and TriangleXDK.pdb in TestFiles."));
            }

            // Breakpoint freeze/resume: after the title has spun for one segment, drop a breakpoint
            // on a per-frame line (Render) so the spinning triangle visibly freezes, hold the frozen
            // state for a segment, then remove the breakpoint and resume so it spins again. Fully
            // timed (no input needed) and exercises setbreakpoint → waitbreak → removebreakpoint → go.
            if (launched && symbolsLoaded)
            {
                var bpResult = SafeBridgeCheck("setbreakpoint", () => client.SetBreakpoint("TriangleXDK.cpp", 173));
                results.Add(bpResult);
                if (bpResult.Status == KitCheckStatus.Passed)
                {
                    results.Add(SafeBridgeCheck("waitbreak", () => client.WaitBreak(TimeSpan.FromSeconds(30))));
                    // The title is now halted at Render() — the triangle is frozen mid-spin. Hold it
                    // for one segment so the freeze is visible, then remove the breakpoint and resume.
                    XbdmKitWait.DwellWithProgress("Bridge: TriangleXDK frozen at breakpoint", XbdmKitWait.VisibleSegment);
                    results.Add(SafeBridgeCheck("removebreakpoint", () => client.RemoveBreakpoint()));
                    results.Add(SafeBridgeCheck("go", () => client.Go()));
                    // Resumed: spin again for a segment so the resume is visible.
                    XbdmKitWait.DwellWithProgress("Bridge: TriangleXDK resumed", XbdmKitWait.VisibleSegment);
                }
            }
            else
            {
                var reason = launched
                    ? "Symbols not loaded; breakpoint requires file/line resolution."
                    : "TriangleXDK launch failed; breakpoint requires a running title.";
                results.Add(KitCheck.Skip(BridgeCategory, "setbreakpoint", reason));
                results.Add(KitCheck.Skip(BridgeCategory, "waitbreak", reason));
                results.Add(KitCheck.Skip(BridgeCategory, "removebreakpoint", reason));
                results.Add(KitCheck.Skip(BridgeCategory, "go", reason));
            }
        }
        catch (Exception ex)
        {
            results.Add(KitCheck.Fail(BridgeCategory, "Bridge suite", ex.Message));
        }
        finally
        {
            XbdmKitSession.WaitForKit(console, "Waiting for dashboard after bridge");
            session.ReconnectKit();
        }

        return results;
    }

    private static KitCheckResult SafeBridgeCheck(string name, Func<KitCheckResult> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            var inner = ex is AggregateException aggregate
                ? aggregate.Flatten().InnerException ?? aggregate
                : ex;
            var site = inner.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
            var message = $"{inner.GetType().Name}: {inner.Message}" +
                (string.IsNullOrEmpty(site) ? string.Empty : $" @ {site}");
            return KitCheck.Fail(BridgeCategory, name, message);
        }
    }

    private sealed record BridgeLaunch(string FileName, string Arguments);

    private static BridgeLaunch? ResolveBridgeLaunch()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RXDK_BRIDGE_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return new BridgeLaunch(Path.GetFullPath(fromEnv), string.Empty);

        var outputDir = AppContext.BaseDirectory;
        var dllInOutput = Path.Combine(outputDir, "xboxdbg-bridge.dll");
        if (File.Exists(dllInOutput) &&
            File.Exists(Path.Combine(outputDir, "xboxdbg-bridge.runtimeconfig.json")))
        {
            return new BridgeLaunch("dotnet", $"exec \"{dllInOutput}\"");
        }

        var exeCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "xboxdbg-bridge.exe"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Rxdk.XboxDbgBridge.Cli", "bin", "Debug", "net8.0", "xboxdbg-bridge.exe")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Rxdk.XboxDbgBridge.Cli", "bin", "Release", "net8.0", "xboxdbg-bridge.exe")),
        };

        var exe = exeCandidates.FirstOrDefault(File.Exists);
        if (exe is not null)
            return new BridgeLaunch(exe, string.Empty);

        var dllCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "xboxdbg-bridge.dll"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Rxdk.XboxDbgBridge.Cli", "bin", "Debug", "net8.0", "xboxdbg-bridge.dll")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Rxdk.XboxDbgBridge.Cli", "bin", "Release", "net8.0", "xboxdbg-bridge.dll")),
        };

        var dll = dllCandidates.FirstOrDefault(File.Exists);
        if (dll is not null)
            return new BridgeLaunch("dotnet", $"exec \"{dll}\"");

        return null;
    }

    private sealed class BridgeTestClient : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly StreamReader _stdout;
        private readonly System.Collections.Concurrent.BlockingCollection<string> _stdoutLines = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _stderr = new();
        private int _nextId = 1;

        private BridgeTestClient(Process process, StreamWriter stdin, StreamReader stdout)
        {
            _process = process;
            _stdin = stdin;
            _stdout = stdout;
        }

        private void CaptureStderr(string line)
        {
            _stderr.Enqueue(line);
            while (_stderr.Count > 80 && _stderr.TryDequeue(out _))
            {
            }
        }

        private string StderrTail()
        {
            var lines = _stderr.ToArray();
            if (lines.Length == 0)
                return string.Empty;
            var tail = lines.Length > 12 ? lines[^12..] : lines;
            return " | bridge-stderr: " + string.Join(" ⏎ ", tail);
        }

        internal static BridgeTestClient Start(BridgeLaunch launch, string console)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launch.FileName,
                Arguments = launch.Arguments,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.Environment["RXDK_XBOX"] = console;
            var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")
                ?? Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD");
            if (!string.IsNullOrWhiteSpace(password))
                startInfo.Environment["RXDK_XBDM_PASSWORD"] = password;

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start xboxdbg-bridge.");

            var stdin = process.StandardInput;
            var stdout = process.StandardOutput;
            var client = new BridgeTestClient(process, stdin, stdout);

            // One dedicated reader per stream. StreamReader is NOT thread-safe, so the bridge's
            // stdout must be drained by a single long-lived thread; spawning a fresh ReadLine task
            // per poll slice (as before) let a slow response leave overlapping ReadLine calls on the
            // same reader, corrupting it into an ArgumentOutOfRangeException.
            _ = Task.Run(() =>
            {
                try
                {
                    while (stdout.ReadLine() is { } line)
                        client._stdoutLines.Add(line);
                }
                catch
                {
                }
                finally
                {
                    client._stdoutLines.CompleteAdding();
                }
            });

            _ = Task.Run(() =>
            {
                try
                {
                    while (process.StandardError.ReadLine() is { } line)
                        client.CaptureStderr(line);
                }
                catch
                {
                }
            });

            client.WaitForEvent("ready", TimeSpan.FromSeconds(30));
            return client;
        }

        internal KitCheckResult Ping() =>
            Send("ping", null, BridgeCategory, "ping", r =>
                r.TryGetProperty("pong", out var pong) && pong.ValueKind == JsonValueKind.True);

        internal KitCheckResult Attach() =>
            Send("attach", null, BridgeCategory, "attach", _ => true);

        internal KitCheckResult Launch(string dir, string title, bool autoRun = false)
        {
            var launchTimeout = XbdmKitWait.LaunchTimeout + TimeSpan.FromSeconds(30);
            var fields = new Dictionary<string, object>
            {
                ["dir"] = dir,
                ["title"] = title,
                ["timeout"] = (int)Math.Min(XbdmKitWait.LaunchTimeout.TotalMilliseconds, int.MaxValue),
            };
            if (autoRun)
            {
                fields["autoRun"] = true;
                fields["reboot"] = false;
            }

            return Send(
                "launch",
                fields,
                BridgeCategory,
                "launch",
                r => r.TryGetProperty("threadId", out _) ||
                     r.TryGetProperty("moduleBase", out _) ||
                     r.TryGetProperty("running", out _),
                launchTimeout,
                "Bridge: waiting for TriangleXDK");
        }

        internal KitCheckResult Go() =>
            Send("go", null, BridgeCategory, "go", _ => true);

        internal KitCheckResult SetBreakpoint(string file, int line) =>
            Send(
                "setbreakpoint",
                new Dictionary<string, object> { ["file"] = file, ["line"] = line },
                BridgeCategory,
                "setbreakpoint",
                r => r.TryGetProperty("address", out _));

        internal KitCheckResult WaitBreak(TimeSpan timeout) =>
            Send(
                "waitbreak",
                new Dictionary<string, object>
                {
                    ["timeout"] = (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue),
                },
                BridgeCategory,
                "waitbreak",
                r => r.TryGetProperty("address", out _),
                timeout + TimeSpan.FromSeconds(10),
                "Bridge: waiting for breakpoint");

        internal KitCheckResult RemoveBreakpoint() =>
            Send("clearbreakpoints", null, BridgeCategory, "removebreakpoint", _ => true);

        internal KitCheckResult Diag() =>
            Send("diag", null, BridgeCategory, "diag", r => r.TryGetProperty("threads", out _));

        internal KitCheckResult LoadSymbols(string exePath, string pdbPath) =>
            Send(
                "loadsymbols",
                new Dictionary<string, object> { ["exe"] = exePath, ["pdb"] = pdbPath },
                BridgeCategory,
                "loadsymbols",
                _ => true);

        internal KitCheckResult ResolveLine(string file, int line) =>
            Send(
                "resolveline",
                new Dictionary<string, object> { ["file"] = file, ["line"] = line },
                BridgeCategory,
                "resolveline",
                r => r.TryGetProperty("address", out _));

        private KitCheckResult Send(
            string cmd,
            Dictionary<string, object>? fields,
            string category,
            string name,
            Func<JsonElement, bool> validate,
            TimeSpan? timeout = null,
            string? progressLabel = null)
        {
            var id = _nextId++;
            using var document = BuildCommand(id, cmd, fields);
            _stdin.WriteLine(document.RootElement.GetRawText());
            _stdin.Flush();

            var waitTimeout = timeout ?? TimeSpan.FromSeconds(45);
            var result = ReadResult(id, waitTimeout, progressLabel);
            if (!result.Success)
            {
                return KitCheck.Fail(
                    category,
                    name,
                    (result.Error ?? "bridge returned success=false") + StderrTail(),
                    $"{cmd} {result.Raw}");
            }

            if (!validate(result.Payload))
            {
                return KitCheck.Fail(
                    category,
                    name,
                    "Unexpected bridge payload." + StderrTail(),
                    $"{cmd} {result.Raw}");
            }

            return KitCheck.Pass(category, name, cmd);
        }

        private static JsonDocument BuildCommand(int id, string cmd, Dictionary<string, object>? fields)
        {
            var map = new Dictionary<string, object> { ["id"] = id, ["cmd"] = cmd };
            if (fields is not null)
            {
                foreach (var pair in fields)
                    map[pair.Key] = pair.Value;
            }

            return JsonDocument.Parse(JsonSerializer.Serialize(map));
        }

        private void WaitForEvent(string eventName, TimeSpan timeout)
        {
            if (!TryReadLineUntil(timeout, line =>
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    return root.TryGetProperty("type", out var type) &&
                           type.GetString() == "event" &&
                           root.TryGetProperty("event", out var evt) &&
                           evt.GetString() == eventName;
                }))
            {
                throw new TimeoutException($"Bridge did not emit '{eventName}' within {timeout}.");
            }
        }

        private (bool Success, JsonElement Payload, string? Error, string Raw) ReadResult(
            int id,
            TimeSpan timeout,
            string? progressLabel = null)
        {
            string? matchedLine = null;
            if (!TryReadLineUntil(timeout, line =>
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var type) || type.GetString() != "result")
                        return false;
                    if (!root.TryGetProperty("id", out var idProperty) ||
                        !idProperty.TryGetInt32(out var resultId) ||
                        resultId != id)
                        return false;
                    matchedLine = line;
                    return true;
                },
                progressLabel))
            {
                return (false, default, "timeout waiting for bridge result", string.Empty);
            }

            using var resultDoc = JsonDocument.Parse(matchedLine!);
            var root = resultDoc.RootElement;
            var success = root.TryGetProperty("success", out var successProperty) &&
                          successProperty.ValueKind == JsonValueKind.True;
            string? error = null;
            if (root.TryGetProperty("error", out var errorProperty))
                error = errorProperty.GetString();

            return (success, root.Clone(), error, matchedLine!);
        }

        private bool TryReadLineUntil(TimeSpan timeout, Func<string, bool> predicate, string? progressLabel = null)
        {
            var deadline = DateTime.UtcNow + timeout;
            var nextLog = DateTime.UtcNow;
            while (DateTime.UtcNow < deadline)
            {
                if (progressLabel is not null && DateTime.UtcNow >= nextLog)
                {
                    var remaining = Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalSeconds);
                    KitTestProgress.Phase($"{progressLabel}… {remaining}s remaining");
                    nextLog = DateTime.UtcNow.AddSeconds(5);
                }

                var remainingSlice = deadline - DateTime.UtcNow;
                if (remainingSlice <= TimeSpan.Zero)
                    break;

                var slice = remainingSlice > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remainingSlice;
                if (!TryReadLine(out var line, slice))
                    continue;

                if (line is null)
                {
                    if (_process.HasExited)
                        return false;
                    continue;
                }

                try
                {
                    if (predicate(line))
                        return true;
                }
                catch (JsonException)
                {
                }
            }

            return false;
        }

        private bool TryReadLine(out string? line, TimeSpan timeout)
        {
            line = null;
            var waitMs = (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue);
            if (_stdoutLines.TryTake(out var taken, waitMs))
            {
                line = taken;
                return true;
            }

            // No more lines will arrive (reader thread saw EOF) — surface a null line so the
            // caller can notice the process exited; otherwise it's just a poll-slice timeout.
            if (_stdoutLines.IsCompleted)
                return true;

            return false;
        }

        public void Dispose()
        {
            try
            {
                ShutdownToDashboard();
            }
            catch
            {
                // Best effort.
            }

            if (!_process.HasExited && !_process.WaitForExit(5000))
                _process.Kill(entireProcessTree: true);

            _stdin.Dispose();
            _stdout.Dispose();
            _process.Dispose();
        }

        private void ShutdownToDashboard()
        {
            var id = _nextId++;
            _stdin.WriteLine($"{{\"id\":{id},\"cmd\":\"shutdown\",\"rebootDashboard\":true}}");
            _stdin.Flush();
            _ = ReadResult(id, TimeSpan.FromSeconds(30));
        }
    }
}
