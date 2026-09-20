using Rxdk.Engine.Bootstrap;
using Rxdk.Engine.Build;
using Rxdk.Engine.Deploy;
using Rxdk.Engine.Export;
using Rxdk.Engine.Import;
using Rxdk.Engine.Model;
using Rxdk.Engine.Platform;

// Thin CLI over Rxdk.Engine — the pure-.NET replacement for RXDK-VSCode's cli.ts.
// Grows subcommands (build/deploy/run/reboot) as the engine is ported. For now it
// carries `info`, which parses an rxdk.project.json and prints the resolved model —
// a smoke test that the manifest port matches the on-disk contract.

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: rxdk <command> [options]");
    Console.Error.WriteLine("commands:");
    Console.Error.WriteLine("  info --project-root <dir>   Parse rxdk.project.json and print the resolved model");
    Console.Error.WriteLine("  install-tools [--tools-tag <t>] [--xdvdfs-tag <t>]   Download host tools into the staged root");
    Console.Error.WriteLine("  tools-status                Report whether host tools are installed");
    Console.Error.WriteLine("  install-sdk                 Clone/update RXDK-SDK (headers + libs)");
    Console.Error.WriteLine("  sdk-status                  Report staged SDK presence");
    Console.Error.WriteLine("  install-docs                Clone/update RXDK-Docs (SDK + extension help)");
    Console.Error.WriteLine("  docs-status                 Report staged docs presence");
    Console.Error.WriteLine("  install-samples             Clone/update RXDK-Samples (ported XDK sample suite)");
    Console.Error.WriteLine("  samples-status              Report staged samples presence");
    Console.Error.WriteLine("  update-sdk|update-docs|update-tools|update-samples   Update a staged component in place");
    Console.Error.WriteLine("  versions                    Print current/available version per component (SDK/Docs/Tools/Samples)");
    Console.Error.WriteLine("  build --project-root <dir> [--configuration <name>] [--compile-only]   Compile+link to .xbe");
    Console.Error.WriteLine("  deploy --project-root <dir> [--configuration <name>] [--console <ip>]     Copy build output to the devkit");
    Console.Error.WriteLine("  run --project-root <dir> [--console <ip>] [--reboot] [--go]   Launch the deployed title (--go = run without halting for a debugger)");
    Console.Error.WriteLine("  launch-xemu --project-root <dir> [--xemu-path <exe>] [--xemu-params <args>]   Build + boot the ISO in xemu");
    Console.Error.WriteLine("  reboot [--console <ip>]     Warm-reboot the devkit");
    Console.Error.WriteLine("  remove-dxt --project-root <dir> [--name <n>] [--console <ip>]   Delete the DXT from xe:\\dxt");
    Console.Error.WriteLine("  set-ip --address <ip>       Set the devkit IP/hostname (registry)");
    Console.Error.WriteLine("  xbox-ip                     Print the resolved devkit address");
    Console.Error.WriteLine("  import-vcproj --in <file.vcproj> [--out <dir>] [--scaffold <dir>] [--copy-sources]   Import a VS2003 XDK project");
    Console.Error.WriteLine("  import-sln --in <file.sln> [--out <dir>] [--scaffold <dir>] [--copy-sources]   Import a VS2003 XDK solution (multi-project)");
    Console.Error.WriteLine("  generate-vcxproj --project-root <dir>   Generate a .vcxproj + .sln from rxdk.project.json (open a VS Code project in Visual Studio)");
    return 2;
}

var command = args[0];
var opts = ParseArgs(args.Skip(1));

