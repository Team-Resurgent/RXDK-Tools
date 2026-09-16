using System.Text.Json;
using System.Xml.Linq;
using Rxdk.Engine.Export;
using Rxdk.Engine.Model;
using Xunit;

namespace Rxdk.Engine.Tests;

/// <summary>
/// VcxprojExporter writes a native Rxdk.MsBuild .vcxproj (ApplicationType=RXDK, real ClCompile/Link/
/// ImageBld items -- see TemplateSrc/Dxt for the hand-authored shape it mirrors) FROM an
/// rxdk.project.json, so a project created the VS Code / Open Folder way can be opened in Visual
/// Studio ("Import VSCode Project"). This file exercises every field the manifest schema supports,
/// so a field silently dropped fails a test instead of only surfacing on a real click.
/// </summary>
public sealed class VcxprojExporterTests
{
    // Every field RxdkProjectManifest / RxdkImageBuildOptions supports, non-default where a default
    // exists, so a field that round-trips to its own default value can't hide a bug. Two named
    // configurations (Debug/Release) matching what RXDK-VSCode actually writes today, since the new
    // toolset derives UseDebugLibraries from the VS configuration name rather than a manifest field.
    private static RxdkProjectManifest FullManifest() => new()
    {
        Name = "FullFieldsSample",
        DefaultConfiguration = "Debug",
        Configurations = new()
        {
            ["Debug"] = new RxdkProjectManifest
            {
                Libraries = new() { "libd3d8d.lib", "libxapid.lib", "libcd.lib", "libcppd.lib", "libkerneld.lib" },
            },
            ["Release"] = new RxdkProjectManifest
            {
                Libraries = new() { "libd3d8.lib", "libxapi.lib", "libc.lib", "libcpp.lib", "libkernel.lib" },
            },
        },
        Type = RxdkProjectKind.Executable,
        Sources = new() { "src/main.cpp", "src/helper.c" },
        Resources = new() { "font.rdf", "gamepad.rdf" },
        LibraryPaths = new() { "vendor/lib" },
        AdditionalLibraries = new() { "vendor/prebuilt/thirdparty.lib" },
        ProjectReferences = new() { "../SharedLib" },
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

    // Metadata (Link.LibraryDependencies etc.) lives inside possibly-multiple ItemDefinitionGroup
    // elements (a common/unconditioned one plus per-config ones, or just per-config ones when the
    // value legitimately differs by configuration, like library names). A specific config's value
    // is whatever's in its own conditioned block, falling back to the unconditioned one.
    private static string? ItemMetaValue(XDocument doc, string itemType, string metaName, string? config = null)
    {
        XNamespace ns = doc.Root!.GetDefaultNamespace();
        foreach (var idg in doc.Root!.Elements(ns + "ItemDefinitionGroup"))
        {
            var cond = idg.Attribute("Condition")?.Value ?? "";
            var matches = config is null ? cond == "" : cond.Contains($"=='{config}|Xbox'");
            if (!matches) continue;
            var val = idg.Element(ns + itemType)?.Element(ns + metaName)?.Value;
            if (val != null) return val;
        }
        return null;
    }

    private static string? FlatPropertyValue(XDocument doc, string name)
    {
        XNamespace ns = doc.Root!.GetDefaultNamespace();
        return doc.Root!.Elements(ns + "PropertyGroup").Elements(ns + name).FirstOrDefault()?.Value;
    }

    [Fact]
    public void Libraries_export_per_config_as_LibraryDependencies_without_the_lib_extension()
    {
        // Debug and Release keep their own literal names (the manifest already spells out the
        // config-appropriate "d"-suffixed name) -- no attempt to collapse them into one "$(D)"
        // value; that's exactly what a hand-authored template does too.
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal(
                "libd3d8d;libxapid;libcd;libcppd;libkerneld",
                ItemMetaValue(doc, "Link", "LibraryDependencies", "Debug"));
            Assert.Equal(
                "libd3d8;libxapi;libc;libcpp;libkernel",
                ItemMetaValue(doc, "Link", "LibraryDependencies", "Release"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Libcompat_is_pulled_out_of_LibraryDependencies_into_a_whole_archive_AdditionalOptions()
    {
        // libcompat[d] needs -Wl,--whole-archive wrapping to win its COMDAT tie-break against zig's
        // bundled compiler-rt -- a plain "-lNAME" (LibraryDependencies) only pulls referenced
        // objects, defeating the point. No toolset-side auto-detection (Rxdk.MsBuild.props no
        // longer force-links it for every project): the exporter does this once, per project, at
        // generation time, and must never emit it as a second, redundant plain entry too.
        var projectRoot = Directory.CreateTempSubdirectory("rxdk-vcxproj-export-test-").FullName;
        try
        {
            var manifest = new RxdkProjectManifest
            {
                Name = "CompatSample",
                DefaultConfiguration = "Debug",
                Configurations = new()
                {
                    ["Debug"] = new RxdkProjectManifest { Libraries = new() { "libc.lib", "libcompatd.lib" } },
                    ["Release"] = new RxdkProjectManifest { Libraries = new() { "libc.lib", "libcompat.lib" } },
                },
                Sources = new() { "src/main.c" },
            };
            var json = JsonSerializer.Serialize(manifest, RxdkManifestLoader.JsonOptions);
            File.WriteAllText(Path.Combine(projectRoot, RxdkManifestLoader.ManifestFileName), json);

            var result = VcxprojExporter.Export(projectRoot);
            var doc = XDocument.Load(result.VcxprojPath);

            // "libc" is identical once libcompat is pulled out of both configs, so it hoists into
            // the common (unconditioned) ItemDefinitionGroup rather than staying per-config.
            Assert.Equal("libc", ItemMetaValue(doc, "Link", "LibraryDependencies"));
            Assert.Equal("%(Link.AdditionalOptions) -Wl,--whole-archive -llibcompatd -Wl,--no-whole-archive",
                ItemMetaValue(doc, "Link", "AdditionalOptions", "Debug"));
            Assert.Equal("%(Link.AdditionalOptions) -Wl,--whole-archive -llibcompat -Wl,--no-whole-archive",
                ItemMetaValue(doc, "Link", "AdditionalOptions", "Release"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void AdditionalLibraries_export_as_LinkAdditionalDependencies_verbatim_paths()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("vendor\\prebuilt\\thirdparty.lib", ItemMetaValue(doc, "Link", "AdditionalDependencies"));
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
    public void IncludePaths_and_own_PublicIncludePaths_export_as_ClCompileRxdkAdditionalIncludeDirectories()
    {
        // The real AdditionalIncludeDirectories name can't be used (Microsoft.Cpp.targets clobbers
        // it) -- Rxdk.MsBuild.targets' ClCompile target reads RxdkAdditionalIncludeDirectories
        // instead (see TemplateSrc/Cube/child/cubemesh.vcxproj for the same pattern). Own
        // PublicIncludePaths is folded in too: it doesn't add itself to this project's own compile
        // otherwise, the same self-reference gap every library template needs.
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("src\\include;vendor\\include;include", ItemMetaValue(doc, "ClCompile", "RxdkAdditionalIncludeDirectories"));
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
    public void CompileFlags_export_as_ClCompileAdditionalOptions()
    {
        // Unlike the old Makefile engine, the real ClCompile target passes AdditionalOptions
        // straight to ZigCompile, so this now has a real, working equivalent.
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("-mno-ms-bitfields", ItemMetaValue(doc, "ClCompile", "AdditionalOptions"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void CppStandard_exports_only_when_non_default()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("c++20", ItemMetaValue(doc, "ClCompile", "CppLanguageStandard"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void PublicIncludePaths_stays_a_flat_property_too()
    {
        // Still emitted as the real PublicIncludePaths property (correct per-toolset convention,
        // matches every hand-authored library template), even though it only documents intent for
        // now (a ProjectReference consumer needs its own explicit RxdkAdditionalIncludeDirectories).
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("include", FlatPropertyValue(doc, "PublicIncludePaths"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void ImageBuild_fields_export_to_matching_ImageBld_item_metadata()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Equal("131072", ItemMetaValue(doc, "ImageBld", "StackSize"));
            Assert.Equal("false", ItemMetaValue(doc, "ImageBld", "Debug"));
            Assert.Equal("false", ItemMetaValue(doc, "ImageBld", "NoLogo"));
            Assert.Equal("false", ItemMetaValue(doc, "ImageBld", "NoLibWarn"));
            Assert.Equal("true", ItemMetaValue(doc, "ImageBld", "LimitMemory"));
            Assert.Equal("true", ItemMetaValue(doc, "ImageBld", "DontModifyHardDisk"));
            Assert.Equal("true", ItemMetaValue(doc, "ImageBld", "DontMountUtilityDrive"));
            Assert.Equal("true", ItemMetaValue(doc, "ImageBld", "FormatUtilityDrive"));
            Assert.Equal("32768", ItemMetaValue(doc, "ImageBld", "UtilityDriveClusterSize"));
            Assert.Equal("Audio;Video", ItemMetaValue(doc, "ImageBld", "NoPreload"));
            Assert.Equal("0xffff1234", ItemMetaValue(doc, "ImageBld", "TestId"));
            Assert.Equal("0xffff5678,00112233", ItemMetaValue(doc, "ImageBld", "TestAltId"));
            Assert.Equal("7", ItemMetaValue(doc, "ImageBld", "TestRegion"));
            Assert.Equal("1", ItemMetaValue(doc, "ImageBld", "TestRatings"));
            Assert.Equal("0xffffffff", ItemMetaValue(doc, "ImageBld", "TestMediaTypes"));
            Assert.Equal("AABBCCDD", ItemMetaValue(doc, "ImageBld", "TestLanKey"));
            Assert.Equal("EEFF0011", ItemMetaValue(doc, "ImageBld", "TestSignKey"));
            Assert.Equal("Full Fields Sample", ItemMetaValue(doc, "ImageBld", "TestName"));
            Assert.Equal("4096", ItemMetaValue(doc, "ImageBld", "TestVersion"));
            Assert.Equal("titleinfo.bin", ItemMetaValue(doc, "ImageBld", "TitleInfo"));
            Assert.Equal("titleimage.xpr", ItemMetaValue(doc, "ImageBld", "TitleImage"));
            Assert.Equal("saveimage.xpr", ItemMetaValue(doc, "ImageBld", "DefaultSaveImage"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void DeployPaths_and_Embed_export_as_IsoCopy_and_RxdkEmbed_items()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            XNamespace ns = doc.Root!.GetDefaultNamespace();
            var isoCopy = doc.Root!.Elements(ns + "ItemGroup").Elements(ns + "IsoCopy")
                .Select(e => e.Attribute("Include")!.Value).ToList();
            Assert.Equal(new[] { "Media\\**" }, isoCopy);

            var embed = doc.Root!.Elements(ns + "ItemGroup").Elements(ns + "RxdkEmbed").ToList();
            Assert.Single(embed);
            Assert.Equal("assets\\icon.xpr", embed[0].Attribute("Include")!.Value);
            Assert.Equal("IconXpr", embed[0].Element(ns + "Name")!.Value);
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void CreateIso_false_and_ForceCopy_surface_as_warnings_not_silently_dropped()
    {
        var (result, _, projectRoot) = ExportFullManifest();
        try
        {
            Assert.Contains(result.Warnings, w => w.StartsWith("createIso:false"));
            Assert.Contains(result.Warnings, w => w.StartsWith("forceCopy"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Resources_export_as_RxdkResource_items()
    {
        // One generic item type for .rdf/.xap/.vsh/.psh -- Rxdk.MsBuild.targets' RxdkBuildResources
        // target classifies by extension and runs bundler/xactbld/xsasm before ClCompile.
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            XNamespace ns = doc.Root!.GetDefaultNamespace();
            var resources = doc.Root!.Elements(ns + "ItemGroup").Elements(ns + "RxdkResource")
                .Select(e => e.Attribute("Include")!.Value).ToList();
            Assert.Equal(new[] { "font.rdf", "gamepad.rdf" }, resources);
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Sources_export_as_ClCompile_items_in_order()
    {
        var (_, doc, projectRoot) = ExportFullManifest();
        try
        {
            XNamespace ns = doc.Root!.GetDefaultNamespace();
            var sources = doc.Root!.Elements(ns + "ItemGroup").Elements(ns + "ClCompile")
                .Select(e => e.Attribute("Include")!.Value).ToList();
            Assert.Equal(new[] { "src\\main.cpp", "src\\helper.c" }, sources);
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Fact]
    public void Unresolved_project_reference_warns_and_is_not_linkable()
    {
        // "../SharedLib" (FullManifest) has no .vcxproj on disk next to this temp project, so unlike
        // the old Makefile engine (which had a manifest-driven RxdkProjectReferences fallback link
        // path) the new toolset simply can't link it yet -- it needs "Import VSCode Project" run on
        // it first. The exporter must still say so instead of silently dropping the reference.
        var (result, doc, projectRoot) = ExportFullManifest();
        try
        {
            XNamespace ns = doc.Root!.GetDefaultNamespace();
            Assert.Empty(doc.Root!.Elements(ns + "ItemGroup").Elements(ns + "ProjectReference"));
            Assert.Contains(result.Warnings, w => w.Contains("../SharedLib"));
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }

    [Theory]
    [InlineData(RxdkProjectKind.Library, "StaticLibrary")]
    [InlineData(RxdkProjectKind.Dxt, "DebuggerExtension")]
    [InlineData(RxdkProjectKind.Executable, "Application")]
    public void Type_exports_to_the_matching_ConfigurationType(RxdkProjectKind kind, string expected)
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
            XNamespace ns = doc.Root!.GetDefaultNamespace();
            var configTypes = doc.Root!.Elements(ns + "PropertyGroup")
                .Where(pg => (string?)pg.Attribute("Label") == "Configuration")
                .Select(pg => pg.Element(ns + "ConfigurationType")?.Value)
                .Distinct().ToList();
            Assert.Equal(new[] { expected }, configTypes);
        }
        finally { Directory.Delete(projectRoot, recursive: true); }
    }
}
