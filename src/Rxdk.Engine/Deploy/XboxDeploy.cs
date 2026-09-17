using Rxdk.Engine.Build;
using Rxdk.Engine.Model;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Deploy;

public sealed record DeployResult(bool Ok, IReadOnlyList<string> Deployed, string? Error = null)
{
    public static DeployResult Fail(string error) => new(false, Array.Empty<string>(), error);
}

/// <summary>
/// Copies build output to the devkit via xbcp, and removes DXTs via xbdel. C# port of
/// RXDK-VSCode xboxDeploy.ts. Uses '-' switches (not '/') so the tools don't misparse args.
/// </summary>
public static class XboxDeploy
{
    public sealed class DeployOptions
    {
        public required string ProjectRoot { get; init; }
        public string? ProjectName { get; init; }
        public string? LocalDir { get; init; }
        public string? RemoteDir { get; init; }
        public string? ConsoleName { get; init; }
        /// <summary>Filename patterns for the project's own output. Default: *.xbe, *.pdb, *.map.</summary>
        public IReadOnlyList<string>? Files { get; init; }
        /// <summary>
        /// Configuration to select from a multi-config rxdk.project.json (e.g. "Debug"/"Release") --
        /// this picks the per-config outputDir the build wrote to. Ignored for a flat manifest.
        /// </summary>
        public string? Configuration { get; init; }
        /// <summary>
        /// Deploy as a DXT (to xe:\dxt) when no explicit RemoteDir is given. For a manifest-less
        /// (pure-.vcxproj) DXT project the caller sets this from ConfigurationType=DebuggerExtension;
        /// a manifest's Type=Dxt still implies it on its own.
        /// </summary>
        public bool IsDxt { get; init; }
        /// <summary>Overrides the manifest's forceCopy (Xbox Deployment page). Null = use the manifest / default (incremental).</summary>
        public bool? ForceCopy { get; init; }
        /// <summary>Overrides the manifest's deployPaths (Xbox Deployment page "Deploy Files"). Null = use the manifest.</summary>
        public IReadOnlyList<string>? DeployPaths { get; init; }
        /// <summary>
        /// Never read rxdk.project.json; drive deploy entirely from these options. RXDK-VS20XX sets
        /// this: a .vcxproj is the single source of truth there, so even a sample folder that happens
        /// to carry an rxdk.project.json (for the VS Code side) must be ignored. VS Code / CLI-direct
        /// leave it false and the manifest is loaded as before.
        /// </summary>
        public bool IgnoreManifest { get; init; }
        public bool Quiet { get; init; }
        public Action<string>? Log { get; init; }
    }

    public static async Task<DeployResult> DeployProjectAsync(DeployOptions opts, CancellationToken ct = default)
    {
        try
        {
            var projectRoot = Path.GetFullPath(opts.ProjectRoot);
            // rxdk.project.json drives deploy for a VS Code / CLI-direct project (its per-config
            // outputDir/forceCopy/deployPaths). RXDK-VS20XX is purely .vcxproj-driven and sets
            // IgnoreManifest, passing everything explicitly (LocalDir, RemoteDir, IsDxt, ForceCopy,
            // DeployPaths from the Xbox Deployment properties) -- so a sample folder that carries an
            // rxdk.project.json for the VS Code side is never consulted from Visual Studio. The load
            // is also tolerated as absent (a fresh .vcxproj project simply has no manifest).
            RxdkProjectManifest? manifest = null;
            if (!opts.IgnoreManifest)
            {
                try { manifest = RxdkManifestLoader.Load(projectRoot).ResolveConfiguration(opts.Configuration); }
                catch (FileNotFoundException) { /* no manifest -> driven entirely by opts + defaults */ }
            }

            var projectName = opts.ProjectName ?? manifest?.Name;
            if (string.IsNullOrWhiteSpace(projectName))
                projectName = Path.GetFileName(projectRoot.TrimEnd('\\', '/'));

            if (opts.LocalDir == null && manifest == null)
                return DeployResult.Fail("No rxdk.project.json and no output directory given; cannot locate the build output to deploy.");
            var localDir = Path.GetFullPath(opts.LocalDir ?? SdkLayout.GetProjectOutDir(projectRoot, manifest!));
            if (!Directory.Exists(localDir))
                return DeployResult.Fail($"Deploy source directory not found: {localDir}");

            // A DXT deploys to xe:\dxt (xbdm scans E:\dxt\*.DXT non-recursively), not xe:\<name>. An
            // explicit RemoteDir (Xbox Deployment > Remote Path) always wins; otherwise the console
            // dir follows the project kind's convention.
            var isDxt = opts.IsDxt || manifest?.Type == RxdkProjectKind.Dxt;
            var remoteDir = !string.IsNullOrWhiteSpace(opts.RemoteDir)
                ? NormalizeRemoteDir(opts.RemoteDir!, projectName!)
                : (isDxt ? @"xe:\dxt" : NormalizeRemoteDir("", projectName!));
            var xbcp = RxdkPaths.ResolveHostTool("xbcp");
            var displayAddr = string.IsNullOrWhiteSpace(opts.ConsoleName)
                ? await ConsoleResolver.GetActiveXboxAddressAsync(ct)
                : opts.ConsoleName.Trim();
            var consoleSwitch = await ConsoleResolver.ResolveConsoleSwitchAsync(opts.ConsoleName, ct);
            opts.Log?.Invoke(displayAddr is not null
                ? $"Deploying to Xbox '{displayAddr}' -> {remoteDir}"
                : $"Deploying to default Xbox -> {remoteDir}");

            // ForceCopy (default false): incremental deploy sends only files newer than the console
            // copy (xbcp -d); the .xbe/.pdb are freshly built so they always go, but unchanged media
            // is skipped. ForceCopy = true drops -d so everything is re-sent.
            var incremental = (opts.ForceCopy ?? manifest?.ForceCopy) != true;
            if (incremental && !opts.Quiet) opts.Log?.Invoke("Incremental deploy (only new/changed files; set Force Copy to override).");

            var defaultPatterns = isDxt ? new[] { "*.dxt" } : new[] { "*.xbe", "*.pdb", "*.map" };
            var patterns = opts.Files is { Count: > 0 } ? opts.Files : defaultPatterns;
            var sent = new List<string>();
            foreach (var pattern in patterns)
            {
                foreach (var name in ListFilesMatching(localDir, pattern))
                {
                    var dest = $@"{remoteDir}\{name}";
                    await XbcpCopyAsync(xbcp, Path.Combine(localDir, name), dest, consoleSwitch, incremental, opts.Log, ct);
                    sent.Add(name);
                }
            }
            if (sent.Count == 0)
                return DeployResult.Fail($"No files matched in {localDir} (patterns: {string.Join(", ", patterns)})");

            // deployPaths: project-relative files/dirs copied next to the output on the console.
            // Copied per-file with an explicit destination (xbcp's own recursive copy misbehaves
            // on a plain local folder source — see xboxDeploy.ts).
            var deployFiles = PackXiso.ResolveDeployPaths(projectRoot, opts.DeployPaths ?? manifest?.DeployPaths, opts.Log);
            foreach (var entry in deployFiles)
            {
                var dest = $@"{remoteDir}\{entry.RelativeDest.Replace('/', '\\')}";
                await XbcpCopyAsync(xbcp, entry.Source, dest, consoleSwitch, incremental, opts.Log, ct);
            }

            var summary = $"Deployed: {string.Join(", ", sent)} -> {remoteDir}";
            if (deployFiles.Count > 0) summary += $"; deployPaths: {deployFiles.Count} file(s)";
            opts.Log?.Invoke(summary);
            return new DeployResult(true, sent);
        }
        catch (Exception err)
        {
            return DeployResult.Fail(err.Message);
        }
    }

