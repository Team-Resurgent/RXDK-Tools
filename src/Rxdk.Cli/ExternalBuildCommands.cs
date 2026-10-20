using System.Text.Json;
using Rxdk.Engine.Build;
using Rxdk.Engine.Model;
using Rxdk.Engine.Platform;

/// <summary>
/// The commands the CMake toolchain (cmake/RxdkToolchain.cmake) builds an Xbox title with, so a
/// CMake project compiles and links exactly as <c>rxdk build</c> does.
/// </summary>
static class ExternalBuildCommands
{
    public static void PrintUsage()
    {
        Console.Error.WriteLine("  toolchain-info              Print the compiler, archiver, SDK and tool paths (KEY=VALUE) for build systems");
        Console.Error.WriteLine("  compile-args --lang c|c++ [--cpp-std <std>] [--no-exceptions]   Print the compile flags, one per line");
        Console.Error.WriteLine("  compile-args --optimize <mode>   Print only an optimize mode's flags (Debug, ReleaseSafe, ReleaseFast, ReleaseSmall)");
        Console.Error.WriteLine("  compile-launch -- <compiler> <args...>   Compile, retrying a thread_local TU with line tables only (as rxdk build)");
        Console.Error.WriteLine("  link-exe -o <exe> [--debug] [--dxt] [--lib-dir <dir>]... <objects, libraries, @rsp>...   Link a title");
        Console.Error.WriteLine("  make-xbe --exe <exe> [--image-build <json>] [--title-name <n>] [--title-id <id>] [--stack <bytes>]   Build the .xbe next to the .exe");
        Console.Error.WriteLine("  make-iso --xbe <xbe> --name <n> --out-dir <dir> [--project-root <dir>] [--deploy <path[=dest]>]...   Pack an XISO");
    }

    static string Fwd(string path) => path.Replace('\\', '/');

