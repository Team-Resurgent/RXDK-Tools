using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Rxdk.Engine.Model;

namespace Rxdk.Engine.Import;

/// <summary>
/// Imports a Visual Studio .NET 2003 XDK project (<c>.vcproj</c>, VisualStudioProject format) into
/// a native RXDK Visual Studio project (<c>.vcxproj</c> Makefile-type) plus its <c>.filters</c>.
/// Each VS2003 configuration is preserved; its compiler/linker/XboxImage/XboxDeployment settings
/// are mapped onto the RXDK per-configuration properties the property pages drive.
/// The RXDK scaffolding (Rxdk.Xbox.props/targets + the property-page rule XMLs) is copied from a
/// scaffold directory. By default source files are referenced in place (relative to the output
/// directory); pass <paramref name="copySources"/> to mirror them into the output folder so the
/// imported project is self-contained.
/// </summary>
public static class Vcproj2003Importer
{
    public sealed class ImportResult
    {
        public string VcxprojPath = "";
        /// <summary>The emitted rxdk.project.json (the project file VS Code loads).</summary>
        public string ManifestPath = "";
        public string ProjectName = "";
        public string ProjectGuid = "";
        public bool IsLibrary;
        public int ConfigurationCount;
        public int SourceCount;
        public List<(string Name, string Flavor)> Configs = new();
        public List<string> UnmappedLibraries = new();
        public List<string> Warnings = new();
        /// <summary>Per-file, clickable diagnostics in gcc/clang format (<c>path:line:col: warning: msg</c>)
        /// that both IDEs' output parsers turn into navigable Error-List / Problems entries. One entry
        /// per file per category (points at the first hit), bounded so a large import can't flood.</summary>
        public List<string> Diagnostics = new();
        /// <summary>True when the .vcproj has no Xbox configuration at all -- a PC-side host
        /// tool that ships alongside a sample (CreatePushBufferOnPC writes pushbuffer data on
        /// the PC for the Xbox sample to load). Nothing was generated for it.</summary>
        public bool SkippedNotXbox;
    }

    /// <summary>A native &lt;ProjectReference&gt; to emit into the generated .vcxproj.</summary>
    public sealed record ProjRef(string Name, string RelPath);