switch (command)
{
    case "info":
        return CmdInfo(opts);
    case "install-tools":
    case "update-tools":
        return await CmdInstallTools(opts);
    case "tools-status":
        return CmdToolsStatus();
    case "install-sdk":
    case "update-sdk":
        return await CmdInstallSdk(opts);
    case "sdk-status":
        return CmdSdkStatus();
    case "install-llvm":
    case "update-llvm":
        return await CmdInstallLlvm(opts);
    case "llvm-status":
        return CmdLlvmStatus();
    case "build-sdk-lib":
        return await CmdBuildSdkLib(opts);
    case "build-sdk":
        return await CmdBuildSdk(opts);
    case "install-docs":
    case "update-docs":
        return await CmdInstallDocs(opts);
    case "docs-status":
        return CmdDocsStatus();
    case "install-samples":
    case "update-samples":
        return await CmdInstallSamples(opts);
    case "samples-status":
        return CmdSamplesStatus();
    case "versions":
        return await CmdVersions(opts);
    case "build":
        return await CmdBuild(opts);
    case "deploy":
        return await CmdDeploy(opts);
    case "run":
        return await CmdRun(opts);
    case "launch-xemu":
        return await CmdLaunchXemu(opts);
    case "reboot":
        return await CmdReboot(opts);
    case "remove-dxt":
        return await CmdRemoveDxt(opts);
    case "set-ip":
        return await CmdSetIp(opts);
    case "xbox-ip":
        return await CmdXboxIp();
    case "import-vcproj":
        return CmdImportVcproj(opts);
    case "import-sln":
        return CmdImportSln(opts);
    case "generate-vcxproj":
        return CmdGenerateVcxproj(opts);
    default:
        Console.Error.WriteLine($"unknown command: {command}");
        return 2;
}

static int CmdImportVcproj(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("in", out var input) || string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine("missing required --in <file.vcproj>");
        return 2;
    }
    opts.TryGetValue("out", out var outDir);
    opts.TryGetValue("scaffold", out var scaffold);
    var copySources = opts.ContainsKey("copy-sources");
    try
    {
        // Import() already writes a single-project .sln via VcxprojExporter.Export (the multi-project
        // import-sln path additionally writes its own umbrella .sln alongside each project's own).
        var r = Vcproj2003Importer.Import(input, outDir ?? "",
            string.IsNullOrEmpty(scaffold) ? null : scaffold, copySources, log: msg => Console.WriteLine(msg));
        Console.WriteLine($"OK: imported {r.ProjectName} ({r.ConfigurationCount} config(s), {r.SourceCount} source(s)) -> {r.VcxprojPath}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"import-vcproj failed: {ex.Message}");
        return 1;
    }
}

static int CmdImportSln(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("in", out var input) || string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine("missing required --in <file.sln>");
        return 2;
    }
    opts.TryGetValue("out", out var outDir);
    opts.TryGetValue("scaffold", out var scaffold);
    var copySources = opts.ContainsKey("copy-sources");
    try
    {
        var r = SolutionImporter.ImportSolution(input, outDir ?? "",
            string.IsNullOrEmpty(scaffold) ? null : scaffold, copySources, msg => Console.WriteLine(msg));
        Console.WriteLine($"OK: imported {r.Projects.Count} project(s) -> {r.SlnPath}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"import-sln failed: {ex.Message}");
        return 1;
    }
}

static int CmdGenerateVcxproj(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("project-root", out var root) || string.IsNullOrEmpty(root))
    {
        Console.Error.WriteLine("missing required --project-root <dir>");
        return 2;
    }
    try
    {
        var r = VcxprojExporter.Export(root);
        Console.WriteLine($"OK: generated {r.ProjectName} -> {r.VcxprojPath}");
        foreach (var w in r.Warnings) Console.WriteLine($"Warning: {w}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"generate-vcxproj failed: {ex.Message}");
        return 1;
    }
}

// Extension-compatibility gate: if --max-version (the loaded extension's version) is given and the
// component's live version is newer, refuse the install/update and tell the user to update the
// extension first, so a component can never run ahead of the extension that drives it. Exit code 3
// distinguishes "gated" from a real failure (1).
static async Task<int> GateComponentAsync(string componentName, Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("max-version", out var maxVersion) || string.IsNullOrWhiteSpace(maxVersion))
        return 0;
    var all = await ComponentVersions.GetAllAsync(maxVersion);
    var match = all.FirstOrDefault(c => string.Equals(c.Name, componentName, StringComparison.OrdinalIgnoreCase));
    if (match?.Blocked == true)
    {
        Console.Error.WriteLine(
            $"{componentName} {match.Available} needs a newer RXDK extension (you have {maxVersion}). " +
            $"Update the RXDK extension first, then update {componentName}.");
        return 3;
    }
    return 0;
}

