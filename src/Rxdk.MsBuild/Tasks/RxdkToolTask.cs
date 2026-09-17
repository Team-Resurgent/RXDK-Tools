using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Version-stable base for every RXDK MSBuild task. Derives from
    /// <see cref="Microsoft.Build.Utilities.Task"/> (whose assembly is stable across Visual
    /// Studio releases) rather than the per-VS Microsoft.Build.CPPTasks.Common
    /// TrackedVCToolTask, so ONE net472 Rxdk.MsBuild.dll loads under VS2022 (v170) and
    /// VS "18"/v180 alike. Tasks build their command lines explicitly and run the tool via
    /// <see cref="Run"/>; clang/lld/zig diagnostics are surfaced to the VS Error List via
    /// <see cref="LogDiagnostics"/>. Mirrors RXDK-360's Rxdk.Xbox360.Modern.Build.ModernTool.
    /// </summary>
    public abstract class RxdkToolTask : Task
    {
        /// <summary>Result of running a child process.</summary>
        protected sealed class ProcResult
        {
            public int ExitCode;
            public string StdOut = "";
            public string StdErr = "";
            public string Combined => StdOut + StdErr;
        }

        // Backslash-doubler for zig/clang response files. Reproduced verbatim from
        // Microsoft.Build.CPPTasks.VCToolTask.FindBackSlashInPath (the field the previous
        // TrackedVCToolTask-based tasks called). clang/zig treat '\' as an escape inside a
        // response file, so every path separator must be doubled: Replace(text, "\\\\").
        protected static readonly Regex FindBackSlashInPath = new Regex(
            "(?<=[^\\\\])\\\\(?=[^\\\\\\\"\\s])|(\\\\(?=[^\\\"]))|((?<=[^\\\\][\\\\])\\\\(?=[\\\"]))",
            RegexOptions.Compiled);

        /// <summary>
        /// RXDK install root. RXDK is only an OVERRIDE. The normal case is a bare VSIX install
        /// whose "Complete Setup" staged the SDK + host tools at the default data root with no
        /// env var set -- so fall back to it exactly like Rxdk.Engine's RxdkPaths.RxdkDataRoot
        /// (%ProgramData%\RXDK on Windows), rather than hard-failing on an unset env var.
        /// </summary>
        public string GetRXDKRoot()
        {
            var root = Environment.GetEnvironmentVariable("RXDK");
            if (!string.IsNullOrEmpty(root))
                return root.Trim();

            // A custom install path chosen in the standalone installer, recorded in the registry.
            // Plain-VSIX installs never write it, so they fall through to ProgramData as before.
            var reg = RegistryInstallPath();
            if (!string.IsNullOrEmpty(reg))
                return reg;

            var programData = Environment.GetEnvironmentVariable("ProgramData");
            if (string.IsNullOrEmpty(programData))
                programData = @"C:\ProgramData";
            var fallback = Path.Combine(programData, "RXDK");
            if (Directory.Exists(fallback))
                return fallback;

            Log.LogError("RXDK is not installed. Open the RXDK tool window and run Complete Setup (or set the RXDK environment variable).");
            return null;
        }

        /// <summary>
        /// The RXDK install path the standalone installer recorded, or null. Reads
        /// HKLM\SOFTWARE\TeamResurgent\RXDK\InstallPath. The installer writes both registry views;
        /// 32-bit readers (incl. MSBuild's $(Registry:)) resolve through WOW6432Node, so try the
        /// 32-bit view first, then the 64-bit view. (This task assembly only ever runs on Windows.)
        /// </summary>
        private static string RegistryInstallPath()
        {
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry32, Microsoft.Win32.RegistryView.Registry64 })
            {
                try
                {
                    using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
                    using (var key = baseKey.OpenSubKey(@"SOFTWARE\TeamResurgent\RXDK"))
                    {
                        if (key?.GetValue("InstallPath") is string p && !string.IsNullOrWhiteSpace(p) && Directory.Exists(p.Trim()))
                            return p.Trim();
                    }
                }
                catch { /* registry unavailable / access denied -> fall through */ }
            }
            return null;
        }

        /// <summary>Resolve a host tool ({rxdk}\tools\{name}), or the name itself if rooted.</summary>
        protected string GetToolExe(string toolName)
        {
            if (Path.IsPathRooted(toolName))
                return toolName;
            var rxdk = GetRXDKRoot();
            if (rxdk == null)
                return null;
            return $"{rxdk}\\tools\\{toolName}";
        }

        /// <summary>Quote an argument for a Windows command line only where needed.</summary>
        protected static string Quote(string a)
        {
            if (string.IsNullOrEmpty(a)) return a;
            if (a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// Run <paramref name="exe"/>, capturing stdout/stderr. When
        /// <paramref name="useResponseFile"/> is set, the switch args in
        /// <paramref name="args"/> are written to a temp ASCII .rsp file (with every backslash
        /// DOUBLED, per <see cref="FindBackSlashInPath"/>) and the tool is invoked as
        /// "<paramref name="leadingArgs"/> @rspfile" -- the zig sub-tool token ("cc"/"ar") is
        /// passed via <paramref name="leadingArgs"/> so it stays on the real command line (a
        /// response file is only processed once the sub-tool is named). Otherwise
        /// <paramref name="args"/> are quoted onto the command line directly (host tools).
        /// </summary>
        protected ProcResult Run(string exe, IEnumerable<string> args, string workingDir = null,
                                 bool useResponseFile = false, IEnumerable<string> leadingArgs = null,
                                 bool doubleBackslashes = true)
        {
            string cmdLine;
            string rspPath = null;
            try
            {
                if (useResponseFile)
                {
                    // The switch args are pre-formatted response-file chunks (already quoted
                    // where needed); join and (for clang/lld) double every backslash so they do
                    // not treat path separators as escapes. llvm-ar (zig ar) does not escape
                    // backslashes, so its caller opts out -- matching the previous tasks, where
                    // only ZigCompile/ZigLd overrode the response-file writer to double them.
                    var rsp = string.Join(" ", args);
                    if (doubleBackslashes)
                        rsp = FindBackSlashInPath.Replace(rsp, "\\\\");
                    rspPath = Path.GetTempFileName();
                    File.WriteAllText(rspPath, rsp, new ASCIIEncoding());

                    var lead = new StringBuilder();
                    if (leadingArgs != null)
                        foreach (var l in leadingArgs)
                        {
                            if (lead.Length > 0) lead.Append(' ');
                            lead.Append(l);
                        }
                    cmdLine = (lead.Length > 0 ? lead + " " : "") + "@" + Quote(rspPath);
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var a in args)
                    {
                        if (string.IsNullOrEmpty(a)) continue;
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(a);
                    }
                    cmdLine = sb.ToString();
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = cmdLine,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                Log.LogMessage(MessageImportance.Low, "  " + exe + " " + cmdLine);
                if (useResponseFile)
                    Log.LogMessage(MessageImportance.Low, "  (response file) " + string.Join(" ", args));

                var outBuf = new StringBuilder();
                var errBuf = new StringBuilder();
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) outBuf.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) errBuf.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    return new ProcResult { ExitCode = p.ExitCode, StdOut = outBuf.ToString(), StdErr = errBuf.ToString() };
                }
            }
            finally
            {
                if (rspPath != null)
                    try { File.Delete(rspPath); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// Echo a tool's output to the MSBuild log, promoting lines that match one of the
        /// supplied diagnostic regexes (with a non-empty CATEGORY group) to Error/Warning so
        /// they surface in the Visual Studio Error List. The regexes are the exact ones the
        /// previous tasks used (ZigCompile.clangMessageRegex / ZigLd.ldMessageRegex).
        /// </summary>
        protected void LogDiagnostics(string text, IEnumerable<Regex> regexes)
        {
            if (string.IsNullOrEmpty(text)) return;
            var rx = regexes as IList<Regex> ?? new List<Regex>(regexes ?? Array.Empty<Regex>());
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;

                bool handled = false;
                foreach (var r in rx)
                {
                    Match m = r.Match(line);
                    if (!m.Success) continue;

                    string cat = m.Groups["CATEGORY"].Success ? m.Groups["CATEGORY"].Value.Trim().ToLowerInvariant() : "";
                    if (cat.Length == 0)
                        break; // matched but no diagnostic category: treat as plain output

                    string file = m.Groups["FILENAME"].Success ? m.Groups["FILENAME"].Value.Trim() : "";
                    if (file.Length == 0) file = null;
                    int lineNo = m.Groups["LINE"].Success && int.TryParse(m.Groups["LINE"].Value, out var ln) ? ln : 0;
                    int colNo = m.Groups["COLUMN"].Success && int.TryParse(m.Groups["COLUMN"].Value, out var cn) ? cn : 0;
                    string msg = m.Groups["TEXT"].Success ? m.Groups["TEXT"].Value.Trim() : line;
                    if (msg.Length == 0) msg = line;

                    if (cat == "error" || cat == "fatal error")
                        Log.LogError(null, null, null, file, lineNo, colNo, 0, 0, msg);
                    else if (cat == "warning")
                        Log.LogWarning(null, null, null, file, lineNo, colNo, 0, 0, msg);
                    else
                        Log.LogMessage(MessageImportance.Normal, line);
                    handled = true;
                    break;
                }

                if (!handled)
                    Log.LogMessage(MessageImportance.Normal, line);
            }
        }

        // ---- shared response-file / command-line argument builders -----------------------
        // These produce pre-formatted, pre-quoted chunks. The zig tasks join them into a
        // response file (Run doubles the backslashes); order of Add* calls == command-line order.

        protected static void Flag(List<string> a, bool cond, string flag)
        {
            if (cond && !string.IsNullOrEmpty(flag)) a.Add(flag);
        }

        /// <summary>A reverse-switch bool that only emits when it was explicitly set.</summary>
        protected static void FlagRev(List<string> a, bool set, bool value, string on, string off)
        {
            if (!set) return;
            var s = value ? on : off;
            if (!string.IsNullOrEmpty(s)) a.Add(s);
        }

        /// <summary>Emit "&lt;sw&gt;&lt;value&gt;" (value quoted if needed) when value is non-empty.</summary>
        protected static void Opt(List<string> a, string sw, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            a.Add(sw + Quote(value));
        }

        /// <summary>Emit one "&lt;sw&gt;&lt;entry&gt;" per non-empty array entry.</summary>
        protected static void OptList(List<string> a, string sw, IEnumerable<string> values)
        {
            if (values == null) return;
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    a.Add(sw + Quote(v.Trim()));
        }

        /// <summary>Emit the flag an enum value maps to (nothing if unmapped or empty).</summary>
        protected static void Mapped(List<string> a, IDictionary<string, string> map, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (map.TryGetValue(key, out var f) && !string.IsNullOrEmpty(f))
                a.Add(f);
        }

        /// <summary>Append raw pass-through options (e.g. AdditionalOptions), never quoted.</summary>
        protected static void Raw(List<string> a, string raw)
        {
            if (!string.IsNullOrWhiteSpace(raw)) a.Add(raw.Trim());
        }
    }
}