    public static async Task<int> ToolchainInfo()
    {
        try
        {
            var tc = await Toolchain.ResolveAsync();
            Console.WriteLine($"RXDK_CC={Fwd(tc.CompilerExe)}");
            Console.WriteLine($"RXDK_AR={Fwd(tc.ArchiverExe)}");
            Console.WriteLine($"RXDK_TARGET={tc.TargetTriple}");
            Console.WriteLine($"RXDK_SDK_INCLUDE={Fwd(SdkLayout.GetSdkIncludeDir())}");
            Console.WriteLine($"RXDK_SDK_LIB={Fwd(SdkLayout.GetSdkLibDir())}");
            Console.WriteLine($"RXDK_IMAGEBLD={Fwd(RxdkPaths.ResolveHostTool("imagebld"))}");
            Console.WriteLine($"RXDK_XDVDFS={Fwd(RxdkPaths.ResolveHostTool("xdvdfs"))}");
            Console.WriteLine($"RXDK_CLI={Fwd(Environment.ProcessPath ?? "")}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"toolchain-info failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> CompileArgs(Dictionary<string, string> opts)
    {
        if (opts.TryGetValue("optimize", out var modeName))
        {
            if (!OptimizeMode.TryParse(modeName, out var mode))
            {
                Console.Error.WriteLine($"compile-args: unknown --optimize {modeName} (Debug, ReleaseSafe, ReleaseFast, ReleaseSmall)");
                return 2;
            }
            foreach (var f in XboxRecipe.OptimizeFlags(mode)) Console.WriteLine(f);
            return 0;
        }

        if (!opts.TryGetValue("lang", out var lang) || lang is not ("c" or "c++"))
        {
            Console.Error.WriteLine("compile-args: --lang c|c++ is required");
            return 2;
        }
        try
        {
            var tc = await Toolchain.ResolveAsync();
            var cppStd = opts.TryGetValue("cpp-std", out var std) && !string.IsNullOrWhiteSpace(std) ? std : "c++23";
            var flags = XboxRecipe.CompileFlags(tc, lang == "c++", cppStd, exceptions: !opts.ContainsKey("no-exceptions"));
            foreach (var f in flags) Console.WriteLine(f);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"compile-args failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> CompileLaunch(string[] argv)
    {
        var dash = Array.IndexOf(argv, "--");
        var command = dash >= 0 ? argv[(dash + 1)..] : argv;
        try
        {
            return await ExternalBuild.CompileWithTlsRetryAsync(command, Console.WriteLine, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"compile-launch failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> LinkExe(string[] argv)
    {
        string? output = null;
        bool debug = false, dxt = false;
        var libDirs = new List<string>();
        var inputs = new List<string>();
        var args = ExternalBuild.ExpandResponseFiles(argv);
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "-o" when i + 1 < args.Count: output = args[++i]; break;
                case "--debug": debug = true; break;
                case "--dxt": dxt = true; break;
                case "--lib-dir" when i + 1 < args.Count: libDirs.Add(args[++i]); break;
                default: inputs.Add(args[i]); break;
            }
        }
        if (output is null)
        {
            Console.Error.WriteLine("link-exe: -o <exe> is required");
            return 2;
        }
        try
        {
            var tc = await Toolchain.ResolveAsync();
            var classified = ExternalBuild.ClassifyLinkInputs(inputs);
            var r = await ExternalBuild.LinkAsync(tc, classified, libDirs, output, debug, dxt, Console.WriteLine, CancellationToken.None);
            if (!r.Success) Console.Error.WriteLine($"link-exe: link failed (exit {r.ExitCode})");
            return r.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"link-exe failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> MakeXbe(Dictionary<string, string> opts)
    {
        if (!opts.TryGetValue("exe", out var exe))
        {
            Console.Error.WriteLine("make-xbe: --exe <exe> is required");
            return 2;
        }
        try
        {
            // The same imageBuild settings a manifest takes, from a JSON file, with the common
            // ones also available as options.
            var image = new RxdkImageBuildOptions();
            if (opts.TryGetValue("image-build", out var json))
                image = JsonSerializer.Deserialize<RxdkImageBuildOptions>(File.ReadAllText(json), RxdkManifestLoader.JsonOptions)
                        ?? new RxdkImageBuildOptions();
            if (opts.TryGetValue("title-name", out var name)) image.TestName = name;
            if (opts.TryGetValue("title-id", out var id)) image.TestId = id;
            if (opts.TryGetValue("stack", out var stack)) image.StackSize = int.Parse(stack);
            var tool = RxdkPaths.ResolveHostTool("imagebld");
            var xbe = await ImageBuild.BuildXbeAsync(exe, tool, image, null, Directory.GetCurrentDirectory(), Console.WriteLine);
            Console.WriteLine($"Built {xbe}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"make-xbe failed: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> MakeIso(string[] argv)
    {
        string? xbe = null, name = null, outDir = null, root = null;
        var deploy = new List<string>();
        for (var i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--xbe" when i + 1 < argv.Length: xbe = argv[++i]; break;
                case "--name" when i + 1 < argv.Length: name = argv[++i]; break;
                case "--out-dir" when i + 1 < argv.Length: outDir = argv[++i]; break;
                case "--project-root" when i + 1 < argv.Length: root = argv[++i]; break;
                case "--deploy" when i + 1 < argv.Length: deploy.Add(argv[++i]); break;
            }
        }
        if (xbe is null || name is null || outDir is null)
        {
            Console.Error.WriteLine("make-iso: --xbe, --name and --out-dir are required");
            return 2;
        }
        try
        {
            var stage = PackXiso.ResolveDeployPaths(root ?? Directory.GetCurrentDirectory(), deploy, Console.WriteLine);
            var iso = await PackXiso.PackAsync(xbe, name, outDir, RxdkPaths.ResolveHostTool("xdvdfs"), stage, Console.WriteLine);
            Console.WriteLine($"Packed {iso}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"make-iso failed: {ex.Message}");
            return 1;
        }
    }
}
