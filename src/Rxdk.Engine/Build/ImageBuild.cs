using Rxdk.Engine.Model;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Build;

/// <summary>
/// Wraps the imagebld host tool: PE .exe → Xbox .xbe, or → flat .dxt. C# port of
/// RXDK-VSCode imageBuild.ts.
/// </summary>
public static class ImageBuild
{
    private sealed class ResolvedSettings
    {
        public int StackSize = 65536;
        public bool Debug = true;
        public bool NoLogo = true;
        public bool NoLibWarn = true;
        public bool LimitMemory;
        public bool DontModifyHardDisk;
        public bool DontMountUtilityDrive;
        public bool FormatUtilityDrive;
        public int UtilityDriveClusterSize;
        public List<string> NoPreload = new();
        // Certificate / title info (pass-through strings; empty = omit the switch).
        public string? TestId;
        public string? TestAltId;
        public string? TestRegion;
        public string? TestRatings;
        public string? TestMediaTypes;
        public string? TestLanKey;
        public string? TestSignKey;
        public string? TestName;
        public string? TestVersion;
        public string? TitleInfo;
        public string? TitleImage;
        public string? DefaultSaveImage;
    }

    private static ResolvedSettings Resolve(RxdkImageBuildOptions? o)
    {
        var s = new ResolvedSettings();
        if (o is null) return s;
        if (o.StackSize is { } v) s.StackSize = v;
        if (o.Debug is { } d) s.Debug = d;
        if (o.NoLogo is { } nl) s.NoLogo = nl;
        if (o.NoLibWarn is { } nlw) s.NoLibWarn = nlw;
        if (o.LimitMemory is { } lm) s.LimitMemory = lm;
        if (o.DontModifyHardDisk is { } dmh) s.DontModifyHardDisk = dmh;
        if (o.DontMountUtilityDrive is { } dmu) s.DontMountUtilityDrive = dmu;
        if (o.FormatUtilityDrive is { } fu) s.FormatUtilityDrive = fu;
        if (o.UtilityDriveClusterSize is { } uc) s.UtilityDriveClusterSize = uc;
        if (o.NoPreload is { } np) s.NoPreload = np;
        s.TestId = o.TestId;
        s.TestAltId = o.TestAltId;
        s.TestRegion = o.TestRegion;
        s.TestRatings = o.TestRatings;
        s.TestMediaTypes = o.TestMediaTypes;
        s.TestLanKey = o.TestLanKey;
        s.TestSignKey = o.TestSignKey;
        s.TestName = o.TestName;
        s.TestVersion = o.TestVersion;
        s.TitleInfo = o.TitleInfo;
        s.TitleImage = o.TitleImage;
        s.DefaultSaveImage = o.DefaultSaveImage;
        return s;
    }