    /// <summary>Delete a DXT from the console's E:\dxt via xbdel (pair with a warm reboot).</summary>
    public static async Task<DeployResult> RemoveDxtAsync(
        string projectRoot, string? projectName = null, string? consoleName = null,
        Action<string>? log = null, CancellationToken ct = default)
    {
        try
        {
            var name = projectName;
            if (string.IsNullOrEmpty(name))
                name = RxdkManifestLoader.Load(Path.GetFullPath(projectRoot)).Name;
            var xbdel = RxdkPaths.ResolveHostTool("xbdel");
            if (!File.Exists(xbdel))
                return DeployResult.Fail($"xbdel not found at {xbdel}. Update the RXDK host tools.");
            var displayAddr = string.IsNullOrWhiteSpace(consoleName)
                ? await ConsoleResolver.GetActiveXboxAddressAsync(ct)
                : consoleName.Trim();
            var consoleSwitch = await ConsoleResolver.ResolveConsoleSwitchAsync(consoleName, ct);
            var remote = $@"xe:\dxt\{name}.dxt";
            log?.Invoke(displayAddr is not null
                ? $"Removing {remote} from '{displayAddr}'"
                : $"Removing {remote} from default Xbox");

            var args = new List<string> { "-f", remote };
            if (consoleSwitch is not null) { args.Add("-x"); args.Add(consoleSwitch); }
            var r = await ProcessRunner.RunStreamedAsync(xbdel, args, log, ct: ct);
            if (!r.Success)
                return DeployResult.Fail($"xbdel failed (exit {r.ExitCode}) — was {remote} present?");
            return new DeployResult(true, new[] { $"{name}.dxt (removed)" });
        }
        catch (Exception err)
        {
            return DeployResult.Fail(err.Message);
        }
    }

    // ---- helpers ----

    private static async Task XbcpCopyAsync(
        string xbcp, string localFile, string remoteDest, string? console, bool incremental, Action<string>? log, CancellationToken ct)
    {
        // -y overwrite, -t create dest dir; -d = copy only if the source is newer than the console
        // copy (skips up-to-date files) — omitted for a forced full copy. NOT -q: xbcp prints one
        // concise "copy/skip <dest>" line per file, which is the deploy's per-file log now (the
        // engine no longer pre-logs each file). Suppress the "$ xbcp ..." command echo too.
        var args = new List<string> { "-y", "-t" };
        if (incremental) args.Add("-d");
        if (console is not null) { args.Add("-x"); args.Add(console); }
        args.Add(localFile);
        args.Add(remoteDest);
        var r = await ProcessRunner.RunStreamedAsync(xbcp, args, log, ct: ct, echoCommand: false);
        if (!r.Success)
            throw new InvalidOperationException($"xbcp failed copying {Path.GetFileName(localFile)} (exit {r.ExitCode})");
    }

    private static string NormalizeRemoteDir(string remoteDir, string defaultName)
    {
        var dir = string.IsNullOrEmpty(remoteDir) ? $@"xe:\{defaultName}" : remoteDir;
        if (System.Text.RegularExpressions.Regex.IsMatch(dir, @"^x[edc]:\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return dir.TrimEnd('\\');
        return $@"xe:\{dir}".TrimEnd('\\');
    }

    /// <summary>Only `*.ext`-shaped patterns are used by callers — not a full glob engine.</summary>
    private static IEnumerable<string> ListFilesMatching(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            var matches = pattern.StartsWith("*.", StringComparison.Ordinal)
                ? name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
            if (matches) yield return name;
        }
    }
}
