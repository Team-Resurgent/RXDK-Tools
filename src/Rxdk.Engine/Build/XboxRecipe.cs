namespace Rxdk.Engine.Build;

/// <summary>
/// The parts of an Xbox title build that a build system other than <c>rxdk build</c> must
/// reproduce exactly: the compile flags, the per-configuration optimize flags, and the link's
/// library order and entry point. <see cref="XboxBuild"/> and the CMake toolchain (through
/// Rxdk.Cli's compile-args and link-exe) both call these, so they cannot drift apart.
/// </summary>
public static class XboxRecipe
{
    /// <summary>
    /// Every flag a translation unit is compiled with except its optimize/debug level, its
    /// source/object/depfile, and the title's own includes, defines and flags. Includes the SDK
    /// include directory.
    /// </summary>
    public static List<string> CompileFlags(Toolchain tc, bool isCpp, string cppStandard, bool exceptions) =>
        XboxBuild.ComposeCompileArgs(
            tc, null, null, new[] { "-I", SdkLayout.GetSdkIncludeDir() }, Array.Empty<string>(),
            Array.Empty<string>(), isCpp, cppStandard, exceptions, null);

    /// <summary>The optimize and debug-info flags a configuration adds to <see cref="CompileFlags"/>.</summary>
    public static List<string> OptimizeFlags(RxdkOptimizeMode mode)
    {
        var flags = new List<string>(OptimizeMode.CompileFlags(mode));
        // Without full debug info, line tables still give stepping and crash symbolization.
        if (!OptimizeMode.KeepsDebugInfo(mode))
            flags.Add("-gline-tables-only");
        return flags;
    }

    /// <summary>The library arguments for a title's link, in order, and its entry point.</summary>
    public sealed record LinkPlan(List<string> LibraryArgs, string Entry);

    /// <summary>
    /// Orders a title's libraries for the link. <paramref name="sdkLibNames"/> are file names
    /// (libxapi.lib, libd3d8d.lib) found in <paramref name="searchDirs"/>; <paramref name="userLibs"/>
    /// are the title's own library files, which may depend on each other.
    /// </summary>
    public static LinkPlan PlanLink(
        IEnumerable<string> sdkLibNames, IReadOnlyList<string> userLibs, IReadOnlyList<string> searchDirs, bool isDxt)
    {
        // Deduped in first-seen order, the kernel library (either build: libkernel.lib or the Debug
        // libkerneld.lib) forced last so other archives resolve kernel imports.
        var libNames = new List<string>();
        foreach (var n in sdkLibNames)
            if (!string.IsNullOrWhiteSpace(n) && !libNames.Contains(n)) libNames.Add(n);
        foreach (var kernel in new[] { "libkernel.lib", "libkerneld.lib" })
        {
            if (libNames.Remove(kernel))
                libNames.Add(kernel);
        }

        // A title linking xAPI enters at XapiTitleStartup (which runs XapiInitProcess:
        // per-title/utility drive setup, etc.) rather than the bare libc `start`. The
        // library is named by its exact file, so the Debug build lists libxapid.lib --
        // match either spelling, or a Debug title silently falls back to `start` and
        // skips the whole xAPI init (e.g. no T:/U:/Z: drives).
        var linksXapi = libNames.Contains("libxapi.lib") || libNames.Contains("libxapid.lib");
        var entry = isDxt ? "DxtEntry" : linksXapi ? "XapiTitleStartup" : "start";

        var linkLibs = new List<string>();
        if (isDxt) linkLibs.Add("-Wl,--dynamicbase"); // DXT keeps its base-reloc table.

        string? ResolveLib(string name)
        {
            foreach (var dir in searchDirs)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // Resolve every SDK library, partitioning the whole-archive override(s) from the
        // referenced-only libs.
        var wholeArchiveLibs = new List<string>();
        var regularLibs = new List<string>();
        foreach (var libName in libNames)
        {
            // Fully verbatim, like a real "Additional Dependencies" list: the manifest/project
            // names the exact file it wants, extension included (libxapi.lib / libxapid.lib),
            // same as the retail XDK's d3d8$(D).lib flow -- the engine never appends the Debug
            // "d" suffix, or the ".lib" extension, on its own.
            var resolved = ResolveLib(libName)
                ?? (libName == "libkernel.lib" ? ResolveLib("xboxkrnl.lib") : null);
            if (resolved is null)
                throw new InvalidOperationException(
                    $"Missing library: {libName} under sdk/lib - run RXDK SDK install");
            // libcompat[d] must be force-linked whole-archive to win the compiler-rt comdat
            // tie-break (see XdkLink.IsWholeArchiveLib); every other library links the normal,
            // referenced-only way.
            (XdkLink.IsWholeArchiveLib(resolved) ? wholeArchiveLibs : regularLibs).Add(resolved);
        }

        // Emit the whole-archive override(s) AHEAD of every other archive (per f849319). libcompat
        // carries picolibc's fabs/sqrt/sin/cos/rem_pio2/memmove/... which must beat zig
        // compiler-rt's per-symbol COMDAT copies (SSE2 memmove faults on a PIII; the x87 fabs
        // corrupts the FPU stack). Defining them first means libc's identical copies are never
        // pulled -- when libcompat trailed libc, a pull of libc's copy (e.g. rem_pio2 via a trig
        // call) duplicate-symbol'd against the whole-archived version and failed the link.
        foreach (var wa in wholeArchiveLibs)
        {
            linkLibs.Add("-Wl,--whole-archive");
            linkLibs.Add(wa);
            linkLibs.Add("-Wl,--no-whole-archive");
        }
        if (userLibs.Count > 0)
        {
            linkLibs.Add("-Wl,--start-group");
            linkLibs.AddRange(userLibs);
            linkLibs.Add("-Wl,--end-group");
        }
        linkLibs.AddRange(regularLibs);
        return new LinkPlan(linkLibs, entry);
    }
}
