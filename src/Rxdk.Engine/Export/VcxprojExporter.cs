using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Rxdk.Engine.Import;
using Rxdk.Engine.Model;

namespace Rxdk.Engine.Export;

/// <summary>
/// Generates a native Rxdk.MsBuild <c>.vcxproj</c> (+ single-project <c>.sln</c>) FROM an existing
/// <c>rxdk.project.json</c>, so a project created the VS Code / Open Folder way -- which has no
/// MSBuild project at all -- can be opened and built in Visual Studio too. The manifest schema is
/// the stable contract shared with RXDK-VSCode; this only reads it. Field names in the generated
/// project mirror the same ApplicationType=RXDK shape the TemplateSrc templates use (see
/// TemplateSrc/Dxt, TemplateSrc/Cube) -- real ClCompile/Link/ImageBld items, no JSON manifest or
/// Makefile intermediary. A field is only emitted where its value differs from the Rxdk.MsBuild.props
/// default, so the generated vcxproj reads like a hand-authored template.
/// </summary>
public static class VcxprojExporter
{
    public sealed class ExportResult
    {
        public string VcxprojPath = "";
        public string SlnPath = "";
        public string ProjectName = "";
        public string ProjectGuid = "";
        public List<string> Warnings = new();
    }

    public static ExportResult Export(string projectRoot)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var manifest = RxdkManifestLoader.TryLoad(projectRoot)
            ?? throw new FileNotFoundException($"no {RxdkManifestLoader.ManifestFileName} under {projectRoot}");

        var name = !string.IsNullOrWhiteSpace(manifest.Name)
            ? manifest.Name
            : Path.GetFileName(projectRoot.TrimEnd('\\', '/'));

        // A flat (single-config) manifest applies identically regardless of which VS configuration is
        // active (ResolveConfiguration ignores the requested name when there's no "configurations"
        // dict), so synthesize the two standard Debug/Release vcxproj configs from it. A multi-config
        // manifest keeps its own named configs.
        var configNames = manifest.ConfigurationNames.Count > 0
            ? manifest.ConfigurationNames.ToList()
            : new List<string> { "Debug", "Release" };

        var result = new ExportResult { ProjectName = name, ProjectGuid = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}" };
        var defaultCfg = !string.IsNullOrEmpty(manifest.DefaultConfiguration) && configNames.Contains(manifest.DefaultConfiguration!)
            ? manifest.DefaultConfiguration!
            : configNames[0];
        var defaultManifest = manifest.ResolveConfiguration(defaultCfg);

        var (nativeRefs, unresolvedRefs) = ResolveProjectReferences(defaultManifest.ProjectReferences, projectRoot, result.Warnings);

        var perConfigLink = configNames.ToDictionary(c => c, c => LinkMeta(manifest.ResolveConfiguration(c), projectRoot, result.Warnings));
        var perConfigCompile = configNames.ToDictionary(c => c, c => CompileMeta(manifest.ResolveConfiguration(c), projectRoot, nativeRefs, result.Warnings));
        var perConfigImageBld = defaultManifest.IsLibrary
            ? new Dictionary<string, Dictionary<string, string>>()
            : configNames.ToDictionary(c => c, c => ImageBldMeta(manifest.ResolveConfiguration(c)));

        if (defaultManifest.Resources is { Count: > 0 })
            result.Warnings.Add($"{defaultManifest.Resources.Count} resource (.rdf) file(s) are listed but not compiled: the bundler pipeline isn't wired into the MSBuild toolset yet. They're added to the project as non-built items.");
        if (defaultManifest.ForceCopy == true)
            result.Warnings.Add("forceCopy has no vcxproj equivalent (deploy-to-devkit incremental copy is a VS Code launch setting) and was not exported.");
        if (defaultManifest.CreateIso == false)
            result.Warnings.Add("createIso:false has no vcxproj equivalent yet -- the generated project always packs an ISO.");

        result.VcxprojPath = Path.Combine(projectRoot, name + ".vcxproj");
        File.WriteAllText(
            result.VcxprojPath,
            BuildVcxproj(name, result.ProjectGuid, defaultManifest, configNames, perConfigLink, perConfigCompile, perConfigImageBld, nativeRefs),
            new UTF8Encoding(false));

