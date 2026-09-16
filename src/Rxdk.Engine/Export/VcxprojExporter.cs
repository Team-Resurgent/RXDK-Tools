using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Rxdk.Engine.Import;
using Rxdk.Engine.Model;

namespace Rxdk.Engine.Export;

/// <summary>
/// The reverse of the RxdkGenerateProjectJson MSBuild target (RXDK-VS20XX's Platform.targets):
/// generates a <c>.vcxproj</c> (+ single-project <c>.sln</c>) FROM an existing <c>rxdk.project.json</c>,
/// so a project created the VS Code / Open Folder way -- which has no MSBuild project at all -- can be
/// opened in Visual Studio too. Field names mirror the RxdkXxx MSBuild properties _RxdkCollectConfig /
/// RxdkWriteProjectJson read and write; a field is only emitted where its value differs from the
/// Platform.props default, exactly like the hand-authored project templates (see TemplateSrc/Dxt).
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

        var perConfigFields = configNames.ToDictionary(
            c => c,
            c => Fields(manifest.ResolveConfiguration(c), c, projectRoot, manifest.ConfigurationNames.Count > 0, result.Warnings));
        var perConfigItemMeta = configNames.ToDictionary(
            c => c,
            c => ItemMetadata(manifest.ResolveConfiguration(c)));

        var defaultCfg = !string.IsNullOrEmpty(manifest.DefaultConfiguration) && configNames.Contains(manifest.DefaultConfiguration!)
            ? manifest.DefaultConfiguration!
            : configNames[0];
        var defaultManifest = manifest.ResolveConfiguration(defaultCfg);

        var (nativeRefs, extraRefs) = ResolveProjectReferences(defaultManifest.ProjectReferences, projectRoot, result.Warnings);

        result.VcxprojPath = Path.Combine(projectRoot, name + ".vcxproj");
        File.WriteAllText(
            result.VcxprojPath,
            BuildVcxproj(name, result.ProjectGuid, defaultManifest, configNames, perConfigFields, perConfigItemMeta, nativeRefs, extraRefs),
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

    // Native <ProjectReference> for deps that already have a .vcxproj (build-order + IDE navigation);
    // everything else falls back into the RxdkProjectReferences string list the manifest-driven link
    // already resolves by folder, so a dependency that hasn't been exported yet still links.
    private static (List<(string RelPath, string Guid)> Native, List<string> Extra) ResolveProjectReferences(
        List<string>? refs, string projectRoot, List<string> warnings)
    {
        var native = new List<(string, string)>();
        var extra = new List<string>();
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
                if (guid != null)
                {
                    native.Add((Path.GetRelativePath(projectRoot, depVcxproj), guid));
                    continue;
                }
            }
            // No .vcxproj yet (or its GUID couldn't be read) -- keep it as a manifest-style reference.
            extra.Add(rel.Replace('\\', '/'));
            if (depVcxproj == null)
                warnings.Add($"projectReference \"{rel}\" has no .vcxproj yet -- run Import VSCode Project on it too, then re-import this project to pick up build ordering in Visual Studio.");
        }
        return (native, extra);
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

    // ---- per-config RxdkXxx property values (the reverse of Platform.targets' fields() JS task) ----

    // Platform.props defaults; a field equal to its default is omitted so the generated vcxproj reads
    // like a hand-authored template rather than restating every default explicitly.
    private static readonly RxdkImageBuildOptions ImgDefault = new()
    {
        StackSize = 65536, Debug = true, NoLogo = true, NoLibWarn = true,
        LimitMemory = false, DontModifyHardDisk = false, DontMountUtilityDrive = false,
        FormatUtilityDrive = false, UtilityDriveClusterSize = 0,
    };

    private static Dictionary<string, string> Fields(
        RxdkProjectManifest m, string cfgName, string projectRoot, bool isMultiConfig, List<string> warnings)
    {
        var f = new Dictionary<string, string>();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) f[k] = v!; }
        string Join(IEnumerable<string>? xs) => string.Join(";", (xs ?? Enumerable.Empty<string>()).Select(x => x.Replace('/', '\\')));

        if (m.Type is RxdkProjectKind.Library) Add("RxdkType", "library");
        else if (m.Type is RxdkProjectKind.Dxt) Add("RxdkType", "dxt");

        // Only pin RxdkConfig when the manifest is explicit about it -- otherwise let Platform.props
        // derive debug/release from $(Configuration), which is what a VS user toggling the
        // Configuration dropdown expects.
        if (m.Configuration.HasValue)
            Add("RxdkConfig", m.EffectiveConfiguration.ToString().ToLowerInvariant());

        Add("RxdkAdditionalLibraries", Join(m.AdditionalLibraries));
        Add("RxdkPublicIncludePaths", Join(m.PublicIncludePaths));
        if (!string.IsNullOrWhiteSpace(m.CppStandard)) Add("RxdkCppStandard", m.CppStandard!.Trim());
        if (m.Exceptions.HasValue) Add("RxdkExceptions", m.Exceptions.Value ? "true" : "false");
        if (m.Incremental.HasValue) Add("RxdkIncrementalBuild", m.Incremental.Value ? "true" : "false");
        Add("RxdkDeployPaths", Join(m.DeployPaths));
        if (m.Embed is { Count: > 0 })
            Add("RxdkEmbed", string.Join(";", m.Embed.Select(e => $"{e.Path.Replace('/', '\\')}|{e.Name}")));
        if (m.CreateIso.HasValue && m.CreateIso.Value != true) Add("RxdkCreateIso", "false");
        if (m.ForceCopy.HasValue && m.ForceCopy.Value != false) Add("RxdkForceCopy", "true");

        var ib = m.ImageBuild;
        if (ib != null)
        {
            if (ib.StackSize.HasValue && ib.StackSize != ImgDefault.StackSize) Add("RxdkStackSize", ib.StackSize.Value.ToString(CultureInfo.InvariantCulture));
            if (ib.Debug.HasValue && ib.Debug != ImgDefault.Debug) Add("RxdkImageDebug", ib.Debug.Value ? "true" : "false");
            if (ib.NoLogo.HasValue && ib.NoLogo != ImgDefault.NoLogo) Add("RxdkNoLogo", ib.NoLogo.Value ? "true" : "false");
            if (ib.NoLibWarn.HasValue && ib.NoLibWarn != ImgDefault.NoLibWarn) Add("RxdkNoLibWarn", ib.NoLibWarn.Value ? "true" : "false");
            if (ib.LimitMemory.HasValue && ib.LimitMemory != ImgDefault.LimitMemory) Add("RxdkLimitMemory", ib.LimitMemory.Value ? "true" : "false");
            if (ib.DontModifyHardDisk.HasValue && ib.DontModifyHardDisk != ImgDefault.DontModifyHardDisk) Add("RxdkDontModifyHardDisk", ib.DontModifyHardDisk.Value ? "true" : "false");
            if (ib.DontMountUtilityDrive.HasValue && ib.DontMountUtilityDrive != ImgDefault.DontMountUtilityDrive) Add("RxdkDontMountUtilityDrive", ib.DontMountUtilityDrive.Value ? "true" : "false");
            if (ib.FormatUtilityDrive.HasValue && ib.FormatUtilityDrive != ImgDefault.FormatUtilityDrive) Add("RxdkFormatUtilityDrive", ib.FormatUtilityDrive.Value ? "true" : "false");
            if (ib.UtilityDriveClusterSize.HasValue && ib.UtilityDriveClusterSize != 0) Add("RxdkUtilityDriveClusterSize", ib.UtilityDriveClusterSize.Value.ToString(CultureInfo.InvariantCulture));
            if (ib.NoPreload is { Count: > 0 }) Add("RxdkNoPreload", string.Join(";", ib.NoPreload));
            Add("RxdkTestId", ib.TestId);
            Add("RxdkTestAltId", ib.TestAltId);
            Add("RxdkTestRegion", ib.TestRegion);
            Add("RxdkTestRatings", ib.TestRatings);
            Add("RxdkTestMediaTypes", ib.TestMediaTypes);
            Add("RxdkTestLanKey", ib.TestLanKey);
            Add("RxdkTestSignKey", ib.TestSignKey);
            Add("RxdkTestName", ib.TestName);
            Add("RxdkTestVersion", ib.TestVersion);
            Add("RxdkTitleInfo", ib.TitleInfo?.Replace('/', '\\'));
            Add("RxdkTitleImage", ib.TitleImage?.Replace('/', '\\'));
            Add("RxdkDefaultSaveImage", ib.DefaultSaveImage?.Replace('/', '\\'));
        }

        // Platform.props defaults RxdkOutDir to out\$(Configuration); omit when the manifest already
        // says exactly that (true for every manifest RxdkGenerateProjectJson itself writes).
        var outDir = m.OutputDir?.Trim().TrimEnd('/', '\\');
        if (!string.IsNullOrEmpty(outDir) &&
            !string.Equals(outDir.Replace('\\', '/'), $"out/{cfgName}", StringComparison.OrdinalIgnoreCase))
        {
            Add("RxdkOutDir", outDir!.Replace('/', '\\'));
        }

        // compileFlags has no MSBuild-property equivalent yet (RxdkGenerateProjectJson doesn't read one
        // back either), so it can't round-trip into the vcxproj. Flag it once so it isn't silently lost.
        if (m.CompileFlags is { Count: > 0 } && !warnings.Any(w => w.StartsWith("compileFlags")))
            warnings.Add($"compileFlags {(isMultiConfig ? $"in configuration \"{cfgName}\" " : "")}has no VS20XX equivalent yet and was not exported -- add it back via the project's property pages, or keep building this project from VS Code.");

        return f;
    }

    // Libraries/LibraryPaths/IncludePaths/Defines persist as real VC++ item metadata
    // (Link.AdditionalDependencies / Link.AdditionalLibraryDirectories /
    // ClCompile.AdditionalIncludeDirectories / ClCompile.PreprocessorDefinitions), the reverse of
    // Platform.props' _RxdkCollectConfig, which now reads them the same way -- not the flat
    // RxdkLibraries-style properties Fields() above writes. Outer key is the item type
    // ("Link"/"ClCompile"), inner is metadata name -> value.
    private static Dictionary<string, Dictionary<string, string>> ItemMetadata(RxdkProjectManifest m)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        void AddMeta(string itemType, string metaName, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!result.TryGetValue(itemType, out var inner)) result[itemType] = inner = new();
            inner[metaName] = value;
        }
        string Join(IEnumerable<string>? xs) => string.Join(";", (xs ?? Enumerable.Empty<string>()).Select(x => x.Replace('/', '\\')));

        AddMeta("Link", "AdditionalDependencies", Join(m.Libraries));
        AddMeta("Link", "AdditionalLibraryDirectories", Join(m.LibraryPaths));
        AddMeta("ClCompile", "AdditionalIncludeDirectories", Join(m.IncludePaths));
        AddMeta("ClCompile", "PreprocessorDefinitions", Join(m.Defines));
        return result;
    }

    // ---- .vcxproj text ----

    private static string BuildVcxproj(
        string name, string projectGuid, RxdkProjectManifest defaultManifest, List<string> configNames,
        Dictionary<string, Dictionary<string, string>> perConfigFields,
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> perConfigItemMeta,
        List<(string RelPath, string Guid)> nativeRefs, List<string> extraRefs)
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

        // Hoist fields identical across every config into one unconditioned PropertyGroup (mirrors
        // RxdkWriteProjectJson's "common" logic, just for MSBuild properties instead of JSON keys).
        var allKeys = perConfigFields.Values.SelectMany(d => d.Keys).Distinct().ToList();
        var common = new Dictionary<string, string>();
        foreach (var key in allKeys)
        {
            var values = configNames.Select(c => perConfigFields[c].TryGetValue(key, out var v) ? v : null).ToList();
            if (values.All(v => v != null) && values.Distinct().Count() == 1)
                common[key] = values[0]!;
        }

        var ext = defaultManifest.IsDxt ? "dxt" : defaultManifest.IsLibrary ? "lib" : "xbe";
        sb.AppendLine("  <PropertyGroup>");
        foreach (var (k, v) in common) sb.AppendLine($"    <{k}>{Esc(v)}</{k}>");
        if (extraRefs.Count > 0) sb.AppendLine($"    <RxdkProjectReferences>{Esc(string.Join(";", extraRefs))}</RxdkProjectReferences>");
        sb.AppendLine($"    <NMakeOutput>$(MSBuildProjectDirectory)\\$(RxdkOutDir)\\$(MSBuildProjectName).{ext}</NMakeOutput>");
        sb.AppendLine("  </PropertyGroup>");

        foreach (var c in configNames)
        {
            var perCfg = perConfigFields[c].Where(kv => !common.ContainsKey(kv.Key)).ToList();
            if (perCfg.Count == 0) continue;
            sb.AppendLine($"  <PropertyGroup Condition=\"'$(Configuration)|$(Platform)'=='{Esc(c)}|Xbox'\">");
            foreach (var (k, v) in perCfg) sb.AppendLine($"    <{k}>{Esc(v)}</{k}>");
            sb.AppendLine("  </PropertyGroup>");
        }

        // Libraries/LibraryPaths/IncludePaths/Defines as real VC++ item metadata (see ItemMetadata),
        // hoisting fields identical across every config the same way the flat properties above do.
        var itemTypes = perConfigItemMeta.Values.SelectMany(d => d.Keys).Distinct().ToList();
        var commonMeta = new Dictionary<string, Dictionary<string, string>>();
        foreach (var itemType in itemTypes)
        {
            var metaNames = perConfigItemMeta.Values
                .Select(d => d.TryGetValue(itemType, out var inner) ? inner.Keys : Enumerable.Empty<string>())
                .SelectMany(k => k).Distinct().ToList();
            foreach (var metaName in metaNames)
            {
                string? Get(string c) => perConfigItemMeta[c].TryGetValue(itemType, out var inner) && inner.TryGetValue(metaName, out var v) ? v : null;
                var values = configNames.Select(Get).ToList();
                if (values.All(v => v != null) && values.Distinct().Count() == 1)
                {
                    if (!commonMeta.TryGetValue(itemType, out var outInner)) commonMeta[itemType] = outInner = new();
                    outInner[metaName] = values[0]!;
                }
            }
        }

        void WriteItemDefinitionGroup(Dictionary<string, Dictionary<string, string>> meta, string? condition)
        {
            if (meta.Count == 0) return;
            sb.AppendLine(condition is null
                ? "  <ItemDefinitionGroup>"
                : $"  <ItemDefinitionGroup Condition=\"'$(Configuration)|$(Platform)'=='{Esc(condition)}|Xbox'\">");
            foreach (var (itemType, inner) in meta)
            {
                sb.AppendLine($"    <{itemType}>");
                foreach (var (metaName, value) in inner) sb.AppendLine($"      <{metaName}>{Esc(value)}</{metaName}>");
                sb.AppendLine($"    </{itemType}>");
            }
            sb.AppendLine("  </ItemDefinitionGroup>");
        }

        WriteItemDefinitionGroup(commonMeta, null);
        foreach (var c in configNames)
        {
            var perCfgMeta = new Dictionary<string, Dictionary<string, string>>();
            foreach (var itemType in itemTypes)
            {
                if (!perConfigItemMeta[c].TryGetValue(itemType, out var inner)) continue;
                var commonInner = commonMeta.TryGetValue(itemType, out var ci) ? ci : new Dictionary<string, string>();
                var remaining = inner.Where(kv => !commonInner.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
                if (remaining.Count > 0) perCfgMeta[itemType] = remaining;
            }
            WriteItemDefinitionGroup(perCfgMeta, c);
        }

        if (defaultManifest.Sources is { Count: > 0 })
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var s in defaultManifest.Sources) sb.AppendLine($"    <ClCompile Include=\"{Esc(s.Replace('/', '\\'))}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        var resources = defaultManifest.Resources is { Count: > 0 } ? defaultManifest.Resources : null;
        if (resources != null)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var r in resources) sb.AppendLine($"    <None Include=\"{Esc(r.Replace('/', '\\'))}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        if (nativeRefs.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var (relPath, guid) in nativeRefs)
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

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;
}
