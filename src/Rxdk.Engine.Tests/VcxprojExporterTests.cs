using System.Text.Json;
using System.Xml.Linq;
using Rxdk.Engine.Export;
using Rxdk.Engine.Model;
using Xunit;

namespace Rxdk.Engine.Tests;

/// <summary>
/// VcxprojExporter is the reverse of Platform.props' RxdkGenerateProjectJson MSBuild target: it
/// writes a .vcxproj FROM an rxdk.project.json, so a project created the VS Code / Open Folder way
/// can be opened in Visual Studio ("Import VSCode Project"). This file exercises every field the
/// manifest schema supports, so a field silently dropped (or written to a container Platform.props
/// no longer reads -- the exact bug this suite was written after: RxdkLibraries kept being written
/// as a flat property after the engine switched to reading Link.AdditionalDependencies item
/// metadata, so an imported project would silently lose every library) fails a test instead of
/// only surfacing on a real "Import VSCode Project" click.
///
/// The reverse direction (.vcxproj -> rxdk.project.json, RxdkGenerateProjectJson) lives entirely in
/// Platform.props as an inline RoslynCodeTaskFactory target, not in this C# codebase, and needs a
/// real MSBuild.exe to run -- it is NOT exercised here. These tests only guarantee the export half:
/// every manifest field lands in the .vcxproj under the name/container the MSBuild side actually
/// reads (see Platform.props' _RxdkCollectConfig for the reader).
/// </summary>
public sealed class VcxprojExporterTests
{
    // Every field RxdkProjectManifest / RxdkImageBuildOptions supports, non-default where a default
    // exists, so a field that round-trips to its own default value can't hide a bug.
    private static RxdkProjectManifest FullManifest() => new()
    {
        Name = "FullFieldsSample",
        Type = RxdkProjectKind.Executable,
        Configuration = RxdkConfiguration.Debug,
        Sources = new() { "src/main.cpp", "src/helper.c" },
        Libraries = new() { "libd3d8d.lib", "libxapid.lib", "libcd.lib", "libcppd.lib", "libkerneld.lib", "libcompatd.lib" },
        Resources = new() { "font.rdf", "gamepad.rdf" },
        LibraryPaths = new() { "vendor/lib" },
        AdditionalLibraries = new() { "vendor/prebuilt/thirdparty.lib" },
        ProjectReferences = new() { "../SharedLib" },
        OutputDir = "out/Custom",
        DeployPaths = new() { "Media" },
        Embed = new() { new RxdkEmbedFile { Path = "assets/icon.xpr", Name = "IconXpr" } },
        CreateIso = false,
        ForceCopy = true,
        ImageBuild = new RxdkImageBuildOptions
        {
            StackSize = 131072,
            Debug = false,
            NoLogo = false,
            NoLibWarn = false,
            LimitMemory = true,
            DontModifyHardDisk = true,
            DontMountUtilityDrive = true,
            FormatUtilityDrive = true,
            UtilityDriveClusterSize = 32768,
            NoPreload = new() { "Audio", "Video" },
            TestId = "0xffff1234",
            TestAltId = "0xffff5678,00112233",
            TestRegion = "7",
            TestRatings = "1",
            TestMediaTypes = "0xffffffff",
            TestLanKey = "AABBCCDD",
            TestSignKey = "EEFF0011",
            TestName = "Full Fields Sample",
            TestVersion = "4096",
            TitleInfo = "titleinfo.bin",
            TitleImage = "titleimage.xpr",
            DefaultSaveImage = "saveimage.xpr",
        },
        IncludePaths = new() { "src/include", "vendor/include" },
        PublicIncludePaths = new() { "include" },
        Defines = new() { "MY_FLAG", "NAME=VALUE" },
        CompileFlags = new() { "-mno-ms-bitfields" },
        CppStandard = "c++20",
        Exceptions = false,
        Incremental = false,
    };

    private static (VcxprojExporter.ExportResult Result, XDocument Doc, string ProjectRoot) ExportFullManifest()
    {
        var projectRoot = Directory.CreateTempSubdirectory("rxdk-vcxproj-export-test-").FullName;
        try
        {
            var manifest = FullManifest();
            var json = JsonSerializer.Serialize(manifest, RxdkManifestLoader.JsonOptions);
            File.WriteAllText(Path.Combine(projectRoot, RxdkManifestLoader.ManifestFileName), json);

            var result = VcxprojExporter.Export(projectRoot);
            var doc = XDocument.Load(result.VcxprojPath);
            return (result, doc, projectRoot);
        }
        catch
        {
            Directory.Delete(projectRoot, recursive: true);
            throw;
        }
    }