    // XDK link library (base name, variant suffix stripped) -> RXDK libraries, semicolon
    // separated. null = no equivalent. The list form is there for one-to-many mappings; XFONT
    // used to need it, but its objects now live in libxgraphics.lib exactly as they do in the
    // retail XDK, so xgraphics maps straight across again.
    private static readonly Dictionary<string, string?> LibMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["d3d8"] = "libd3d8", ["d3dx8"] = "libd3dx8", ["dsound"] = "libdsound",
        ["xapilib"] = "libxapi", ["xgraphics"] = "libxgraphics", ["xmv"] = "libxmv",
        ["xbdm"] = "libxbdm", ["xboxkrnl"] = "libkernel",
        ["xonline"] = "libxonline",
        // The XNet stack ships in two variants and they are NOT interchangeable: xnet
        // is the plain sockets build, xneto adds ONLINE/QoS/SG and only links when
        // paired with xonline. 'o' is a feature-set letter, not a variant suffix, so
        // each needs its own entry (the 'd'/'s' suffixes still strip off either).
        ["xnet"] = "libxnet", ["xneto"] = "libxneto", ["xnetos"] = "libxneto",
        ["xact"] = "libxact", ["xacteng"] = "libxact", ["dmusic"] = "libdmusic",
        ["xvoice"] = "libxvoice", ["xfont"] = "libxgraphics",
        // UIX is its own archive, as in 5849. It calls into libxonline, so a
        // title linking it needs both -- libuix first.
        ["uix"] = "libuix;libxonline",
        // No RXDK equivalent (soundtrack API / the instrumented perf build):
        ["xsndtrk"] = null, ["xperf"] = null,
    };

    // Defines RXDK provides itself; dropped from the imported per-config define list.
    private static readonly HashSet<string> DroppedDefines =
        new(StringComparer.OrdinalIgnoreCase) { "_XBOX", "XBOX", "_DEBUG", "NDEBUG" };

    private sealed class Cfg
    {
        public string Name = "";       // VS2003 config name without the |Platform suffix
        public string Flavor = "Release";
        public string? ReleaseOptimize;
        public string Defines = "";
        public string IncludePaths = "";
        public string Libraries = "";
        public string DeployPaths = "";
        // imagebld / cert / title
        public string? StackSize, ImageDebug, LimitMemory, DontModifyHd, DontMountUd, NoLibWarn;
        public string? TitleId, TitleName, TitleImage, XbeVersion;
    }

    public static ImportResult Import(string vcprojPath, string outDir, string? scaffoldDir,
        bool copySources = false, IReadOnlyList<ProjRef>? projectRefs = null, Action<string>? log = null)
    {
        vcprojPath = Path.GetFullPath(vcprojPath);
        if (!File.Exists(vcprojPath)) throw new FileNotFoundException($"vcproj not found: {vcprojPath}");
        var vcprojDir = Path.GetDirectoryName(vcprojPath)!;
        outDir = Path.GetFullPath(string.IsNullOrWhiteSpace(outDir) ? vcprojDir : outDir);
        Directory.CreateDirectory(outDir);

        // VS2003 .vcproj files are Windows-1252; net8 lacks that code page without an extra provider.
        // Decode as Latin1 (compatible for the ASCII paths/identifiers here) and parse the string, so
        // the encoding declaration in the prolog is ignored.
        var text = File.ReadAllText(vcprojPath, Encoding.Latin1);
        var doc = XDocument.Parse(text, LoadOptions.None);
        var root = doc.Root ?? throw new InvalidOperationException("Empty .vcproj");
        var name = (string?)root.Attribute("Name") ?? Path.GetFileNameWithoutExtension(vcprojPath);
        var isLib = (string?)FirstConfig(root)?.Attribute("ConfigurationType") == "4";

        var result = new ImportResult { ProjectName = name };
        var unmapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        result.IsLibrary = isLib;

        // ---- configurations ----
        // XDK .vcproj files carry Xbox-platform configs and often PC "|Win32" variants too; import
        // only the Xbox ones — the Win32 builds can't target the console and, sharing a base name
        // with their Xbox counterparts, would otherwise collide into duplicate configurations.
        var allElems = (root.Element("Configurations")?.Elements("Configuration") ?? Enumerable.Empty<XElement>()).ToList();
        var xboxElems = allElems.Where(c => ConfigPlatform(c).Equals("Xbox", StringComparison.OrdinalIgnoreCase)).ToList();
        if (xboxElems.Count == 0 && allElems.Count > 0)
        {
            // Not an Xbox project at all -- a PC-side host tool that happens to sit in a sample
            // directory, like CreatePushBufferOnPC (which writes pushbuffer data on the PC for
            // the Xbox PushBuffer sample to load). Importing it anyway produced a .vcxproj that
            // can never build: its sources include PC-only headers such as d3d8-xbox.h.
            result.SkippedNotXbox = true;
            result.Warnings.Add("no Xbox-platform configuration; skipped (PC-side host tool).");
            return result;
        }
        var allConfigs = xboxElems.Select(c => ParseConfig(c, unmapped)).ToList();

        // The generated .vcxproj lives one directory deeper than the source .vcproj (in a
        // <name>\ subfolder), so path-valued settings copied verbatim from the .vcproj --
        // which are relative to the .vcproj -- must be rebased to the .vcxproj dir, exactly
        // like the source files are (ResolveSources below). Without this a sample's
        // "..\..\Common\include" / deploy media point one level too shallow and aren't found.
        foreach (var c in allConfigs)
        {
            c.IncludePaths = RebasePathList(c.IncludePaths, vcprojDir, outDir);
            c.DeployPaths  = RebasePathList(c.DeployPaths, vcprojDir, outDir);
        }

        // Drop configs RXDK has no equivalent for (profiling / LTCG / SDL / Win32 name variants),
        // then dedupe by name so the generated project can never carry duplicate configurations.
        var configs = allConfigs.Where(c => !IsUnsupportedConfig(c.Name))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var dropped = allConfigs.Count - configs.Count;
        if (dropped > 0) result.Warnings.Add($"dropped {dropped} unsupported config(s) (profile/LTCG/fastcap/SDL/Win32).");
        if (configs.Count == 0) { configs = allConfigs; result.Warnings.Add("all configs were unsupported; kept them as-is."); }

        result.ConfigurationCount = configs.Count;
        result.Configs = configs.Select(c => (c.Name, c.Flavor)).ToList();
        result.UnmappedLibraries = unmapped.OrderBy(x => x).ToList();

        // ---- files (with filter folders) ----
        var rawFiles = new List<(string abs, string tag, string? filter)>();
        var filters = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFiles(root.Element("Files"), null, vcprojDir, rawFiles, filters);
        var sources = ResolveSources(rawFiles, vcprojDir, outDir, copySources, result);
        result.SourceCount = sources.Count(s => s.tag == "ClCompile");
        ScanForInlineAsm(rawFiles, result);
        ScanForLegacyForScope(rawFiles, result);

        // ---- write .vcxproj + .filters ----
        var projectGuid = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
        result.ProjectGuid = projectGuid;
        var vcxprojPath = Path.Combine(outDir, name + ".vcxproj");
        File.WriteAllText(vcxprojPath, BuildVcxproj(name, isLib, projectGuid, configs, sources, projectRefs), new UTF8Encoding(false));
        File.WriteAllText(vcxprojPath + ".filters", BuildFilters(sources, filters), new UTF8Encoding(false));
        result.VcxprojPath = vcxprojPath;

        // Also emit rxdk.project.json from the SAME parsed data. It's the project file VS Code opens,
        // and the committed manifest VS20XX's unified project model uses -- so an imported VS2003
        // project loads in both IDEs. Only adds a file; the .vcxproj flow above is unchanged.
        var manifestPath = Path.Combine(outDir, "rxdk.project.json");
        // The source list was built vcxproj-style (backslashes). The committed json must be
        // forward-slash: it is read on Linux/macOS too, and only '/' is portable there.
        var manifest = BuildManifest(name, isLib, configs, sources);
        RxdkManifestLoader.NormalizeSeparators(manifest);
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, ManifestWriteOptions) + "\n",
            new UTF8Encoding(false));
        result.ManifestPath = manifestPath;

        // No scaffold is copied per-project: the RXDK MSBuild integration (props/targets +
        // property-page rules) lives in the installed "Xbox" platform (VCTargetsPath\Platforms\Xbox),
        // which the imported project inherits from Platform=Xbox. The scaffoldDir parameter is kept
        // for API compatibility but is no longer used.
        _ = scaffoldDir;

        if (result.UnmappedLibraries.Count > 0)
            result.Warnings.Add("libraries with no RXDK equivalent (dropped): " +
                string.Join(", ", result.UnmappedLibraries) + " - the title may not link until those APIs are available.");

        log?.Invoke($"Imported {name}: {result.ConfigurationCount} configuration(s), {result.SourceCount} source file(s) -> {vcxprojPath}");
        foreach (var w in result.Warnings) log?.Invoke($"Warning: {w}");
        // Emit the clickable per-file diagnostics raw (no "Warning:" prefix) so the IDEs' gcc-style
        // parsers pick them up as navigable Error-List / Problems entries.
        foreach (var d in result.Diagnostics) log?.Invoke(d);
        return result;
    }

    // VC++ project type GUID used in .sln Project() entries (matches SolutionImporter's).
    private const string VcxprojTypeGuid = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";

    /// <summary>
    /// Writes a single-project <c>&lt;name&gt;.sln</c> beside the imported .vcxproj so the project opens
    /// directly in Visual Studio (double-click) rather than only as a bare .vcxproj. Multi-project
    /// solution imports use SolutionImporter's umbrella .sln instead, so this is only called for the
    /// standalone import-vcproj path. No-op if nothing was generated (e.g. a skipped non-Xbox project).
    /// </summary>
    public static void WriteSolution(string outDir, ImportResult result)
    {
        if (string.IsNullOrEmpty(result.VcxprojPath) || result.Configs.Count == 0) return;
        var name = result.ProjectName;
        var vcxprojRel = Path.GetFileName(result.VcxprojPath);
        var configs = result.Configs.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        sb.AppendLine("VisualStudioVersion = 17.0.0.0");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.0.0");
        sb.AppendLine($"Project(\"{VcxprojTypeGuid}\") = \"{name}\", \"{vcxprojRel}\", \"{result.ProjectGuid}\"");
        sb.AppendLine("EndProject");
        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        foreach (var c in configs) sb.AppendLine($"\t\t{c}|Xbox = {c}|Xbox");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var c in configs)
        {
            sb.AppendLine($"\t\t{result.ProjectGuid}.{c}|Xbox.ActiveCfg = {c}|Xbox");
            sb.AppendLine($"\t\t{result.ProjectGuid}.{c}|Xbox.Build.0 = {c}|Xbox");
        }
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
        sb.AppendLine("\t\tHideSolutionNode = FALSE");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("EndGlobal");
        File.WriteAllText(Path.Combine(outDir, name + ".sln"), sb.ToString(), new UTF8Encoding(false));
    }

    // Clean, indented output that omits null fields (a minimal manifest), matching the committed
    // rxdk.project.json files. camelCase + camelCase enums align with RxdkManifestLoader's reader.
    private static readonly JsonSerializerOptions ManifestWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // Per-configuration sub-manifests inherit the top-level name; skip their empty "name" on write
        // (WhenWritingNull can't -- string's default is null, not "") so only the top-level name is emitted.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static ti =>
                {
                    if (ti.Type != typeof(RxdkProjectManifest)) return;
                    foreach (var p in ti.Properties)
                        if (p.Name == "name")
                            p.ShouldSerialize = static (_, val) => val is string s && s.Length > 0;
                },
            },
        },
    };

    // Build the multi-config rxdk.project.json from the parsed configs + resolved sources. Sources are
    // shared across configs in a VS2003 project, so each config lists the same compile set (as the
    // sample manifests do); per-config libraries/defines/includes/deploy/image settings are preserved.
    private static RxdkProjectManifest BuildManifest(string name, bool isLib, List<Cfg> configs,
        List<(string include, string tag, string? filter)> sources)
    {
        var srcList = sources.Where(s => s.tag == "ClCompile").Select(s => s.include).ToList();
        static List<string>? AsList(string semi) =>
            SplitList(semi).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() is { Count: > 0 } l ? l : null;

        RxdkImageBuildOptions? Image(Cfg c)
        {
            if (isLib) return null;
            var hasAny = c.StackSize != null || c.ImageDebug != null || c.LimitMemory != null ||
                c.DontModifyHd != null || c.DontMountUd != null || c.NoLibWarn != null ||
                c.TitleId != null || c.TitleName != null || c.TitleImage != null || c.XbeVersion != null;
            if (!hasAny) return null;
            static bool? B(string? s) => s is null ? null : s == "true";
            return new RxdkImageBuildOptions
            {
                StackSize = int.TryParse(c.StackSize, out var ss) ? ss : null,
                Debug = B(c.ImageDebug),
                LimitMemory = B(c.LimitMemory),
                DontModifyHardDisk = B(c.DontModifyHd),
                DontMountUtilityDrive = B(c.DontMountUd),
                NoLibWarn = B(c.NoLibWarn),
                TestId = c.TitleId,
                TestName = c.TitleName ?? name,
                TestVersion = c.XbeVersion,
                TitleImage = c.TitleImage,
            };
        }

        RxdkProjectManifest ForConfig(Cfg c) => new()
        {
            Configuration = c.Flavor.Equals("Debug", StringComparison.OrdinalIgnoreCase)
                ? RxdkConfiguration.Debug : RxdkConfiguration.Release,
            Sources = srcList.Count > 0 ? new List<string>(srcList) : null,
            Libraries = AsList(c.Libraries),
            IncludePaths = AsList(c.IncludePaths),
            Defines = AsList(c.Defines),
            DeployPaths = AsList(c.DeployPaths),
            ImageBuild = Image(c),
        };

        var defaultCfg = configs.FirstOrDefault(c => c.Name.Equals("Debug", StringComparison.OrdinalIgnoreCase))?.Name
            ?? configs.FirstOrDefault()?.Name;

        return new RxdkProjectManifest
        {
            Name = name,
            Type = isLib ? RxdkProjectKind.Library : null,
            Configurations = configs.ToDictionary(c => c.Name, ForConfig, StringComparer.OrdinalIgnoreCase),
            DefaultConfiguration = defaultCfg,
        };
    }

    private static XElement? FirstConfig(XElement root) =>
        root.Element("Configurations")?.Elements("Configuration").FirstOrDefault();

    private static Cfg ParseConfig(XElement c, HashSet<string> unmapped)
    {
        var full = (string?)c.Attribute("Name") ?? "";
        var cfg = new Cfg { Name = full.Split('|')[0] };

        XElement? Tool(string n) => c.Elements("Tool").FirstOrDefault(t => (string?)t.Attribute("Name") == n);
        var cl = Tool("VCCLCompilerTool");
        var link = Tool("VCLinkerTool");
        var img = Tool("XboxImageTool");
        var dep = Tool("XboxDeploymentTool");

        // flavor + optimize from Optimization (0=Debug, 1=MinSize, 2/3=Speed) and the config name.
        var opt = (string?)cl?.Attribute("Optimization") ?? "";
        var isDebug = opt == "0" || cfg.Name.StartsWith("Debug", StringComparison.OrdinalIgnoreCase);
        cfg.Flavor = isDebug ? "Debug" : "Release";
        if (!isDebug) cfg.ReleaseOptimize = opt == "1" ? "ReleaseSmall" : "ReleaseFast";

        // defines: drop the ones RXDK provides.
        cfg.Defines = string.Join(";", SplitList((string?)cl?.Attribute("PreprocessorDefinitions"))
            .Where(d => !DroppedDefines.Contains(d)));
        cfg.IncludePaths = string.Join(";", SplitList((string?)cl?.Attribute("AdditionalIncludeDirectories")));

        // libraries: map XDK -> RXDK, dedupe, always add libc/libcpp/libkernel.
        var libs = new List<string>();
        void Add(string l) { if (!libs.Contains(l, StringComparer.OrdinalIgnoreCase)) libs.Add(l); }
        foreach (var raw in SplitLibs((string?)link?.Attribute("AdditionalDependencies")))
        {
            var mapped = MapLib(raw, out var known);
            if (mapped != null) foreach (var one in mapped.Split(';')) Add(one);
            else if (!known) { /* unknown, non-.lib token - ignore */ }
            else unmapped.Add(raw);
        }
        foreach (var forced in new[] { "libc", "libcpp", "libkernel" }) Add(forced);
        cfg.Libraries = string.Join(";", libs);

        // deploy files
        cfg.DeployPaths = string.Join(";", SplitList((string?)dep?.Attribute("AdditionalFiles")));

        // imagebld / cert / title
        cfg.StackSize = NormalizeInt((string?)img?.Attribute("StackSize"));
        cfg.ImageDebug = Bool((string?)img?.Attribute("IncludeDebugInfo"));
        cfg.LimitMemory = Bool((string?)img?.Attribute("LimitAvailableMemoryTo64MB"));
        cfg.DontModifyHd = Bool((string?)img?.Attribute("DontModifyHD"));
        cfg.DontMountUd = Bool((string?)img?.Attribute("DontMountUD"));
        cfg.NoLibWarn = Bool((string?)img?.Attribute("NoLibWarn"));
        cfg.TitleId = NonEmpty((string?)img?.Attribute("TitleID"));
        cfg.TitleName = NonEmpty((string?)img?.Attribute("TitleName"));
        cfg.TitleImage = NonEmpty((string?)img?.Attribute("TitleImage"));
        cfg.XbeVersion = NonEmpty((string?)img?.Attribute("XBEVersion"));
        return cfg;
    }

    // The platform half of a VS2003 config Name ("Debug|Xbox" -> "Xbox").
    private static string ConfigPlatform(XElement c)
    {
        var full = (string?)c.Attribute("Name") ?? "";
        var bar = full.IndexOf('|');
        return bar >= 0 ? full[(bar + 1)..].Trim() : "";
    }

    // Config names RXDK has no build equivalent for: profiling/instrumentation modes and non-Xbox
    // platform variants encoded into the config name (the XDK uses "(Win32)"/"(SDL)" suffixes).
    private static bool IsUnsupportedConfig(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("profile") || n.Contains("ltcg") || n.Contains("fastcap") || n.Contains("fast cap")
            || n.Contains("(win32)") || n.Contains("(win)") || n.Contains("(sdl)") || n.Contains("(pc)");
    }

    private static string? MapLib(string token, out bool known)
    {
        known = false;
        var baseName = token;
        if (baseName.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^4];
        // strip a variant suffix (debug 'd', instrumented 'i', static 's', 'ltcg') to find the base.
        foreach (var (b, rxdk) in LibMap)
        {
            if (baseName.Equals(b, StringComparison.OrdinalIgnoreCase) ||
                (baseName.StartsWith(b, StringComparison.OrdinalIgnoreCase) &&
                 IsVariantSuffix(baseName[b.Length..])))
            {
                known = true;
                return rxdk; // may be null (known but no RXDK equivalent)
            }
        }
        return null;
    }

    private static bool IsVariantSuffix(string s) =>
        s.Length == 0 || s.Equals("d", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("i", StringComparison.OrdinalIgnoreCase) || s.Equals("s", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("ltcg", StringComparison.OrdinalIgnoreCase);

    // ---- files ----

    private static void CollectFiles(XElement? node, string? filterPath, string vcprojDir,
        List<(string abs, string tag, string? filter)> files, SortedSet<string> filters)
    {
        if (node == null) return;
        foreach (var el in node.Elements())
        {
            if (el.Name.LocalName == "Filter")
            {
                var fn = (string?)el.Attribute("Name") ?? "";
                var path = string.IsNullOrEmpty(filterPath) ? fn : filterPath + "\\" + fn;
                if (!string.IsNullOrEmpty(path)) filters.Add(path);
                CollectFiles(el, path, vcprojDir, files, filters);
            }
            else if (el.Name.LocalName == "File")
            {
                var rel = (string?)el.Attribute("RelativePath") ?? "";
                if (string.IsNullOrWhiteSpace(rel)) continue;
                var abs = Path.GetFullPath(Path.Combine(vcprojDir, rel.Replace("/", "\\")));
                var ext = Path.GetExtension(abs).ToLowerInvariant();
                var tag = ext is ".cpp" or ".cxx" or ".cc" or ".c" ? "ClCompile"
                        : ext is ".h" or ".hpp" or ".hxx" or ".inl" ? "ClInclude" : "None";
                files.Add((abs, tag, filterPath));
            }
        }
    }

    // Turn raw absolute file references into the &lt;include&gt; paths written to the .vcxproj.
    // Default: reference the originals in place (relative to outDir). With copySources: mirror each
    // file into outDir, preserving its path relative to the source project (leading "..\" segments
    // are collapsed so nothing escapes outDir), then reference the copy. Same-name clashes from
    // different sources get a numeric suffix. Files already inside outDir are left untouched.
    // MSVC __asm/_asm blocks (NOT GCC __asm__(...) which Clang handles) — matches the bare token,
    // excluding __asm__ where the trailing "__" makes it GCC extended asm.
    private static readonly Regex MsvcAsmRegex =
        new(@"(?<![\w])(__asm|_asm)(?![\w])", RegexOptions.Compiled);

    // Comment/string spans. Replaced with equal-length blanks that KEEP newlines, so scanning the
    // result still lines up 1:1 with the original for accurate line numbers, while a token inside a
    // comment or string literal can't trip a false positive.
    private static readonly Regex StripRegex =
        new(@"//[^\n]*|/\*.*?\*/|""(\\.|[^""\\])*""|'(\\.|[^'\\])*'",
            RegexOptions.Compiled | RegexOptions.Singleline);

    // A bare local declaration like "int i;" on its own line.
    private static readonly Regex BareDeclRegex = new(
        @"^\s*(?:unsigned\s+)?(?:int|short|long|char|float|double|DWORD|WORD|BYTE|size_t)\s+([A-Za-z_]\w*)\s*;\s*$",
        RegexOptions.Compiled);

    // A for-loop that (re)declares a loop variable: "for (int i = ...".
    private static readonly Regex ForDeclRegex = new(
        @"\bfor\s*\(\s*(?:unsigned\s+)?(?:int|short|long|char|float|double|DWORD|WORD|BYTE|size_t)\s+([A-Za-z_]\w*)\s*=",
        RegexOptions.Compiled);

    private const int MaxDiagnosticFiles = 50;

    private static readonly HashSet<string> CodeExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".c", ".cpp", ".cxx", ".cc", ".h", ".hpp", ".hxx", ".inl" };

    // Blank out comments/strings but preserve line breaks so line numbers stay exact, then split.
    private static string[] StrippedLines(string text) =>
        StripRegex.Replace(text, m => Regex.Replace(m.Value, "[^\n]", " ")).Split('\n');

    /// <summary>
    /// Emit a bounded set of diagnostics for one hazard category: a single summary "Warning:" headline
    /// listing the affected files, plus ONE clickable <c>path:line:col: warning: msg</c> diagnostic per
    /// file (anchored at the first hit, noting "+N more in this file"). Capped at <see cref="MaxDiagnosticFiles"/>
    /// files so a large legacy import can't flood the Error List / Problems panel.
    /// </summary>
    private static void EmitFileDiagnostics(
        ImportResult result, string category, string advice, List<(string abs, int firstLine, int count)> hits)
    {
        if (hits.Count == 0) return;
        var shown = hits.Take(MaxDiagnosticFiles).ToList();
        result.Warnings.Add(
            $"{category} detected in {hits.Count} file(s): " +
            string.Join(", ", shown.Select(h => Path.GetFileName(h.abs))) +
            (hits.Count > shown.Count ? $", +{hits.Count - shown.Count} more" : "") +
            ". " + advice);
        foreach (var h in shown)
        {
            var more = h.count > 1 ? $" (+{h.count - 1} more in this file)" : "";
            result.Diagnostics.Add($"{h.abs}:{h.firstLine}:1: warning: {category}{more} — {advice}");
        }
        if (hits.Count > shown.Count)
            result.Diagnostics.Add($"...and {hits.Count - shown.Count} more file(s) with {category} (not listed; cap {MaxDiagnosticFiles}).");
    }

    /// <summary>
    /// Warns when imported C/C++ sources contain MSVC inline <c>_asm</c>/<c>__asm</c> blocks. The RXDK
    /// toolchain is Clang, which miscompiles hand-written MS inline asm that clobbers registers without
    /// declaring it (a real bug we hit: an <c>fsincos</c> block corrupting <c>ebx</c> and hanging the
    /// title). Flag it at import so the porter reviews/rewrites those blocks in portable C.
    /// </summary>
    private static void ScanForInlineAsm(
        List<(string abs, string tag, string? filter)> raw, ImportResult result)
    {
        var hits = new List<(string abs, int firstLine, int count)>();
        foreach (var abs in DistinctCodeFiles(raw))
        {
            string text;
            try { text = File.ReadAllText(abs); }
            catch { continue; }
            var lines = StrippedLines(text);
            int first = 0, count = 0;
            for (int i = 0; i < lines.Length; i++)
                if (MsvcAsmRegex.IsMatch(lines[i])) { if (count == 0) first = i + 1; count++; }
            if (count > 0) hits.Add((abs, first, count));
        }
        EmitFileDiagnostics(result, "MSVC inline __asm",
            "the RXDK Clang toolchain miscompiles hand-written MS inline asm that clobbers registers " +
            "without declaring them (can hang or crash the title); rewrite the asm in portable C.", hits);
    }

    /// <summary>
    /// Warns when imported sources use the VS2003 <c>/Zc:forScope-</c> idiom: a bare <c>TYPE name;</c>
    /// declaration shortly followed by <c>for (TYPE name = ...)</c>. Under old MSVC the loop variable had
    /// function scope, so code AFTER the loop read its exit value; under the RXDK Clang toolchain the
    /// <c>for</c> declaration is block-scoped and shadows the outer one, leaving it uninitialized — a real
    /// bug we hit (BootAnim's camera controller read a garbage node index every frame → hang). Heuristic:
    /// flags any file with the idiom for review, even where the counter is only used inside the loop.
    /// </summary>
    private static void ScanForLegacyForScope(
        List<(string abs, string tag, string? filter)> raw, ImportResult result)
    {
        var hits = new List<(string abs, int firstLine, int count)>();
        foreach (var abs in DistinctCodeFiles(raw))
        {
            string text;
            try { text = File.ReadAllText(abs); }
            catch { continue; }
            var lines = StrippedLines(text);
            var recent = new Dictionary<string, int>(StringComparer.Ordinal); // ident -> decl line index
            int first = 0, count = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var bd = BareDeclRegex.Match(lines[i]);
                if (bd.Success) recent[bd.Groups[1].Value] = i;
                var fd = ForDeclRegex.Match(lines[i]);
                if (fd.Success && recent.TryGetValue(fd.Groups[1].Value, out var declLine) && i - declLine <= 12)
                { if (count == 0) first = i + 1; count++; }
            }
            if (count > 0) hits.Add((abs, first, count));
        }
        EmitFileDiagnostics(result, "legacy VS2003 for-scope idiom",
            "under Clang the for-loop variable is block-scoped, so code reading the counter after the loop " +
            "reads a different, uninitialized variable (can hang or misbehave); drop the redundant type in " +
            "the for so it reuses the outer variable.", hits);
    }

    // Distinct existing C/C++ source/header files from the raw reference list, preserving order.
    private static IEnumerable<string> DistinctCodeFiles(List<(string abs, string tag, string? filter)> raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (abs, _, _) in raw)
            if (CodeExtensions.Contains(Path.GetExtension(abs)) && seen.Add(abs) && File.Exists(abs))
                yield return abs;
    }

    private static List<(string include, string tag, string? filter)> ResolveSources(
        List<(string abs, string tag, string? filter)> raw, string vcprojDir, string outDir,
        bool copySources, ImportResult result)
    {
        var sources = new List<(string include, string tag, string? filter)>();
        if (!copySources)
        {
            var anyAbsolute = false;
            foreach (var (abs, tag, filter) in raw)
            {
                var include = MakeRelative(outDir, abs);
                if (Path.IsPathFullyQualified(include)) anyAbsolute = true;
                sources.Add((include, tag, filter));
            }
            if (anyAbsolute)
                result.Warnings.Add("some sources live outside the output folder and were referenced by " +
                    "ABSOLUTE path, so the generated project is not portable. Import in place (output = the " +
                    "project's own folder), or pass --copy-sources to mirror the sources into the output folder.");
            return sources;
        }

        var usedDest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // destAbs -> srcAbs
        var copied = 0;
        foreach (var (abs, tag, filter) in raw)
        {
            var destRel = SafeDestRel(vcprojDir, abs);
            var destAbs = Path.GetFullPath(Path.Combine(outDir, destRel));
            if (usedDest.TryGetValue(destAbs, out var prevSrc) && !PathEquals(prevSrc, abs))
                destRel = Disambiguate(destRel, outDir, usedDest, out destAbs);

            if (!usedDest.ContainsKey(destAbs))
            {
                usedDest[destAbs] = abs;
                if (!File.Exists(abs))
                {
                    result.Warnings.Add($"source not found, not copied: {abs}");
                }
                else if (!PathEquals(destAbs, abs)) // don't copy a file onto itself (outDir == source)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
                    File.Copy(abs, destAbs, overwrite: true);
                    copied++;
                }
            }
            sources.Add((destRel.Replace('/', '\\'), tag, filter));
        }
        result.Warnings.Add($"copied {copied} source file(s) into the output folder.");
        return sources;
    }

    // File path relative to the source project, with leading "..\" (or a drive root) collapsed so the
    // copy stays inside outDir. e.g. "..\..\common\util.cpp" -> "common\util.cpp".
    private static string SafeDestRel(string vcprojDir, string abs)
    {
        var rel = Path.GetRelativePath(vcprojDir, abs);
        if (Path.IsPathRooted(rel)) return Path.GetFileName(abs); // different drive: flatten to name
        var parts = rel.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                       .Where(p => p != "..").ToArray();
        return parts.Length == 0 ? Path.GetFileName(abs) : string.Join("\\", parts);
    }

    // Append _2, _3, ... before the extension until the destination is unused.
    private static string Disambiguate(string destRel, string outDir,
        Dictionary<string, string> usedDest, out string destAbs)
    {
        var dir = Path.GetDirectoryName(destRel) ?? "";
        var stem = Path.GetFileNameWithoutExtension(destRel);
        var ext = Path.GetExtension(destRel);
        for (var i = 2; ; i++)
        {
            var cand = string.IsNullOrEmpty(dir) ? $"{stem}_{i}{ext}" : Path.Combine(dir, $"{stem}_{i}{ext}");
            destAbs = Path.GetFullPath(Path.Combine(outDir, cand));
            if (!usedDest.ContainsKey(destAbs)) return cand;
        }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---- .vcxproj / .filters emit ----

    private static string BuildVcxproj(string name, bool isLib, string projectGuid, List<Cfg> configs,
        List<(string include, string tag, string? filter)> sources, IReadOnlyList<ProjRef>? projectRefs)
    {
        var ext = isLib ? "lib" : "xbe";
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        sb.AppendLine("  <ItemGroup Label=\"ProjectConfigurations\">");
        foreach (var c in configs)
        {
            sb.AppendLine($"    <ProjectConfiguration Include=\"{Esc(c.Name)}|Xbox\">");
            sb.AppendLine($"      <Configuration>{Esc(c.Name)}</Configuration>");
            sb.AppendLine("      <Platform>Xbox</Platform>");
            sb.AppendLine("    </ProjectConfiguration>");
        }
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("  <PropertyGroup Label=\"Globals\">");
        sb.AppendLine("    <VCProjectVersion>16.0</VCProjectVersion>");
        sb.AppendLine($"    <ProjectGuid>{projectGuid}</ProjectGuid>");
        sb.AppendLine("    <RootNamespace>XboxNamespace</RootNamespace>");
        sb.AppendLine("    <WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>");
        sb.AppendLine($"    <ProjectName>{Esc(name)}</ProjectName>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.Default.props\" />");
        sb.AppendLine("  <PropertyGroup Label=\"Configuration\">");
        sb.AppendLine("    <ConfigurationType>Makefile</ConfigurationType>");
        sb.AppendLine("    <PlatformToolset Condition=\"'$(VisualStudioVersion)' == '17.0'\">v143</PlatformToolset>");
        sb.AppendLine("    <PlatformToolset Condition=\"'$(VisualStudioVersion)' == '18.0'\">v145</PlatformToolset>");
        sb.AppendLine("    <PlatformToolset Condition=\"'$(PlatformToolset)' == ''\">v143</PlatformToolset>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.props\" />");
        sb.AppendLine("  <PropertyGroup>");
        if (isLib) sb.AppendLine("    <RxdkType>library</RxdkType>");
        sb.AppendLine($"    <NMakeOutput>$(MSBuildProjectDirectory)\\$(RxdkOutDir)\\$(MSBuildProjectName).{ext}</NMakeOutput>");
        sb.AppendLine("  </PropertyGroup>");

        foreach (var c in configs)
        {
            sb.AppendLine($"  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='{Esc(c.Name)}|Xbox'\">");
            sb.AppendLine($"    <RxdkBuildFlavor>{c.Flavor}</RxdkBuildFlavor>");
            if (c.ReleaseOptimize != null) sb.AppendLine($"    <RxdkReleaseOptimize>{c.ReleaseOptimize}</RxdkReleaseOptimize>");
            Prop(sb, "RxdkDefines", c.Defines);
            Prop(sb, "RxdkIncludePaths", c.IncludePaths);
            Prop(sb, "RxdkLibraries", c.Libraries);
            Prop(sb, "RxdkDeployPaths", c.DeployPaths);
            Prop(sb, "RxdkStackSize", c.StackSize);
            Prop(sb, "RxdkImageDebug", c.ImageDebug);
            Prop(sb, "RxdkLimitMemory", c.LimitMemory);
            Prop(sb, "RxdkDontModifyHardDisk", c.DontModifyHd);
            Prop(sb, "RxdkDontMountUtilityDrive", c.DontMountUd);
            Prop(sb, "RxdkNoLibWarn", c.NoLibWarn);
            Prop(sb, "RxdkTestId", c.TitleId);
            Prop(sb, "RxdkTestName", c.TitleName);
            Prop(sb, "RxdkTitleImage", c.TitleImage);
            Prop(sb, "RxdkTestVersion", c.XbeVersion);
            sb.AppendLine("  </PropertyGroup>");
        }

        EmitItems(sb, sources, "ClCompile");
        EmitItems(sb, sources, "ClInclude");
        EmitItems(sb, sources, "None");
        // Native project references (build order); RXDK links each child .lib via the manifest
        // projectReferences the targets derive from @(ProjectReference). The ItemDefinitionGroup in
        // the Xbox platform already marks these build-order-only, so no per-item metadata is needed.
        if (projectRefs is { Count: > 0 })
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var r in projectRefs) sb.AppendLine($"    <ProjectReference Include=\"{Esc(r.RelPath)}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.targets\" />");
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private static void EmitItems(StringBuilder sb, List<(string include, string tag, string? filter)> sources, string tag)
    {
        var items = sources.Where(s => s.tag == tag).ToList();
        if (items.Count == 0) return;
        sb.AppendLine("  <ItemGroup>");
        foreach (var s in items) sb.AppendLine($"    <{tag} Include=\"{Esc(s.include)}\" />");
        sb.AppendLine("  </ItemGroup>");
    }

    private static string BuildFilters(List<(string include, string tag, string? filter)> sources, SortedSet<string> filters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Project ToolsVersion=\"4.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        if (filters.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var f in filters)
            {
                sb.AppendLine($"    <Filter Include=\"{Esc(f)}\">");
                sb.AppendLine($"      <UniqueIdentifier>{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}</UniqueIdentifier>");
                sb.AppendLine("    </Filter>");
            }
            sb.AppendLine("  </ItemGroup>");
        }
        foreach (var tag in new[] { "ClCompile", "ClInclude", "None" })
        {
            var items = sources.Where(s => s.tag == tag).ToList();
            if (items.Count == 0) continue;
            sb.AppendLine("  <ItemGroup>");
            foreach (var s in items)
            {
                if (string.IsNullOrEmpty(s.filter)) sb.AppendLine($"    <{tag} Include=\"{Esc(s.include)}\" />");
                else
                {
                    sb.AppendLine($"    <{tag} Include=\"{Esc(s.include)}\">");
                    sb.AppendLine($"      <Filter>{Esc(s.filter)}</Filter>");
                    sb.AppendLine($"    </{tag}>");
                }
            }
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    // ---- small helpers ----

    private static void Prop(StringBuilder sb, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) sb.AppendLine($"    <{name}>{Esc(value)}</{name}>");
    }

    private static IEnumerable<string> SplitList(string? s) =>
        (s ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);

    private static IEnumerable<string> SplitLibs(string? s) =>
        (s ?? "").Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);

    private static string? Bool(string? v) =>
        v == null ? null : (v.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? "true" : "false");

    private static string? NonEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // "0x40000" or "262144" -> decimal string (the manifest emits stackSize as a raw JSON number).
    private static string? NormalizeInt(string? v)
    {
        v = NonEmpty(v);
        if (v == null) return null;
        try
        {
            var n = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? long.Parse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : long.Parse(v, CultureInfo.InvariantCulture);
            return n.ToString(CultureInfo.InvariantCulture);
        }
        catch { return null; }
    }

    private static string MakeRelative(string baseDir, string path)
    {
        var rel = Path.GetRelativePath(baseDir, path);
        return rel.Replace('/', '\\');
    }

    // Rebase a ';'-separated list of paths that were relative to `fromDir` (the source
    // .vcproj) so they are relative to `toDir` (the generated .vcxproj). Absolute paths
    // and entries containing MSBuild macros ($(...)) pass through untouched.
    private static string RebasePathList(string list, string fromDir, string toDir)
    {
        if (string.IsNullOrWhiteSpace(list)) return list;
        var rebased = list
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => (p.Contains('$') || Path.IsPathRooted(p))
                ? p
                : MakeRelative(toDir, Path.GetFullPath(Path.Combine(fromDir, p))));
        return string.Join(";", rebased);
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
