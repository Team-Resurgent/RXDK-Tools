using Rxdk.Engine.Build;
using Xunit;

namespace Rxdk.Engine.Tests;

/// <summary>
/// The CMake toolchain builds a title through Rxdk.Cli's compile-args, compile-launch and link-exe,
/// which run <see cref="XboxRecipe"/> and <see cref="ExternalBuild"/>. These pin that what they
/// produce is what <c>rxdk build</c> produces.
/// </summary>
public sealed class ExternalBuildTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rxdk-external-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Lib(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "!<arch>\n");
        return path;
    }

    // ---- compile flags --------------------------------------------------------------------------

    public static IEnumerable<object[]> Modes() =>
        from isCpp in new[] { false, true }
        from mode in Enum.GetValues<RxdkOptimizeMode>()
        select new object[] { isCpp, mode };

    [Theory]
    [MemberData(nameof(Modes))]
    public void CompileFlags_PlusOptimizeFlags_AreTheBuildsCompileCommand(bool isCpp, RxdkOptimizeMode mode)
    {
        var tc = Toolchain.Llvm(_dir);
        var sdkInclude = new[] { "-I", SdkLayout.GetSdkIncludeDir() };
        var build = XboxBuild.ComposeCompileArgs(
            tc, "a.c", "a.obj", sdkInclude, Array.Empty<string>(), Array.Empty<string>(),
            isCpp, "c++23", exceptions: true, mode);
        // What rxdk build adds per source; CMake writes these itself.
        var perSource = new[] { "-x", isCpp ? "c++" : "c", "-c", "a.c", "-oa.obj", "-MD", "-MF", "a.obj.d" };
        foreach (var f in perSource) Assert.True(build.Remove(f), $"rxdk build no longer passes {f}");

        var cmake = XboxRecipe.CompileFlags(tc, isCpp, "c++23", exceptions: true);
        var optimize = XboxRecipe.OptimizeFlags(mode);
        // CMake puts the configuration's flags after the common ones; the flags themselves must match.
        Assert.Equal(build.Order(StringComparer.Ordinal), cmake.Concat(optimize).Order(StringComparer.Ordinal));
        // And the common flags keep the build's order (the SDK include stays ahead of the project's).
        Assert.Equal(build.Where(f => !optimize.Contains(f)), cmake);
    }

    [Theory]
    [InlineData(RxdkOptimizeMode.Debug, false)]
    [InlineData(RxdkOptimizeMode.ReleaseSafe, false)]
    [InlineData(RxdkOptimizeMode.ReleaseFast, true)]
    [InlineData(RxdkOptimizeMode.ReleaseSmall, true)]
    public void OptimizeFlags_LineTablesOnly_WhenTheModeKeepsNoDebugInfo(RxdkOptimizeMode mode, bool lineTables)
    {
        Assert.Equal(lineTables, XboxRecipe.OptimizeFlags(mode).Contains("-gline-tables-only"));
    }

    [Fact]
    public void CompileFlags_CxxStandardAndExceptions_FollowTheSettings()
    {
        var tc = Toolchain.Llvm(_dir);
        var flags = XboxRecipe.CompileFlags(tc, isCpp: true, "c++17", exceptions: false);
        Assert.Contains("-std=c++17", flags);
        Assert.Contains("-fno-exceptions", flags);
        Assert.DoesNotContain("-fexceptions", flags);
        Assert.Contains("-std=c23", XboxRecipe.CompileFlags(tc, isCpp: false, "c++17", exceptions: false));
    }

    // ---- link plan ------------------------------------------------------------------------------

    [Fact]
    public void PlanLink_DedupesAndPutsLibkernelLast()
    {
        var xapi = Lib("libxapi.lib");
        var kernel = Lib("libkernel.lib");
        var c = Lib("libc.lib");
        var plan = XboxRecipe.PlanLink(
            new[] { "libkernel.lib", "libxapi.lib", "libc.lib", "libxapi.lib" },
            Array.Empty<string>(), new[] { _dir }, isDxt: false);
        Assert.Equal(new[] { xapi, c, kernel }, plan.LibraryArgs);
    }

    [Fact]
    public void PlanLink_PutsTheDebugLibkernelLastToo()
    {
        var xapi = Lib("libxapid.lib");
        var kernel = Lib("libkerneld.lib");
        var c = Lib("libcd.lib");
        var plan = XboxRecipe.PlanLink(
            new[] { "libxapid.lib", "libkerneld.lib", "libcd.lib" }, Array.Empty<string>(), new[] { _dir }, false);
        Assert.Equal(new[] { xapi, c, kernel }, plan.LibraryArgs);
    }

    [Fact]
    public void PlanLink_LibkernelFallsBackToXboxkrnl()
    {
        var krnl = Lib("xboxkrnl.lib");
        var plan = XboxRecipe.PlanLink(new[] { "libkernel.lib" }, Array.Empty<string>(), new[] { _dir }, false);
        Assert.Equal(new[] { krnl }, plan.LibraryArgs);
    }

    [Fact]
    public void PlanLink_WholeArchivesLibcompatFirst_AndGroupsTheTitlesLibraries()
    {
        var c = Lib("libcd.lib");
        var compat = Lib("libcompatd.lib");
        var user = new[] { "game/a.lib", "game/b.lib" };
        var plan = XboxRecipe.PlanLink(new[] { "libcd.lib", "libcompatd.lib" }, user, new[] { _dir }, false);
        Assert.Equal(new[]
        {
            "-Wl,--whole-archive", compat, "-Wl,--no-whole-archive",
            "-Wl,--start-group", "game/a.lib", "game/b.lib", "-Wl,--end-group",
            c,
        }, plan.LibraryArgs);
    }

    [Fact]
    public void PlanLink_SearchesTheDirectoriesInOrder()
    {
        var first = Directory.CreateDirectory(Path.Combine(_dir, "first")).FullName;
        File.WriteAllText(Path.Combine(first, "libfoo.lib"), "");
        Lib("libfoo.lib");
        var plan = XboxRecipe.PlanLink(new[] { "libfoo.lib" }, Array.Empty<string>(), new[] { first, _dir }, false);
        Assert.Equal(new[] { Path.Combine(first, "libfoo.lib") }, plan.LibraryArgs);
    }

    [Theory]
    [InlineData("libxapi.lib", false, "XapiTitleStartup")]
    [InlineData("libxapid.lib", false, "XapiTitleStartup")]
    [InlineData("libc.lib", false, "start")]
    [InlineData("libxapi.lib", true, "DxtEntry")]
    public void PlanLink_Entry(string lib, bool dxt, string entry)
    {
        Lib(lib);
        Assert.Equal(entry, XboxRecipe.PlanLink(new[] { lib }, Array.Empty<string>(), new[] { _dir }, dxt).Entry);
    }

    [Fact]
    public void PlanLink_Dxt_KeepsItsRelocations()
    {
        Lib("libc.lib");
        var plan = XboxRecipe.PlanLink(new[] { "libc.lib" }, Array.Empty<string>(), new[] { _dir }, isDxt: true);
        Assert.Equal("-Wl,--dynamicbase", plan.LibraryArgs[0]);
    }

    [Fact]
    public void PlanLink_MissingLibrary_Fails()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            XboxRecipe.PlanLink(new[] { "libnope.lib" }, Array.Empty<string>(), new[] { _dir }, false));
        Assert.Contains("libnope.lib", ex.Message);
    }

    // ---- link inputs ----------------------------------------------------------------------------

    [Fact]
    public void ClassifyLinkInputs_SortsObjectsTitleLibrariesAndSdkLibraries()
    {
        var inputs = ExternalBuild.ClassifyLinkInputs(new[]
        {
            "CMakeFiles/game.dir/main.cpp.obj", "util.o", "counter/counter.lib", @"C:\libs\png.lib",
            "libxapid.lib", "libd3d8", "",
        });
        Assert.Equal(new[] { "CMakeFiles/game.dir/main.cpp.obj", "util.o" }, inputs.Objects);
        Assert.Equal(new[] { "counter/counter.lib", @"C:\libs\png.lib" }, inputs.UserLibraries);
        Assert.Equal(new[] { "libxapid.lib", "libd3d8.lib" }, inputs.SdkLibraries);
    }

    [Fact]
    public void SplitCommandLine_HandlesQuotesAndEscapes()
    {
        Assert.Equal(
            new[] { "a.obj", "dir with space/b.obj", "say \"hi\"", "", "c.obj" },
            ExternalBuild.SplitCommandLine("a.obj \"dir with space/b.obj\"\n\"say \\\"hi\\\"\" \"\"\r\n  c.obj  "));
    }

    [Fact]
    public void ExpandResponseFiles_ReplacesAtFilesInPlace()
    {
        var rsp = Path.Combine(_dir, "objs.rsp");
        File.WriteAllText(rsp, "a.obj \"b c.obj\"\n");
        Assert.Equal(
            new[] { "-o", "x.exe", "a.obj", "b c.obj", "libc.lib", "@missing.rsp" },
            ExternalBuild.ExpandResponseFiles(new[] { "-o", "x.exe", "@" + rsp, "libc.lib", "@missing.rsp" }));
    }

    // ---- TLS retry ------------------------------------------------------------------------------

    [Theory]
    [InlineData(new[] { "-O0", "-g", "-c", "a.c" }, true)]
    [InlineData(new[] { "-O3", "-gline-tables-only" }, false)]
    [InlineData(new[] { "-g", "-gline-tables-only" }, false)]
    [InlineData(new[] { "-gline-tables-only", "-g" }, true)]
    [InlineData(new[] { "-g", "-g0" }, false)]
    [InlineData(new[] { "-O2" }, false)]
    public void NeedsTlsRetry_OnlyForFullDebugInfo(string[] args, bool expected)
    {
        Assert.Equal(expected, ExternalBuild.NeedsTlsRetry(args));
    }

    [Theory]
    [InlineData(new[] { "-c", "a.c", "-o", "out/a.obj" }, "out/a.obj")]
    [InlineData(new[] { "-c", "a.c", "-oout/a.obj" }, "out/a.obj")]
    [InlineData(new[] { "-c", "a.c" }, null)]
    public void ObjectPath_ReadsDashO(string[] args, string? expected)
    {
        Assert.Equal(expected, ExternalBuild.ObjectPath(args));
    }
}