static async Task<int> CmdInstallTools(Dictionary<string, string> opts)
{
    var gated = await GateComponentAsync("Tools", opts);
    if (gated != 0) return gated;
    opts.TryGetValue("tools-tag", out var toolsTag);
    opts.TryGetValue("xdvdfs-tag", out var xdvdfsTag);
    try
    {
        var root = await HostToolsInstaller.InstallAsync(
            hostToolsTag: string.IsNullOrEmpty(toolsTag) ? null : toolsTag,
            xdvdfsTag: string.IsNullOrEmpty(xdvdfsTag) ? null : xdvdfsTag,
            log: msg => Console.WriteLine(msg));
        Console.WriteLine($"Host tools installed at: {root}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"install-tools failed: {ex.Message}");
        return 1;
    }
}

static int CmdToolsStatus()
{
    var root = RxdkPaths.GetStagedToolsRoot();
    Console.WriteLine($"staged tools root: {root}");
    var installed = HostToolsInstaller.IsInstalled();
    foreach (var tool in HostToolsInstaller.RequiredHostTools)
    {
        var path = System.IO.Path.Combine(root, RxdkPaths.HostToolExecutableName(tool));
        Console.WriteLine($"  [{(System.IO.File.Exists(path) ? "x" : " ")}] {tool}");
    }
    Console.WriteLine($"installed: {installed}");
    return installed ? 0 : 1;
}

static async Task<int> CmdInstallSdk(Dictionary<string, string> opts)
{
    var gated = await GateComponentAsync("SDK", opts);
    if (gated != 0) return gated;
    try
    {
        var root = await SdkStaging.EnsureAsync(log: msg => Console.WriteLine(msg));
        Console.WriteLine($"SDK staged at: {root}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"install-sdk failed: {ex.Message}");
        return 1;
    }
}

static int CmdSdkStatus()
{
    Console.WriteLine($"staged SDK root: {RxdkPaths.GetStagedSdkRoot()}");
    var headers = SdkStaging.IsStagedSdkPresent();
    var libs = SdkStaging.IsStagedSdkLibPresent();
    Console.WriteLine($"  headers (include/d3d8.h): {headers}");
    Console.WriteLine($"  libs (linkable marker):   {libs}");
    return headers && libs ? 0 : 1;
}

static async Task<int> CmdInstallDocs(Dictionary<string, string> opts)
{
    var gated = await GateComponentAsync("Docs", opts);
    if (gated != 0) return gated;
    try
    {
        var root = await DocsStaging.EnsureAsync(log: msg => Console.WriteLine(msg));
        Console.WriteLine($"Docs staged at: {root}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"install-docs failed: {ex.Message}");
        return 1;
    }
}

static int CmdDocsStatus()
{
    Console.WriteLine($"staged docs root: {RxdkPaths.GetStagedDocsRoot()}");
    var present = DocsStaging.IsStagedDocsPresent();
    Console.WriteLine($"  docs (xboxsdk/rxdk-vs toc.json): {present}");
    return present ? 0 : 1;
}

static async Task<int> CmdInstallSamples(Dictionary<string, string> opts)
{
    var gated = await GateComponentAsync("Samples", opts);
    if (gated != 0) return gated;
    try
    {
        var root = await SamplesStaging.EnsureAsync(log: msg => Console.WriteLine(msg));
        Console.WriteLine($"Samples staged at: {root}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"install-samples failed: {ex.Message}");
        return 1;
    }
}

static int CmdSamplesStatus()
{
    Console.WriteLine($"staged samples root: {RxdkPaths.GetStagedSamplesRoot()}");
    var present = SamplesStaging.IsStagedSamplesPresent();
    Console.WriteLine($"  samples (RxdkSamples/): {present}");
    return present ? 0 : 1;
}

static async Task<int> CmdVersions(Dictionary<string, string> opts)
{
    // Machine-parseable: one "name<TAB>current<TAB>available<TAB>blocked" line per component.
    // Missing = "-"; blocked = "1" when the live version needs a newer extension than --max-version.
    opts.TryGetValue("max-version", out var maxVersion);
    var components = await ComponentVersions.GetAllAsync(maxVersion);
    foreach (var c in components)
        Console.WriteLine($"{c.Name}\t{c.Current ?? "-"}\t{c.Available ?? "-"}\t{(c.Blocked ? "1" : "0")}");
    return 0;
}


static async Task<int> CmdInstallLlvm(Dictionary<string, string> opts)
{
    // --tag pins a release; default is the "latest" rolling toolchain release.
    opts.TryGetValue("tag", out var tag);
    try
    {
        var root = await LlvmInstaller.InstallAsync(
            tag: string.IsNullOrEmpty(tag) ? null : tag, log: msg => Console.WriteLine(msg));
        Console.WriteLine($"LLVM toolchain ready: {root}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"install-llvm failed: {ex.Message}");
        return 1;
    }
}

static async Task<int> CmdBuildSdkLib(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("repo-root", out var repo) || string.IsNullOrEmpty(repo))
    { Console.Error.WriteLine("missing required --repo-root"); return 2; }
    if (!opts.TryGetValue("manifest", out var manifest) || string.IsNullOrEmpty(manifest))
    { Console.Error.WriteLine("missing required --manifest"); return 2; }
    opts.TryGetValue("config", out var cfg);
    var optimize = string.Equals(cfg, "Release", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(cfg, "ReleaseSmall", StringComparison.OrdinalIgnoreCase)
        ? RxdkOptimizeMode.ReleaseSmall : RxdkOptimizeMode.Debug;
    try
    {
        var m = await SdkLibBuild.LoadManifestAsync(manifest);
        var lib = await SdkLibBuild.BuildLibAsync(repo, m, optimize, log: msg => Console.WriteLine(msg));
        Console.WriteLine($"OK: {m.Name} -> {lib}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"build-sdk-lib failed: {ex.Message}");
        return 1;
    }
}

static async Task<int> CmdBuildSdk(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("repo-root", out var repo) || string.IsNullOrEmpty(repo))
    { Console.Error.WriteLine("missing required --repo-root"); return 2; }
    opts.TryGetValue("manifest", out var manifest);
    if (string.IsNullOrEmpty(manifest)) manifest = System.IO.Path.Combine(repo, "build", "sdk", "sdk.json");
    opts.TryGetValue("config", out var cfg);
    var optimize = string.Equals(cfg, "Release", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(cfg, "ReleaseSmall", StringComparison.OrdinalIgnoreCase)
        ? RxdkOptimizeMode.ReleaseSmall : RxdkOptimizeMode.Debug;
    var stageHeaders = !opts.ContainsKey("no-headers");
    try
    {
        await SdkBuild.BuildAsync(repo, manifest, optimize, stageHeaders, log: msg => Console.WriteLine(msg));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"build-sdk failed: {ex.Message}");
        return 1;
    }
}

static int CmdLlvmStatus()
{
    string? root;
    try { root = LlvmRuntime.ResolveRoot(); }
    catch (Exception ex) { Console.Error.WriteLine($"llvm: {ex.Message}"); return 1; }
    if (root is null)
    {
        Console.WriteLine("llvm: not found (run install-llvm, or set RXDK_LLVM)");
        return 1;
    }
    Console.WriteLine($"llvm: {root}");
    Console.WriteLine($"clang: {LlvmRuntime.ClangExe(root)}");
    Console.WriteLine($"ar: {LlvmRuntime.ArExe(root)}");
    var installed = LlvmInstaller.GetInstalledVersion();
    if (!string.IsNullOrEmpty(installed)) Console.WriteLine($"release: {installed}");
    var builtins = LlvmRuntime.BuiltinsArchive(root);
    Console.WriteLine(builtins is not null
        ? $"builtins: {builtins}"
        : "builtins: MISSING (title link needs libclang_rt.builtins-i386; see build-rt-builtins.ps1)");
    return 0;
}

static async Task<int> CmdBuild(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("project-root", out var root) || string.IsNullOrEmpty(root))
    {
        Console.Error.WriteLine("missing required --project-root");
        return 2;
    }
    // The build reads the project's committed rxdk.project.json and derives everything (optimize
    // level, SDK lib variant) from the selected configuration's debug/release flag -- so the only
    // knobs are --project-root and the optional --configuration (multi-config select).
    opts.TryGetValue("configuration", out var configName);
    var result = await XboxBuild.BuildAsync(new BuildOptions
    {
        ProjectRoot = root,
        CompileOnly = opts.ContainsKey("compile-only"),
        Configuration = string.IsNullOrEmpty(configName) ? null : configName,
        Log = msg => Console.WriteLine(msg),
    });
    if (!result.Ok)
    {
        Console.Error.WriteLine($"build failed: {result.Error}");
        return 1;
    }
    Console.WriteLine($"build OK -> {result.OutDir}");
    return 0;
}

static async Task<int> CmdDeploy(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("project-root", out var root) || string.IsNullOrEmpty(root))
    {
        Console.Error.WriteLine("missing required --project-root");
        return 2;
    }
    opts.TryGetValue("console", out var console);
    opts.TryGetValue("configuration", out var deployConfig);
    // Explicit deploy inputs used by RXDK-VS20XX (which is .vcxproj-driven and ignores
    // rxdk.project.json entirely): the built output dir, the console dir, the project kind, the
    // Xbox Deployment page's Force Copy / Deploy Files. Absent for a VS Code / CLI-direct deploy,
    // which reads them from rxdk.project.json instead.
    opts.TryGetValue("local-dir", out var localDir);
    opts.TryGetValue("remote-dir", out var remoteDir);
    opts.TryGetValue("name", out var deployProjName);
    opts.TryGetValue("deploy-paths", out var deployPathsRaw);
    var deployPaths = string.IsNullOrEmpty(deployPathsRaw)
        ? null
        : deployPathsRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    bool? forceCopy = opts.TryGetValue("force-copy", out var fc) ? !string.Equals(fc, "false", StringComparison.OrdinalIgnoreCase) : null;
    var result = await XboxDeploy.DeployProjectAsync(new XboxDeploy.DeployOptions
    {
        ProjectRoot = root,
        ProjectName = string.IsNullOrEmpty(deployProjName) ? null : deployProjName,
        LocalDir = string.IsNullOrEmpty(localDir) ? null : localDir,
        RemoteDir = string.IsNullOrEmpty(remoteDir) ? null : remoteDir,
        ConsoleName = string.IsNullOrEmpty(console) ? null : console,
        Configuration = string.IsNullOrEmpty(deployConfig) ? null : deployConfig,
        IsDxt = opts.ContainsKey("dxt"),
        ForceCopy = forceCopy,
        DeployPaths = deployPaths,
        IgnoreManifest = opts.ContainsKey("no-manifest"),
        Log = msg => Console.WriteLine(msg),
    });
    if (!result.Ok)
    {
        Console.Error.WriteLine($"deploy failed: {result.Error}");
        return 1;
    }
    return 0;
}

static async Task<int> CmdRemoveDxt(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("project-root", out var root) || string.IsNullOrEmpty(root))
    {
        Console.Error.WriteLine("missing required --project-root");
        return 2;
    }
    opts.TryGetValue("console", out var console);
    opts.TryGetValue("name", out var name);
    var result = await XboxDeploy.RemoveDxtAsync(
        root,
        projectName: string.IsNullOrEmpty(name) ? null : name,
        consoleName: string.IsNullOrEmpty(console) ? null : console,
        log: msg => Console.WriteLine(msg));
    if (!result.Ok)
    {
        Console.Error.WriteLine($"remove-dxt failed: {result.Error}");
        return 1;
    }
    return 0;
}

static async Task<int> CmdRun(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("project-root", out var root) || string.IsNullOrEmpty(root))
    {
        Console.Error.WriteLine("missing required --project-root");
        return 2;
    }
    var manifest = RxdkManifestLoader.TryLoad(root);
    if (manifest is null)
    {
        Console.Error.WriteLine($"no valid rxdk.project.json for {root}");
        return 1;
    }
    opts.TryGetValue("console", out var console);
    var result = await XboxLaunch.LaunchProjectAsync(new XboxLaunch.LaunchOptions
    {
        ProjectName = manifest.Name,
        ConsoleName = string.IsNullOrEmpty(console) ? null : console,
        Reboot = opts.ContainsKey("reboot"),
        // --go / --no-debug: launch-and-run without halting at the initial break for a
        // debugger. For a plain test run (no debugger attaching), this is what you want.
        Go = opts.ContainsKey("go") || opts.ContainsKey("no-debug"),
        Log = msg => Console.WriteLine(msg),
    });
    if (result.NoConsoleConfigured)
    {
        Console.Error.WriteLine("no Xbox console configured (set-ip, or Xbox Neighborhood)");
        return 2;
    }
    if (!result.Ok)
    {
        Console.Error.WriteLine($"run failed: {result.Error}");
        return 1;
    }
    return 0;
}

static async Task<int> CmdLaunchXemu(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("project-root", out var root) || string.IsNullOrEmpty(root))
    {
        Console.Error.WriteLine("missing required --project-root");
        return 2;
    }
    opts.TryGetValue("configuration", out var xemuConfig);
    var manifest = RxdkManifestLoader.TryLoad(root)?.ResolveConfiguration(
        string.IsNullOrEmpty(xemuConfig) ? null : xemuConfig);
    if (manifest is null)
    {
        Console.Error.WriteLine($"no valid rxdk.project.json for {root}");
        return 1;
    }

    // Build a fresh ISO first, then boot it in xemu (no debugging).
    var build = await XboxBuild.BuildAsync(new BuildOptions
    {
        ProjectRoot = root,
        Configuration = string.IsNullOrEmpty(xemuConfig) ? null : xemuConfig,
        Log = msg => Console.WriteLine(msg),
    });
    if (!build.Ok)
    {
        Console.Error.WriteLine("build failed");
        return 1;
    }

    opts.TryGetValue("xemu-path", out var xemuPath);
    opts.TryGetValue("xemu-params", out var xemuParams);
    var result = await XemuLaunch.LaunchXemuAsync(new XemuLaunch.LaunchOptions
    {
        ProjectRoot = root,
        Manifest = manifest,
        XemuPath = xemuPath ?? "",
        XemuParams = xemuParams,
        Log = msg => Console.WriteLine(msg),
    });
    if (!result.Ok)
    {
        Console.Error.WriteLine(result.Error ?? "xemu launch failed");
        return 1;
    }
    return 0;
}