    // Item metadata (Link.AdditionalDependencies etc.) lives inside possibly-multiple
    // ItemDefinitionGroup elements (a common/unconditioned one plus per-config ones); a flat
    // manifest hoists everything into the single unconditioned block, so concatenating every
    // ItemDefinitionGroup's values for one item-type/metadata pair is safe here.
    private static string? ItemMetaValue(XDocument doc, string itemType, string metaName)
    {
        XNamespace ns = doc.Root!.GetDefaultNamespace();
        var values = doc.Root!.Elements(ns + "ItemDefinitionGroup")
            .Elements(ns + itemType)
            .Elements(ns + metaName)
            .Select(e => e.Value)
            .ToList();
        return values.Count == 0 ? null : string.Join(";", values);
    }

    private static string? FlatPropertyValue(XDocument doc, string name)
    {
        XNamespace ns = doc.Root!.GetDefaultNamespace();
        return doc.Root!.Elements(ns + "PropertyGroup").Elements(ns + name).FirstOrDefault()?.Value;
    }

    [Fact]
    public void Libraries_export_as_LinkAdditionalDependencies_with_extension_verbatim()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            // Exact join, in order, extensions untouched -- the engine must never append/strip ".lib"
            // or a "d" suffix; the manifest already names the precise file it wants.
            Assert.Equal(
                "libd3d8d.lib;libxapid.lib;libcd.lib;libcppd.lib;libkerneld.lib;libcompatd.lib",
                ItemMetaValue(doc, "Link", "AdditionalDependencies"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void LibraryPaths_export_as_LinkAdditionalLibraryDirectories()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("vendor\\lib", ItemMetaValue(doc, "Link", "AdditionalLibraryDirectories"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void IncludePaths_export_as_ClCompileAdditionalIncludeDirectories()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("src\\include;vendor\\include", ItemMetaValue(doc, "ClCompile", "AdditionalIncludeDirectories"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Defines_export_as_ClCompilePreprocessorDefinitions()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("MY_FLAG;NAME=VALUE", ItemMetaValue(doc, "ClCompile", "PreprocessorDefinitions"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void AdditionalLibraries_and_PublicIncludePaths_stay_flat_Rxdk_properties()
    {
        // These two have no real VC++ item-metadata equivalent (verbatim prebuilt-lib paths and
        // RXDK's own multi-project "public include" concept), so they must stay as plain
        // Rxdk*-prefixed properties, not move into an ItemDefinitionGroup.
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("vendor\\prebuilt\\thirdparty.lib", FlatPropertyValue(doc, "RxdkAdditionalLibraries"));
            Assert.Equal("include", FlatPropertyValue(doc, "RxdkPublicIncludePaths"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void ImageBuild_fields_export_to_matching_Rxdk_properties()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("131072", FlatPropertyValue(doc, "RxdkStackSize"));
            Assert.Equal("false", FlatPropertyValue(doc, "RxdkImageDebug"));
            Assert.Equal("false", FlatPropertyValue(doc, "RxdkNoLogo"));
            Assert.Equal("false", FlatPropertyValue(doc, "RxdkNoLibWarn"));
            Assert.Equal("true", FlatPropertyValue(doc, "RxdkLimitMemory"));
            Assert.Equal("true", FlatPropertyValue(doc, "RxdkDontModifyHardDisk"));
            Assert.Equal("true", FlatPropertyValue(doc, "RxdkDontMountUtilityDrive"));
            Assert.Equal("true", FlatPropertyValue(doc, "RxdkFormatUtilityDrive"));
            Assert.Equal("32768", FlatPropertyValue(doc, "RxdkUtilityDriveClusterSize"));
            Assert.Equal("Audio;Video", FlatPropertyValue(doc, "RxdkNoPreload"));
            Assert.Equal("0xffff1234", FlatPropertyValue(doc, "RxdkTestId"));
            Assert.Equal("0xffff5678,00112233", FlatPropertyValue(doc, "RxdkTestAltId"));
            Assert.Equal("7", FlatPropertyValue(doc, "RxdkTestRegion"));
            Assert.Equal("1", FlatPropertyValue(doc, "RxdkTestRatings"));
            Assert.Equal("0xffffffff", FlatPropertyValue(doc, "RxdkTestMediaTypes"));
            Assert.Equal("AABBCCDD", FlatPropertyValue(doc, "RxdkTestLanKey"));
            Assert.Equal("EEFF0011", FlatPropertyValue(doc, "RxdkTestSignKey"));
            Assert.Equal("Full Fields Sample", FlatPropertyValue(doc, "RxdkTestName"));
            Assert.Equal("4096", FlatPropertyValue(doc, "RxdkTestVersion"));
            Assert.Equal("titleinfo.bin", FlatPropertyValue(doc, "RxdkTitleInfo"));
            Assert.Equal("titleimage.xpr", FlatPropertyValue(doc, "RxdkTitleImage"));
            Assert.Equal("saveimage.xpr", FlatPropertyValue(doc, "RxdkDefaultSaveImage"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Remaining_scalar_and_list_fields_export_correctly()
    {
        var (result, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("debug", FlatPropertyValue(doc, "RxdkConfig"));
            Assert.Equal("Media", FlatPropertyValue(doc, "RxdkDeployPaths"));
            Assert.Equal("assets\\icon.xpr|IconXpr", FlatPropertyValue(doc, "RxdkEmbed"));
            Assert.Equal("false", FlatPropertyValue(doc, "RxdkCreateIso"));
            Assert.Equal("true", FlatPropertyValue(doc, "RxdkForceCopy"));
            Assert.Equal("c++20", FlatPropertyValue(doc, "RxdkCppStandard"));
            Assert.Equal("false", FlatPropertyValue(doc, "RxdkExceptions"));
            Assert.Equal("false", FlatPropertyValue(doc, "RxdkIncrementalBuild"));
            Assert.Equal("out\\Custom", FlatPropertyValue(doc, "RxdkOutDir"));

            // No RxdkGenerateProjectJson-side property reads compileFlags back (see VcxprojExporter's
            // own comment) -- it can't round-trip, so the exporter must not silently drop it: it has
            // to surface as a warning instead.
            Assert.Contains(result.Warnings, w => w.StartsWith("compileFlags"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Sources_and_resources_export_as_ClCompile_and_None_items()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            XNamespace ns = doc.Root!.GetDefaultNamespace();
            var sources = doc.Root!.Elements(ns + "ItemGroup").Elements(ns + "ClCompile")
                .Select(e => e.Attribute("Include")!.Value).ToList();
            Assert.Equal(new[] { "src\\main.cpp", "src\\helper.c" }, sources);

            var resources = doc.Root!.Elements(ns + "ItemGroup").Elements(ns + "None")
                .Select(e => e.Attribute("Include")!.Value).ToList();
            Assert.Equal(new[] { "font.rdf", "gamepad.rdf" }, resources);
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Unresolved_project_reference_falls_back_to_RxdkProjectReferences_with_a_warning()
    {
        // "../SharedLib" (FullManifest) has no .vcxproj on disk next to this temp project, so it
        // can't become a native <ProjectReference> -- it must still reach the build as a manifest-
        // style reference (RxdkProjectReferences), not silently vanish, with a warning explaining why.
        var (result, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("../SharedLib", FlatPropertyValue(doc, "RxdkProjectReferences"));
            Assert.Contains(result.Warnings, w => w.Contains("../SharedLib"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Theory]
    [InlineData(RxdkProjectKind.Library, "library")]
    [InlineData(RxdkProjectKind.Dxt, "dxt")]
    [InlineData(RxdkProjectKind.Executable, null)] // Executable is the Platform.props default: omitted, not written
    public void Type_exports_only_for_non_default_kinds(RxdkProjectKind kind, string? expected)
    {
        var projectRoot = Directory.CreateTempSubdirectory("rxdk-vcxproj-export-test-").FullName;
        try
        {
            var manifest = new RxdkProjectManifest
            {
                Name = "KindSample",
                Type = kind,
                Sources = new() { "src/main.cpp" },
            };
            var json = JsonSerializer.Serialize(manifest, RxdkManifestLoader.JsonOptions);
            File.WriteAllText(Path.Combine(projectRoot, RxdkManifestLoader.ManifestFileName), json);

            var result = VcxprojExporter.Export(projectRoot);
            var doc = XDocument.Load(result.VcxprojPath);
            Assert.Equal(expected, FlatPropertyValue(doc, "RxdkType"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }
}