    /// <summary>Convert a linked Win32 PE .exe into an Xbox .xbe. Returns the .xbe path.</summary>
    /// <param name="projectRoot">Base dir for resolving project-relative title/save-image files.</param>
    public static async Task<string> BuildXbeAsync(
        string inputExe, string toolPath, RxdkImageBuildOptions? imageBuild,
        IReadOnlyList<string>? insertFiles = null, string? projectRoot = null,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var input = Path.GetFullPath(inputExe);
        if (!File.Exists(input)) throw new FileNotFoundException($"imagebld: input not found: {input}");
        if (!File.Exists(toolPath)) throw new FileNotFoundException($"imagebld: tool not found: {toolPath}");

        var output = Path.GetFullPath(System.Text.RegularExpressions.Regex.Replace(input, @"\.exe$", ".xbe",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        var cfg = Resolve(imageBuild);
        if (cfg.FormatUtilityDrive && cfg.DontMountUtilityDrive)
            throw new InvalidOperationException(
                "imageBuild: formatUtilityDrive and dontMountUtilityDrive cannot both be true");

        // Resolve a project-relative file argument (title info / images) to an absolute path.
        string? ResolveFile(string? rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            var p = rel.Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(p) && !string.IsNullOrEmpty(projectRoot))
                p = Path.Combine(projectRoot, p);
            var full = Path.GetFullPath(p);
            if (!File.Exists(full)) log?.Invoke($"Warning: imagebld file not found: {full}");
            return full;
        }

        var args = new List<string> { $"/in:{input}", $"/out:{output}" };
        if (cfg.NoLogo) args.Add("/nologo");
        if (cfg.StackSize > 0) args.Add($"/stack:{cfg.StackSize}");
        if (cfg.Debug) args.Add("/debug");
        if (cfg.NoLibWarn) args.Add("/nolibwarn");
        if (cfg.LimitMemory) args.Add("/limitmem");
        if (cfg.DontModifyHardDisk) args.Add("/dontmodifyhd");
        if (cfg.DontMountUtilityDrive) args.Add("/dontmountud");
        if (cfg.FormatUtilityDrive) args.Add("/formatud");
        if (cfg.UtilityDriveClusterSize > 0) args.Add($"/udcluster:{cfg.UtilityDriveClusterSize}");
        foreach (var section in cfg.NoPreload.Where(s => !string.IsNullOrEmpty(s)))
            args.Add($"/nopreload:{section}");
        foreach (var insert in (insertFiles ?? Array.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)))
            args.Add($"/INSERTFILE:{insert}");

        // Certificate.
        if (!string.IsNullOrWhiteSpace(cfg.TestId)) args.Add($"/TESTID:{cfg.TestId.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.TestAltId)) args.Add($"/TESTALTID:{cfg.TestAltId.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.TestRegion)) args.Add($"/TESTREGION:{cfg.TestRegion.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.TestRatings)) args.Add($"/TESTRATINGS:{cfg.TestRatings.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.TestMediaTypes)) args.Add($"/TESTMEDIATYPES:{cfg.TestMediaTypes.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.TestLanKey)) args.Add($"/TESTLANKEY:{cfg.TestLanKey.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.TestSignKey)) args.Add($"/TESTSIGNKEY:{cfg.TestSignKey.Trim()}");
        // Title info.
        if (!string.IsNullOrWhiteSpace(cfg.TestName)) args.Add($"/TESTNAME:{cfg.TestName.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.TestVersion)) args.Add($"/TESTVERSION:{cfg.TestVersion.Trim()}");
        if (ResolveFile(cfg.TitleInfo) is { } ti) args.Add($"/TITLEINFO:{ti}");
        if (ResolveFile(cfg.TitleImage) is { } tim) args.Add($"/TITLEIMAGE:{tim}");
        if (ResolveFile(cfg.DefaultSaveImage) is { } dsi) args.Add($"/DEFAULTSAVEIMAGE:{dsi}");

        var r = await ProcessRunner.RunStreamedAsync(toolPath, args, log, ct: ct);
        if (!r.Success) throw new InvalidOperationException($"imagebld failed (exit {r.ExitCode})");
        return output;
    }

    /// <summary>Convert a linked PE .exe into a flat .dxt via `imagebld /DXT`. Returns the .dxt path.</summary>
    public static async Task<string> BuildDxtAsync(
        string inputExe, string outputDxt, string toolPath, Action<string>? log = null,
        CancellationToken ct = default)
    {
        var input = Path.GetFullPath(inputExe);
        if (!File.Exists(input)) throw new FileNotFoundException($"imagebld: input not found: {input}");
        if (!File.Exists(toolPath)) throw new FileNotFoundException($"imagebld: tool not found: {toolPath}");
        var output = Path.GetFullPath(outputDxt);

        // Colon-attached /in:/out: (not positional) so an absolute POSIX path on
        // Linux/macOS is never mistaken for a '/'-switch. imagebld's DXT mode accepts
        // both forms; this matches the normal-build invocation above.
        var r = await ProcessRunner.RunStreamedAsync(
            toolPath, new[] { "/DXT", $"/in:{input}", $"/out:{output}" }, log, ct: ct);
        if (!r.Success) throw new InvalidOperationException($"imagebld /DXT failed (exit {r.ExitCode})");
        return output;
    }
}