static async Task<int> CmdReboot(Dictionary<string, string> opts)
{
    opts.TryGetValue("console", out var console);
    var result = await XboxLaunch.RebootConsoleAsync(
        string.IsNullOrEmpty(console) ? null : console, msg => Console.WriteLine(msg));
    if (result.NoConsoleConfigured) { Console.Error.WriteLine("no Xbox console configured"); return 2; }
    if (!result.Ok) { Console.Error.WriteLine($"reboot failed: {result.Error}"); return 1; }
    return 0;
}

static async Task<int> CmdSetIp(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("address", out var addr) || string.IsNullOrEmpty(addr))
    {
        Console.Error.WriteLine("missing required --address");
        return 2;
    }
    try
    {
        await ConsoleResolver.SetActiveXboxAddressAsync(addr);
        Console.WriteLine($"Xbox address set to {addr}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"set-ip failed: {ex.Message}");
        return 1;
    }
}

static async Task<int> CmdXboxIp()
{
    var addr = await ConsoleResolver.GetActiveXboxAddressAsync();
    Console.WriteLine(addr is null ? "no Xbox console configured" : addr);
    return addr is null ? 1 : 0;
}


static int CmdInfo(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("project-root", out var root) || string.IsNullOrEmpty(root))
    {
        Console.Error.WriteLine("missing required --project-root");
        return 2;
    }

    var manifest = RxdkManifestLoader.TryLoad(root);
    if (manifest is null)
    {
        Console.Error.WriteLine($"no valid {RxdkManifestLoader.ManifestFileName} under {root}");
        return 1;
    }

    Console.WriteLine($"name:           {manifest.Name}");
    Console.WriteLine($"type:           {manifest.EffectiveType}");
    Console.WriteLine($"configuration:  {manifest.EffectiveConfiguration}");
    Console.WriteLine($"sources:        {manifest.Sources?.Count ?? 0}");
    Console.WriteLine($"libraries:      {string.Join(", ", manifest.Libraries ?? new())}");
    Console.WriteLine($"projectRefs:    {string.Join(", ", manifest.ProjectReferences ?? new())}");
    Console.WriteLine($"usesCpp:        {manifest.UsesCpp}");
    Console.WriteLine($"needsIntelliSense: {manifest.NeedsIntelliSense}");
    Console.WriteLine($"isLibrary:      {manifest.IsLibrary}");
    Console.WriteLine($"isDxt:          {manifest.IsDxt}");
    return 0;
}

static Dictionary<string, string> ParseArgs(IEnumerable<string> argv)
{
    var result = new Dictionary<string, string>();
    var list = argv.ToList();
    for (var i = 0; i < list.Count; i++)
    {
        if (!list[i].StartsWith("--")) continue;
        var key = list[i][2..];
        if (i + 1 < list.Count && !list[i + 1].StartsWith("--"))
        {
            result[key] = list[i + 1];
            i++;
        }
        else
        {
            result[key] = "true";
        }
    }
    return result;
}