        // Reuse the VS2003 importer's single-project .sln writer -- same shape, no need to duplicate it.
        var slnResult = new Vcproj2003Importer.ImportResult
        {
            VcxprojPath = result.VcxprojPath,
            ProjectName = name,
            ProjectGuid = result.ProjectGuid,
            Configs = configNames.Select(c => (c, "release")).ToList(),
        };
        Vcproj2003Importer.WriteSolution(projectRoot, slnResult);
        result.SlnPath = Path.Combine(projectRoot, name + ".sln");

        return result;
    }

    // Native <ProjectReference> for deps that already have a .vcxproj (build-order + IDE navigation +
    // the source of RxdkAdditionalIncludeDirectories for their PublicIncludePaths). A dependency
    // without one yet can't link at all in the new toolset (there's no manifest-driven fallback link
    // path the way the old Makefile engine had) -- it needs "Import VSCode Project" run on it first.
    private static (List<(string RelPath, string Guid, string PublicIncludeDir)> Native, List<string> Unresolved) ResolveProjectReferences(
        List<string>? refs, string projectRoot, List<string> warnings)
    {
        var native = new List<(string, string, string)>();
        var unresolved = new List<string>();
        foreach (var rel in refs ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var depDir = Path.GetFullPath(Path.Combine(projectRoot, rel));
            var depVcxproj = Directory.Exists(depDir)
                ? Directory.GetFiles(depDir, "*.vcxproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
                : null;
            if (depVcxproj != null)
            {
                var guid = ReadProjectGuid(depVcxproj);
                var depManifest = RxdkManifestLoader.TryLoad(depDir);
                var publicInc = depManifest?.PublicIncludePaths is { Count: > 0 }
                    ? string.Join(";", depManifest.PublicIncludePaths.Select(p =>
                        Path.GetRelativePath(projectRoot, Path.GetFullPath(Path.Combine(depDir, p))).Replace('/', '\\')))
                    : "";
                if (guid != null)
                {
                    native.Add((Path.GetRelativePath(projectRoot, depVcxproj), guid, publicInc));
                    continue;
                }
            }
            unresolved.Add(rel);
            warnings.Add($"projectReference \"{rel}\" has no .vcxproj yet -- run Import VSCode Project on it too, then re-import this project to pick it up.");
        }
        return (native, unresolved);
    }

    private static string? ReadProjectGuid(string vcxprojPath)
    {
        try
        {
            var doc = XDocument.Load(vcxprojPath);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            return doc.Descendants(ns + "ProjectGuid").FirstOrDefault()?.Value?.Trim();
        }
        catch { return null; }
    }

    // ---- per-config item metadata (the reverse of what a hand-authored TemplateSrc .vcxproj sets) ----

    private static string Join(IEnumerable<string>? xs) => string.Join(";", (xs ?? Enumerable.Empty<string>()).Select(x => x.Replace('/', '\\')));

    // Link: LibraryDependencies is a real "-lNAME" per-name list (Rxdk.MsBuild.props' own
    // LibraryDependencies default works the same way) -- the manifest's Libraries entries already
    // carry the config-appropriate name verbatim (e.g. "libd3d8d.lib" for a Debug config), same as
    // a real "Additional Dependencies" list, so this only strips the ".lib" extension the -l switch
    // adds back on its own. No attempt to collapse Debug/Release names into a single "$(D)"-suffixed
    // value: each config keeps its own explicit names, exactly like every hand-authored template.
    private static Dictionary<string, string> LinkMeta(RxdkProjectManifest m, string projectRoot, List<string> warnings)
    {
        var meta = new Dictionary<string, string>();
        void Add(string k, string v) { if (!string.IsNullOrEmpty(v)) meta[k] = v; }

        static string StripLibExt(string s) => s.EndsWith(".lib", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
        var libNames = (m.Libraries ?? new()).Select(StripLibExt).ToList();

        // libcompat[d] needs an explicit whole-archive wrap to win its COMDAT tie-break against
        // zig's bundled compiler-rt (fabs/sqrt/sin/...; see Rxdk.MsBuild.props' own comment): a
        // plain "-lNAME" only pulls referenced objects, which defeats the point. No toolset-side
        // auto-detection (see the same comment) -- the manifest already names it explicitly like
        // any other library (carried over from the old engine's XdkLink.IsWholeArchiveLib opt-in),
        // so it's pulled out of the plain list here and given the matching whole-archive/
        // no-whole-archive AdditionalOptions instead of a second, redundant plain "-l" entry.
        var compatName = libNames.FirstOrDefault(n =>
            n.Equals("libcompat", StringComparison.OrdinalIgnoreCase) || n.Equals("libcompatd", StringComparison.OrdinalIgnoreCase));
        if (compatName != null)
        {
            libNames.Remove(compatName);
            Add("AdditionalOptions", $"%(Link.AdditionalOptions) -Wl,--whole-archive -l{compatName} -Wl,--no-whole-archive");
        }

        Add("LibraryDependencies", Join(libNames));
        Add("AdditionalLibraryDirectories", Join(m.LibraryPaths));

        // Explicit prebuilt .lib files, linked verbatim (full paths, not "-lNAME" search targets).
        var additional = new List<string>();
        foreach (var rel in m.AdditionalLibraries ?? new())
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            additional.Add(Path.GetRelativePath(projectRoot, Path.GetFullPath(Path.Combine(projectRoot, rel))).Replace('/', '\\'));
        }
        Add("AdditionalDependencies", string.Join(";", additional));

        return meta;
    }

    private static Dictionary<string, string> CompileMeta(
        RxdkProjectManifest m, string projectRoot,
        List<(string RelPath, string Guid, string PublicIncludeDir)> nativeRefs, List<string> warnings)
    {
        var meta = new Dictionary<string, string>();
        void Add(string k, string v) { if (!string.IsNullOrEmpty(v)) meta[k] = v; }

        // Own extra include dirs + own PublicIncludePaths (a library including its own public
        // header needs this too -- PublicIncludePaths only advertises the path to a consumer's
        // ProjectReference, it doesn't add itself to this project's own compile) + each direct
        // ProjectReference's exported PublicIncludePaths (same non-propagation reason).
        var includeDirs = new List<string>();
        includeDirs.AddRange((m.IncludePaths ?? new()).Select(p => p.Replace('/', '\\')));
        includeDirs.AddRange((m.PublicIncludePaths ?? new()).Select(p => p.Replace('/', '\\')));
        foreach (var (_, _, publicInc) in nativeRefs)
            if (!string.IsNullOrEmpty(publicInc))
                includeDirs.Add(publicInc);
        Add("RxdkAdditionalIncludeDirectories", string.Join(";", includeDirs.Distinct()));

        Add("PreprocessorDefinitions", Join(m.Defines));
        if (!string.IsNullOrWhiteSpace(m.CppStandard) && !string.Equals(m.CppStandard.Trim(), "c++23", StringComparison.OrdinalIgnoreCase))
            Add("CppLanguageStandard", m.CppStandard!.Trim());
        if (m.CompileFlags is { Count: > 0 })
            Add("AdditionalOptions", string.Join(" ", m.CompileFlags));

        return meta;
    }

    // ImageBld: only for Application/DebuggerExtension projects, only fields that differ from
    // Rxdk.MsBuild.props' own ImageBld ItemDefinitionGroup defaults.
    private static readonly (int StackSize, bool Debug, bool NoLogo, bool NoLibWarn, bool LimitMemory,
        bool DontModifyHardDisk, bool DontMountUtilityDrive, bool FormatUtilityDrive) ImgDefault =
        (65536, true, true, true, false, false, false, false);

    private static Dictionary<string, string> ImageBldMeta(RxdkProjectManifest m)
    {
        var meta = new Dictionary<string, string>();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) meta[k] = v!; }
        var ib = m.ImageBuild;
        if (ib == null) return meta;

        if (ib.StackSize.HasValue && ib.StackSize != ImgDefault.StackSize) Add("StackSize", ib.StackSize.Value.ToString(CultureInfo.InvariantCulture));
        if (ib.Debug.HasValue && ib.Debug != ImgDefault.Debug) Add("Debug", ib.Debug.Value ? "true" : "false");
        if (ib.NoLogo.HasValue && ib.NoLogo != ImgDefault.NoLogo) Add("NoLogo", ib.NoLogo.Value ? "true" : "false");
        if (ib.NoLibWarn.HasValue && ib.NoLibWarn != ImgDefault.NoLibWarn) Add("NoLibWarn", ib.NoLibWarn.Value ? "true" : "false");
        if (ib.LimitMemory.HasValue && ib.LimitMemory != ImgDefault.LimitMemory) Add("LimitMemory", ib.LimitMemory.Value ? "true" : "false");
        if (ib.DontModifyHardDisk.HasValue && ib.DontModifyHardDisk != ImgDefault.DontModifyHardDisk) Add("DontModifyHardDisk", ib.DontModifyHardDisk.Value ? "true" : "false");
        if (ib.DontMountUtilityDrive.HasValue && ib.DontMountUtilityDrive != ImgDefault.DontMountUtilityDrive) Add("DontMountUtilityDrive", ib.DontMountUtilityDrive.Value ? "true" : "false");
        if (ib.FormatUtilityDrive.HasValue && ib.FormatUtilityDrive != ImgDefault.FormatUtilityDrive) Add("FormatUtilityDrive", ib.FormatUtilityDrive.Value ? "true" : "false");
        if (ib.UtilityDriveClusterSize is > 0) Add("UtilityDriveClusterSize", ib.UtilityDriveClusterSize!.Value.ToString(CultureInfo.InvariantCulture));
        if (ib.NoPreload is { Count: > 0 }) Add("NoPreload", string.Join(";", ib.NoPreload));
        Add("TestId", ib.TestId);
        Add("TestAltId", ib.TestAltId);
        Add("TestRegion", ib.TestRegion);
        Add("TestRatings", ib.TestRatings);
        Add("TestMediaTypes", ib.TestMediaTypes);
        Add("TestLanKey", ib.TestLanKey);
        Add("TestSignKey", ib.TestSignKey);
        Add("TestName", ib.TestName);
        Add("TestVersion", ib.TestVersion);
        Add("TitleInfo", ib.TitleInfo?.Replace('/', '\\'));
        Add("TitleImage", ib.TitleImage?.Replace('/', '\\'));
        Add("DefaultSaveImage", ib.DefaultSaveImage?.Replace('/', '\\'));
        return meta;
    }

    // ---- .vcxproj text ----

    private static string BuildVcxproj(
        string name, string projectGuid, RxdkProjectManifest defaultManifest, List<string> configNames,
        Dictionary<string, Dictionary<string, string>> perConfigLink,
        Dictionary<string, Dictionary<string, string>> perConfigCompile,
        Dictionary<string, Dictionary<string, string>> perConfigImageBld,
        List<(string RelPath, string Guid, string PublicIncludeDir)> nativeRefs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        sb.AppendLine("  <ItemGroup Label=\"ProjectConfigurations\">");
        foreach (var c in configNames)
        {
            sb.AppendLine($"    <ProjectConfiguration Include=\"{Esc(c)}|Xbox\">");
            sb.AppendLine($"      <Configuration>{Esc(c)}</Configuration>");
            sb.AppendLine("      <Platform>Xbox</Platform>");
            sb.AppendLine("    </ProjectConfiguration>");
        }
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("  <PropertyGroup Label=\"Globals\">");
        sb.AppendLine("    <VCProjectVersion>16.0</VCProjectVersion>");
        sb.AppendLine($"    <ProjectGuid>{projectGuid}</ProjectGuid>");
        sb.AppendLine("    <Keyword>RXDK</Keyword>");
        sb.AppendLine("    <MinimumVisualStudioVersion>15.0</MinimumVisualStudioVersion>");
        sb.AppendLine("    <ApplicationType>RXDK</ApplicationType>");
        sb.AppendLine("    <ApplicationTypeRevision>Current</ApplicationTypeRevision>");
        sb.AppendLine("    <RootNamespace>XboxNamespace</RootNamespace>");
        sb.AppendLine("    <WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>");
        sb.AppendLine($"    <ProjectName>{Esc(name)}</ProjectName>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.Default.props\" />");

        var configurationType = defaultManifest.IsDxt ? "DebuggerExtension" : defaultManifest.IsLibrary ? "StaticLibrary" : "Application";
        foreach (var c in configNames)
        {
            var isDebug = c.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0;
            sb.AppendLine($"  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='{Esc(c)}|Xbox'\" Label=\"Configuration\">");
            sb.AppendLine($"    <UseDebugLibraries>{(isDebug ? "true" : "false")}</UseDebugLibraries>");
            sb.AppendLine("    <PlatformToolset>RXDK</PlatformToolset>");
            sb.AppendLine($"    <ConfigurationType>{configurationType}</ConfigurationType>");
            sb.AppendLine("  </PropertyGroup>");
        }
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.props\" />");
        sb.AppendLine("  <ImportGroup Label=\"ExtensionSettings\" />");
        sb.AppendLine("  <ImportGroup Label=\"Shared\" />");
        sb.AppendLine("  <ImportGroup Label=\"PropertySheets\" />");
        sb.AppendLine("  <PropertyGroup Label=\"UserMacros\" />");

        if (defaultManifest.PublicIncludePaths is { Count: > 0 })
        {
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine($"    <PublicIncludePaths>{Esc(Join(defaultManifest.PublicIncludePaths))}</PublicIncludePaths>");
            sb.AppendLine("  </PropertyGroup>");
        }

        WriteHoistedItemDefinitionGroups(sb, "Link", configNames, perConfigLink);
        WriteHoistedItemDefinitionGroups(sb, "ClCompile", configNames, perConfigCompile);
        if (perConfigImageBld.Count > 0)
            WriteHoistedItemDefinitionGroups(sb, "ImageBld", configNames, perConfigImageBld);

        if (defaultManifest.Sources is { Count: > 0 })
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var s in defaultManifest.Sources) sb.AppendLine($"    <ClCompile Include=\"{Esc(s.Replace('/', '\\'))}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        if (defaultManifest.Resources is { Count: > 0 })
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var r in defaultManifest.Resources) sb.AppendLine($"    <None Include=\"{Esc(r.Replace('/', '\\'))}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        if (defaultManifest.Embed is { Count: > 0 })
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var e in defaultManifest.Embed)
            {
                sb.AppendLine($"    <RxdkEmbed Include=\"{Esc(e.Path.Replace('/', '\\'))}\">");
                sb.AppendLine($"      <Name>{Esc(e.Name)}</Name>");
                sb.AppendLine("    </RxdkEmbed>");
            }
            sb.AppendLine("  </ItemGroup>");
        }
        if (defaultManifest.DeployPaths is { Count: > 0 })
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var d in defaultManifest.DeployPaths) sb.AppendLine($"    <IsoCopy Include=\"{Esc(d.Replace('/', '\\'))}\\**\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        if (nativeRefs.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var (relPath, guid, _) in nativeRefs)
            {
                sb.AppendLine($"    <ProjectReference Include=\"{Esc(relPath.Replace('/', '\\'))}\">");
                sb.AppendLine($"      <Project>{guid}</Project>");
                sb.AppendLine("    </ProjectReference>");
            }
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine("  <Import Project=\"$(VCTargetsPath)\\Microsoft.Cpp.targets\" />");
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    // Hoists a metadata field identical across every config into one unconditioned
    // ItemDefinitionGroup (mirrors what a hand-authored template does when a value doesn't vary by
    // configuration); anything left over goes into that config's own conditioned block.
    private static void WriteHoistedItemDefinitionGroups(
        StringBuilder sb, string itemType, List<string> configNames, Dictionary<string, Dictionary<string, string>> perConfig)
    {
        var allKeys = perConfig.Values.SelectMany(d => d.Keys).Distinct().ToList();
        var common = new Dictionary<string, string>();
        foreach (var key in allKeys)
        {
            var values = configNames.Select(c => perConfig[c].TryGetValue(key, out var v) ? v : null).ToList();
            if (values.All(v => v != null) && values.Distinct().Count() == 1)
                common[key] = values[0]!;
        }

        void Write(Dictionary<string, string> fields, string? condition)
        {
            if (fields.Count == 0) return;
            sb.AppendLine(condition is null
                ? "  <ItemDefinitionGroup>"
                : $"  <ItemDefinitionGroup Condition=\"'$(Configuration)|$(Platform)'=='{Esc(condition)}|Xbox'\">");
            sb.AppendLine($"    <{itemType}>");
            foreach (var (k, v) in fields) sb.AppendLine($"      <{k}>{Esc(v)}</{k}>");
            sb.AppendLine($"    </{itemType}>");
            sb.AppendLine("  </ItemDefinitionGroup>");
        }

        Write(common, null);
        foreach (var c in configNames)
        {
            var perCfg = perConfig[c].Where(kv => !common.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            Write(perCfg, c);
        }
    }

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;
}
