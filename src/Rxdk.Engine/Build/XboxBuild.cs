using System.Text.RegularExpressions;
using Rxdk.Engine.Bootstrap;
using Rxdk.Engine.Model;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Build;

public sealed record BuildResult(bool Ok, string OutDir, string? Error = null);

public sealed class BuildOptions
{
    public required string ProjectRoot { get; init; }
    public string? ZigExecutable { get; init; }
    public bool CompileOnly { get; init; }
    /// <summary>
    /// Configuration name to select from a multi-config manifest (e.g. "Debug"/"Release"). Ignored
    /// for a flat single-config manifest. Null = the manifest's defaultConfiguration (or first). The
    /// compiler optimize level follows the resolved configuration's debug/release flag (see
    /// <see cref="DeriveOptimize"/>). Which SDK library variant links is a separate, explicit
    /// concern -- like "Additional Dependencies", the manifest names the exact file it wants
    /// (libxapi.lib or libxapid.lib).
    /// </summary>
    public string? Configuration { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Compiles + links an Xbox title (or static library / DXT) with Zig and imagebld. C# port
/// of RXDK-VSCode xboxBuild.ts. The compile recipe matches the SDK's own title target
/// (build/xbox_target.zig): x86-windows-gnu, -nostdinc, force-included picolibc.h, so only
/// the staged SDK headers are on the path; -march=pentium3 for the Xbox CPU.
/// </summary>
public static class XboxBuild
{
    /// <summary>
    /// Compiler optimize level for a build, derived from the resolved manifest's debug/release flag:
    /// a Debug configuration builds -O0 with debug info; a Release one builds ReleaseFast. This drives
    /// how the project's OWN code compiles, not which SDK library variant gets linked -- like a real
    /// "Additional Dependencies" list, the manifest/project names the exact SDK lib it wants
    /// (libxapi.lib or libxapid.lib); the engine never appends a Debug suffix on its own.
    /// </summary>
    public static RxdkOptimizeMode DeriveOptimize(RxdkProjectManifest manifest) =>
        manifest.EffectiveConfiguration == RxdkConfiguration.Debug
            ? RxdkOptimizeMode.Debug
            : RxdkOptimizeMode.ReleaseFast;


    // -I (not -isystem) everywhere: the SDK's clean-room windef.h/etc. must win over zig's
    // bundled MinGW headers, which -isystem would let shadow them.
    // The sample + framework code is compiled warning-clean; only these unavoidable suppressions
    // remain, and none of them is a fixable source defect:
    //   * c++11-narrowing / address-of-temporary — clang treats these as hard ERRORS on legacy
    //     XDK idioms (braced-init narrowing e.g. STRING={(USHORT)strlen(s),...}; and taking the
    //     address of a temporary passed to a D3DX helper, D3DXVec3Cross(&out,&D3DXVECTOR3(...),...)
    //     — the temporary lives to end-of-expression so the callee is safe). Rewriting Microsoft's
    //     reference idioms is out of scope.
    //   * ignored-pragma-intrinsic — clang cannot honor MSVC's `#pragma intrinsic`; harmless.
    //   * multichar — the XDK FOURCC idiom ('YV12' etc.) is intentional, not a bug.
    //   * unused-command-line-argument — build-driver noise (a flag that doesn't apply to a TU).
    //   * deprecated-enum-enum-conversion — the D3D8 pixel-shader register-combiner API is
    //     *defined* by OR-ing the named PS_REGISTER / PS_CHANNEL / PS_INPUTMAPPING enums together
    //     (see d3d8types.h's own combiner examples). It is the documented, retail-faithful idiom;
    //     C++20 deprecates cross-enum bitwise ops in general but this usage is correct by design,
    //     and casting at every combiner call site across the shader samples would only obscure it.
    private static readonly string[] XdkClangWarnings =
    {
        "-Wno-c++11-narrowing",
        "-Wno-address-of-temporary",
        "-Wno-ignored-pragma-intrinsic",
        "-Wno-multichar",
        "-Wno-unused-command-line-argument",
        "-Wno-deprecated-enum-enum-conversion",
    };

    // Resolve a project's manifest from its committed rxdk.project.json. Every project has one:
    // VS Code / VS20XX Open Folder author it directly, and the VS20XX .vcxproj flow generates it
    // at the project root from the .vcxproj before the build (so a referenced child library project
    // has its own rxdk.project.json too). A committed manifest may be multi-config; resolve to a
    // single effective view for the requested configuration.
    private static RxdkProjectManifest ReadManifest(string dir, string? configName = null)
    {
        if (File.Exists(Path.Combine(dir, RxdkManifestLoader.ManifestFileName)))
            return RxdkManifestLoader.Load(dir).ResolveConfiguration(configName);
        throw new FileNotFoundException(
            $"No rxdk.project.json in {dir}. Build the referenced library project first.");
    }

    // A referenced project has a manifest if it ships an rxdk.project.json (committed, or generated
    // from its .vcxproj by its own build, which runs first via the project-reference build order).
    private static bool HasManifest(string dir) =>
        File.Exists(Path.Combine(dir, RxdkManifestLoader.ManifestFileName));

    private static List<string> ProjectDefineArgs(RxdkProjectManifest m) =>
        (m.Defines ?? new()).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => $"-D{d}").ToList();

    private static List<string> ProjectCompileFlagArgs(RxdkProjectManifest m) =>
        (m.CompileFlags ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();

    // ---- per-file compile ----

    private static async Task ZigCompileAsync(
        Toolchain tc, string source, string obj, IReadOnlyList<string> includeArgs,
        IReadOnlyList<string> defineArgs, IReadOnlyList<string> userFlags, bool isCpp, string cppStandard, bool exceptions,
        RxdkOptimizeMode optimize,
        Action<string>? log, CancellationToken ct)
    {
        var common = new List<string> { "-target", tc.TargetTriple };
        // LLVM builds with -nostdinc (below) and, unlike zig cc, do NOT keep clang's resource dir
        // on the search path, so re-add it here or stddef.h/stdarg.h/... are unfound (empty for zig).
        common.AddRange(tc.ResourceIncludeArgs);
        common.AddRange(OptimizeMode.CompileFlags(optimize));
        common.AddRange(new[]
        {
            "-ffreestanding", "-fno-stack-protector", "-fms-extensions", "-fms-compatibility",
            "-nostdinc", "-include", "picolibc.h", "-march=pentium3",
            // Every Xbox title is built with _XBOX/XBOX defined (the XDK did this); a lot of
            // Xbox headers/code select their platform path on it.
            "-D_XBOX", "-DXBOX",
            // Keep Clang from inline-expanding memmove/memcpy-shaped calls past picolibc's
            // -fno-builtin implementations, and pin the retail (_DEBUG-off) SDK link path.
            "-fno-builtin", "-U_DEBUG",
            // picolibc's default assert() calls __assert_no_args(), which prints a bare
            // "assertion failed" -- useless for locating a fault in a title. Ask for the
            // variant that reports the expression, file and line.
            "-D__ASSERT_VERBOSE",
            // Thread-local storage: emulated TLS (a per-thread table reached via
            // __emutls_get_address, backed by libc tss/emutls.c) instead of the native
            // Windows __tls_index/TEB %fs model, which the RXDK runtime never sets up.
            // Without this, any title `__thread`/`thread_local` (e.g. stb_image's
            // stbi__g_failure_reason / vertically_flip_on_load) reads a wild fixed
            // address and bugchecks. Matches how libcpp is built (xbox_target.zig
            // cppFlags).
            "-femulated-tls",
        });
        // TLS debug-info handling. -femulated-tls makes clang emit a CodeView S_*THREAD32
        // record per thread_local pointing at the native per-var symbol that emutls never
        // defines -> undefined-symbol at link. Historically we blanket-added
        // -gline-tables-only to dodge that, which also stripped ALL local-variable and
        // `this` records -> the debugger's Locals/Autos were empty in Debug builds. Only
        // TUs that actually use TLS hit the link problem, so we now:
        //   * Debug/ReleaseSafe (KeepsDebugInfo): compile with full -g (from CompileFlags)
        //     so locals + `this` are inspectable, then, if the object turns out to use
        //     emulated TLS (ObjUsesEmulatedTls), recompile just that TU with
        //     -gline-tables-only to keep its link clean (locals unavailable in that one
        //     file only).
        //   * ReleaseFast/ReleaseSmall (no -g): add -gline-tables-only for line tables
        //     (F5 stepping + crash symbolization) with no locals expected anyway.
        var keepsDebug = OptimizeMode.KeepsDebugInfo(optimize);
        if (!keepsDebug)
            common.Add("-gline-tables-only");
        common.AddRange(includeArgs);
        common.AddRange(defineArgs);
        common.AddRange(XdkClangWarnings);
        // Project-supplied compile flags come last so they can override the RXDK defaults above
        // (e.g. -mno-ms-bitfields to undo the MSVC bitfield layout that -fms-compatibility selects).
        common.AddRange(userFlags);
        // -x: state the language rather than letting clang infer it from the extension. Its
        // suffix table is case-sensitive, so an imported project spelling a source "Foo.Cpp"
        // would otherwise be treated as a linker input and -c would silently emit no object.
        common.AddRange(new[] { "-x", isCpp ? "c++" : "c" });
        common.AddRange(new[] { "-c", source, $"-o{obj}" });
        // Emit a Make-style header dependency list next to the object (<obj>.d). The incremental
        // build (IsObjUpToDate) reads it to decide whether a later build may skip recompiling this
        // TU. -MD writes the deps as a side effect without suppressing the compile itself.
        common.AddRange(new[] { "-MD", "-MF", obj + ".d" });

        var toolArgs = new List<string>();
        if (isCpp)
        {
            // The standard is per-project (manifest "cppStandard"), defaulting to c++23. XDK-era
            // code that uses the C++98 allocator members has to name an older one -- libc++ gates
            // those on the standard with no opt-in macro, unlike auto_ptr just below.
            toolArgs.AddRange(tc.CompileSubcommand(true));
            toolArgs.AddRange(new[] { $"-std={cppStandard}", "-nostdinc++",
                                      exceptions ? "-fexceptions" : "-fno-exceptions", "-frtti" });
            // Ported XDK-era C++ predates C++17 and still uses std::auto_ptr (removed in C++17,
            // which -std=c++23 selects). libc++ keeps the implementation behind this macro, so
            // opt legacy titles back in rather than forcing them off a modern standard.
            toolArgs.Add("-D_LIBCPP_ENABLE_CXX17_REMOVED_AUTO_PTR");
            // C++ standard library: RXDK ships libc++ (built against picolibc) with headers staged
            // at sdk/include/c++/v1. Add it *before* the C include dir so libc++'s C-header wrappers
            // (ctype.h/wchar.h/...) win and include_next into picolibc. -fms-compatibility-version
            // simulates MSVC 2015+, where char16_t/char32_t are native keywords libc++ requires;
            // plain -fms-compatibility emulates older MSVC and disables them. (libcpp.lib is linked
            // via the project's libraries — the importer force-adds it, and the C++ templates list it.)
            var cxxInc = Path.Combine(SdkLayout.GetSdkIncludeDir(), "c++", "v1");
            if (Directory.Exists(cxxInc))
                toolArgs.AddRange(new[]
                {
                    $"-I{cxxInc}", "-fms-compatibility-version=19.20",
                    // libc++ was built with _WIN32/__MINGW32__ undefined so it takes its newlib
                    // (not Win32/MSVCRT) locale + support backends. Consuming TUs must match, or
                    // libc++'s <locale> pulls the Windows backend and fails on MSVC-only types
                    // like _locale_t that picolibc doesn't provide.
                    "-U_WIN32", "-U__MINGW32__",
                    // The newlib locale backend calls picolibc's *_l locale functions
                    // (strtod_l, ...), which picolibc only declares under __GNU_VISIBLE
                    // (_GNU_SOURCE). libcpp's own build enables it via picolibc_prereq.h.
                    "-D_GNU_SOURCE",
                });
        }
        else
        {
            toolArgs.AddRange(tc.CompileSubcommand(false));
            toolArgs.Add("-std=c23");
        }
        toolArgs.AddRange(common);

        var result = await ProcessRunner.RunStreamedAsync(tc.CompilerExe, toolArgs, log, ct: ct);

        // If a full-debug TU turns out to use emulated TLS, its object carries emutls symbols
        // and a full-`-g` link would fail on the undefined native TLS symbol. Recompile that
        // one TU with -gline-tables-only (line tables only) to keep the link clean; its locals
        // become unavailable but every other TU keeps full local/`this` inspection.
        if (keepsDebug && result.Success && ObjUsesEmulatedTls(obj))
        {
            log?.Invoke(
                $"{Path.GetFileName(source)}: uses thread_local; rebuilding with line-tables-only " +
                "debug info (locals unavailable in this file) to keep the emulated-TLS link clean.");
            var retryArgs = new List<string>(toolArgs) { "-gline-tables-only" };
            result = await ProcessRunner.RunStreamedAsync(tc.CompilerExe, retryArgs, log, ct: ct);
        }

        // Surface (but don't fail on) warnings in the title's own source. Clean RXDK template
        // code produces none, but imported/legacy code warns heavily — most notably -Wformat on
        // DWORD-vs-%u, which is benign on this ILP32 target — while still compiling correctly.
        // Failing the build on those would make importing real projects impractical.
        var combined = (result.StdOut + result.StdErr).Split('\n');
        var sourcePattern = new Regex(Regex.Escape(Path.GetFullPath(source)));
        var warnCount = combined.Count(l => l.Contains(": warning:") && sourcePattern.IsMatch(l));
        if (warnCount > 0 && isCpp)
            log?.Invoke($"Note: {warnCount} warning(s) in {Path.GetFileName(source)} (not fatal)");
        if (!result.Success)
            throw new InvalidOperationException($"Compile failed on {source} (exit {result.ExitCode})");
    }

    // True if a compiled object references emulated-TLS runtime symbols (___emutls_v.*,
    // __emutls_get_address). -femulated-tls only emits these for TUs that actually use
    // thread_local, and the names appear as plain ASCII in the COFF symbol/string table,
    // so a substring scan detects TLS usage without parsing the object -- and it catches
    // TLS pulled in through headers (e.g. stb_image), which a source-text scan would miss.
    private static bool ObjUsesEmulatedTls(string objPath)
    {
        try
        {
            if (!File.Exists(objPath)) return false;
            return ContainsAscii(File.ReadAllBytes(objPath), "emutls");
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsAscii(byte[] haystack, string needle)
    {
        if (haystack.Length < needle.Length) return false;
        var last = haystack.Length - needle.Length;
        for (var i = 0; i <= last; i++)
        {
            var j = 0;
            for (; j < needle.Length; j++)
                if (haystack[i + j] != (byte)needle[j]) break;
            if (j == needle.Length) return true;
        }
        return false;
    }

    /// <summary>
    /// Runs the bundler on the project's .rdf resource files, then xactbld on any .xap XACT
    /// projects. Uses the explicit <see cref="RxdkProjectManifest.Resources"/> list if present,
    /// otherwise auto-discovers every *.rdf under the project root. The bundler resolves
    /// out_header / out_packedresource paths relative to each .rdf, so outputs land in the
    /// project tree (Resource.h next to the sources, the .xpr under the media/deploy path named
    /// in the .rdf). See <see cref="CompileXactProjectsAsync"/> for the .xap step.
    /// </summary>
    private static async Task CompileResourcesAsync(
        string projectRoot, RxdkProjectManifest manifest, Action<string>? log, CancellationToken ct)
    {
        var rdfs = new List<string>();
        if (manifest.Resources is { Count: > 0 })
        {
            // A missing .rdf produces no .xpr, so the media never reaches the ISO and a title
            // that loads it by name links clean and then dies in Initialize() with
            // XBAPPERR_MEDIANOTFOUND before the first frame. Fail here instead.
            var missing = new List<string>();
            foreach (var rel in manifest.Resources)
            {
                if (string.IsNullOrWhiteSpace(rel)) continue;
                if (!rel.EndsWith(".rdf", StringComparison.OrdinalIgnoreCase)) continue;
                var p = Path.GetFullPath(Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(p)) missing.Add(p);
                else rdfs.Add(p);
            }
            if (missing.Count > 0)
                throw new FileNotFoundException(
                    "Missing resource .rdf file(s), so their .xpr media cannot be built: " +
                    string.Join(", ", missing));
        }
        else
        {
            rdfs.AddRange(Directory.EnumerateFiles(projectRoot, "*.rdf", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
            }));
        }

        if (rdfs.Count > 0)
        {
            var bundler = RxdkPaths.ResolveHostTool("bundler");
            if (!File.Exists(bundler))
                throw new FileNotFoundException(
                    $"bundler host tool not found: {bundler}. Update the RXDK tools (the resource pipeline needs the bundler).");

            foreach (var rdf in rdfs)
            {
                // Pass the bare filename as it is spelled on disk. The working dir is already the
                // .rdf's folder, and the bundler derives both the header comment and the macro
                // prefix from the spelling it is handed, so an absolute path would bake this
                // machine's paths into a checked-in file and the project file's casing (imported
                // vcxprojs disagree with the file system) would rename every macro the sources
                // #include.
                var name = OnDiskFileName(rdf);
                log?.Invoke($"Compiling resources: {name}");
                var result = await RunHostToolAsync(
                    bundler, new[] { name, "-q" }, log, Path.GetDirectoryName(rdf), ct);
                if (!result.Success)
                    throw new InvalidOperationException(
                        $"bundler failed on {name} (exit {result.ExitCode})");
            }
        }

        await CompileXactProjectsAsync(projectRoot, manifest, log, ct);
    }

    /// <summary>
    /// Runs the xactbld tool on the project's .xap XACT-project files. Each .xap produces the
    /// generated C header (XactSounds.h, next to the .xap so the sources can #include it) plus
    /// a wave bank (.xwb) and sound bank (.xsb) written to the media paths named inside it (for
    /// deploy). Uses the manifest's .xap resources if listed, otherwise auto-discovers *.xap
    /// under the project root and its immediate parent — XDK sound samples keep the .xap at the
    /// sample root next to the .cpp, one level above the .vcxproj/manifest directory.
    /// </summary>
    private static async Task CompileXactProjectsAsync(
        string projectRoot, RxdkProjectManifest manifest, Action<string>? log, CancellationToken ct)
    {
        var xaps = new List<string>();

        // Explicit .xap entries carried in the manifest resources list.
        foreach (var rel in manifest.Resources ?? new())
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            if (!rel.EndsWith(".xap", StringComparison.OrdinalIgnoreCase)) continue;
            var p = Path.GetFullPath(Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(p)) xaps.Add(p);
        }

        // Auto-discover under the PROJECT ROOT only (skip symlinks/junctions + inaccessible entries;
        // do not scan the parent -- see the shader discovery note in CompileShadersAsync).
        var xapOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        };
        foreach (var f in Directory.EnumerateFiles(projectRoot, "*.xap", xapOpts))
            xaps.Add(Path.GetFullPath(f));

        var unique = xaps.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unique.Count == 0)
            return;

        var xactbld = RxdkPaths.ResolveHostTool("xactbld");
        if (!File.Exists(xactbld))
            throw new FileNotFoundException(
                $"xactbld host tool not found: {xactbld}. Update the RXDK tools (the XACT audio pipeline needs xactbld).");

        foreach (var xap in unique)
        {
            log?.Invoke($"Compiling XACT project: {Path.GetFileName(xap)}");
            var result = await RunHostToolAsync(
                xactbld, new[] { xap, "-q" }, log, Path.GetDirectoryName(xap), ct);
            if (!result.Success)
                throw new InvalidOperationException(
                    $"xactbld failed on {Path.GetFileName(xap)} (exit {result.ExitCode})");
        }
    }

    /// <summary>
    /// Compiles the project's shader sources to NV2A microcode with xsasm: each *.vsh -> *.xvu
    /// and *.psh -> *.xpu, written next to the source so it deploys with the media tree (titles
    /// load e.g. "Shaders\\Foo.xvu" at runtime). Uses the manifest's .vsh/.psh resources if
    /// listed, otherwise auto-discovers under the project root (skipping build-output dirs).
    /// Files without a shader version line are include fragments (#included by others), so they
    /// are skipped rather than compiled standalone. A shader that fails to assemble fails the build.
    /// </summary>
    private static async Task CompileShadersAsync(
        string projectRoot, RxdkProjectManifest manifest, Action<string>? log, CancellationToken ct)
    {
        static bool IsShaderSource(string p) =>
            p.EndsWith(".vsh", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".psh", StringComparison.OrdinalIgnoreCase);

        var shaders = new List<string>();

        // Explicit .vsh/.psh entries carried in the manifest resources list.
        foreach (var rel in manifest.Resources ?? new())
        {
            if (string.IsNullOrWhiteSpace(rel) || !IsShaderSource(rel)) continue;
            var p = Path.GetFullPath(Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(p)) shaders.Add(p);
        }

        // Auto-discover if none listed: every *.vsh / *.psh under the PROJECT ROOT (each project keeps
        // its shaders in its own Media\Shaders). Do NOT scan the parent directory: a project placed
        // directly under a busy folder (e.g. a repo root full of sibling checkouts) would otherwise walk
        // every sibling -- pulling in unrelated shaders and choking on their broken symlinks/junctions.
        // Skip reparse points so a symlink/junction can't redirect the walk or throw, and skip
        // build-output trees (out/, obj/, bin/) so deployed copies are not recompiled.
        if (shaders.Count == 0)
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
            };
            foreach (var pat in new[] { "*.vsh", "*.psh" })
                foreach (var f in Directory.EnumerateFiles(projectRoot, pat, opts))
                    if (!IsBuildOutputPath(f, projectRoot))
                        shaders.Add(Path.GetFullPath(f));
        }

        var unique = shaders
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(HasShaderVersionLine)   // skip include fragments (no vs./ps. version directive)
            .ToList();
        if (unique.Count == 0) return;

        var xsasm = RxdkPaths.ResolveHostTool("xsasm");
        if (!File.Exists(xsasm))
            throw new FileNotFoundException(
                $"xsasm host tool not found: {xsasm}. Update the RXDK tools (the shader pipeline needs xsasm).");

        foreach (var src in unique)
        {
            var isPixel = src.EndsWith(".psh", StringComparison.OrdinalIgnoreCase);
            var outPath = Path.ChangeExtension(src, isPixel ? ".xpu" : ".xvu");
            var dir = Path.GetDirectoryName(src)!;
            log?.Invoke($"Compiling shader: {Path.GetFileName(src)} -> {Path.GetFileName(outPath)}");
            // -I <dir>: fur/fin-style shaders #include sibling fragments from their own directory.
            var result = await RunHostToolAsync(
                xsasm, new[] { src, "-o", outPath, "-I", dir }, log, dir, ct);
            if (!result.Success)
                throw new InvalidOperationException(
                    $"xsasm failed on {Path.GetFileName(src)} (exit {result.ExitCode})");
        }
    }

    /// <summary>
    /// The file's name as the file system spells it, which can differ in case from the path a
    /// project file gave us. Falls back to the requested spelling if the file is gone.
    /// </summary>
    private static string OnDiskFileName(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir)) return name;
        var match = Directory.EnumerateFiles(dir, name).FirstOrDefault();
        return match is null ? name : Path.GetFileName(match);
    }

    /// <summary>
    /// Runs a host tool, retrying the transient failures that appear when something outside the
    /// build (realtime antivirus, the search indexer) is still holding a file the tool has just
    /// generated — either mapping a section on it or keeping it open. Both clear themselves
    /// within a few hundred milliseconds; left alone they fail a parallel sweep on a different
    /// random sample every run.
    /// </summary>
    private static async Task<ProcessResult> RunHostToolAsync(
        string tool,
        IReadOnlyList<string> args,
        Action<string>? log,
        string? workingDirectory,
        CancellationToken ct)
    {
        const int MaxAttempts = 4;
        ProcessResult result;
        for (var attempt = 1; ; attempt++)
        {
            result = await ProcessRunner.RunStreamedAsync(tool, args, log, workingDirectory, ct);
            if (result.Success || attempt == MaxAttempts) return result;

            var output = result.StdOut + result.StdErr;
            var transient =
                output.Contains("user-mapped section open", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
            if (!transient) return result;

            var delayMs = 250 * attempt;
            log?.Invoke(
                $"{Path.GetFileNameWithoutExtension(tool)}: output file is held by another " +
                $"process, retrying in {delayMs}ms");
            await Task.Delay(delayMs, ct);
        }
    }

    /// <summary>True when the path lives under a build-output tree (out/, obj/, bin/).</summary>
    private static bool IsBuildOutputPath(string file, string projectRoot)
    {
        var rel = "/" + Path.GetRelativePath(projectRoot, file).Replace('\\', '/') + "/";
        return rel.Contains("/out/", StringComparison.OrdinalIgnoreCase)
            || rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when a .vsh/.psh is a standalone shader we should assemble to microcode, rather than
    /// a shared include fragment. A shader qualifies either by beginning (past comments/blank
    /// lines) with a version directive such as vs.1.1 / xvs.1.1 / xvss.1.1 / ps.1.1 / xps.1.1, or
    /// by pulling in one via <c>#include</c>. The XDK's combinatorial variants (Fur's
    /// <c>fur_wind0_local0_self0.vsh</c> and friends) are nothing but a few <c>#define</c>s and
    /// <c>#include "fur.vsh"</c> — the include supplies the version line after preprocessing, and
    /// the title loads exactly those variant .xvu, so they must be assembled even though the raw
    /// file never starts with a version directive.
    /// </summary>
    private static bool HasShaderVersionLine(string file)
    {
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw;
            int c = line.IndexOf("//", StringComparison.Ordinal); if (c >= 0) line = line[..c];
            int s = line.IndexOf(';'); if (s >= 0) line = line[..s];
            line = line.Trim();
            if (line.Length == 0) continue;
            // An #include of the shader body makes this a compilable variant, not a fragment.
            if (line.StartsWith("#include", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith('#')) continue;   // other directives (#define, #ifdef) precede the body
            return Regex.IsMatch(line, @"^(xvss|xvsw|xvs|vs|xps|ps)\s*\.\s*\d", RegexOptions.IgnoreCase);
        }
        return false;
    }

    // ---- multi-project (library reference) support ----

    private static List<string> GetProjectReferences(string projectRoot, RxdkProjectManifest m)
    {
        var refs = new List<string>();
        foreach (var rel in m.ProjectReferences ?? new())
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var dir = Path.GetFullPath(Path.Combine(projectRoot, rel));
            if (!HasManifest(dir))
                throw new InvalidOperationException(
                    $"projectReferences: no rxdk.project.json in {dir} (build that project first)");
            refs.Add(dir);
        }
        return refs;
    }

    private static void AddDependencyOrder(string dir, List<string> ordered, Dictionary<string, string> state)
    {
        var key = dir.ToLowerInvariant();
        if (state.TryGetValue(key, out var s))
        {
            if (s == "done") return;
            if (s == "visiting") throw new InvalidOperationException($"Cyclic projectReferences involving {dir}");
        }
        state[key] = "visiting";
        var manifest = ReadManifest(dir);
        foreach (var reference in GetProjectReferences(dir, manifest))
            AddDependencyOrder(reference, ordered, state);
        state[key] = "done";
        ordered.Add(dir);
    }

    /// <summary>Transitive library dependencies, in build (deps-first) order.</summary>
    private static List<string> GetDependencyOrder(string projectRoot, RxdkProjectManifest m)
    {
        var ordered = new List<string>();
        var state = new Dictionary<string, string>();
        foreach (var reference in GetProjectReferences(projectRoot, m))
            AddDependencyOrder(reference, ordered, state);
        return ordered;
    }

    private static List<string> ResolveIncludeArgs(string projectRoot, IReadOnlyList<string>? values, string label)
    {
        var outList = new List<string>();
        foreach (var rel in values ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var dir = Path.GetFullPath(Path.Combine(projectRoot, rel));
            if (!Directory.Exists(dir)) throw new InvalidOperationException($"{label}: not found {dir}");
            outList.Add($"-I{dir}");
        }
        return outList;
    }

    /// <summary>Public includes exported by every transitive library dependency (deduped -I args).</summary>
    private static List<string> GetTransitivePublicIncludeArgs(string projectRoot, RxdkProjectManifest m)
    {
        var seen = new HashSet<string>();
        var outList = new List<string>();
        foreach (var dep in GetDependencyOrder(projectRoot, m))
        {
            var depManifest = ReadManifest(dep);
            foreach (var arg in ResolveIncludeArgs(dep, depManifest.PublicIncludePaths, "publicIncludePaths"))
                if (seen.Add(arg)) outList.Add(arg);
        }
        return outList;
    }

    private static async Task<(List<string> objs, bool usesCpp, bool anyRecompiled)> CompileProjectSourcesAsync(
        string projectRoot, RxdkProjectManifest m, Toolchain tc, string outDir,
        IReadOnlyList<string> includeArgs, IReadOnlyList<string> defineArgs,
        RxdkOptimizeMode optimize, Action<string>? log, CancellationToken ct)
    {
        var objs = new List<string>();
        var usesCpp = false;
        var anyRecompiled = false;
        var incremental = m.Incremental ?? true;
        var userFlagArgs = ProjectCompileFlagArgs(m);
        foreach (var relSrc in m.Sources ?? new())
        {
            var src = Path.Combine(projectRoot, relSrc.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) throw new FileNotFoundException($"Source not found: {src}");
            // Name the object after the full relative source path (separators -> '_'), not just the
            // basename: a title can have several sources sharing a filename (src/code/main.c and
            // src/dreamcast/main.c), and a basename-only object would clobber the earlier one,
            // dropping its symbols at link.
            var objStem = relSrc.Replace('/', '_').Replace('\\', '_');
            var obj = Path.Combine(outDir, Path.ChangeExtension(objStem, ".obj"));
            var ext = Path.GetExtension(src).ToLowerInvariant();
            var isCpp = ext is ".cpp" or ".cxx";
            if (isCpp) usesCpp = true;

            // Incremental: skip the compile when the object is already newer than the source and
            // every header it pulled in on the previous build (recorded in <obj>.d). A missing
            // object or depfile, or any newer input, forces the recompile.
            if (incremental && IsObjUpToDate(src, obj))
            {
                log?.Invoke($"Up to date {obj}");
                objs.Add(obj);
                continue;
            }

            await ZigCompileAsync(tc, src, obj, includeArgs, defineArgs, userFlagArgs, isCpp,
                                  m.EffectiveCppStandard, m.Exceptions ?? true, optimize, log, ct);
            // A compiler can exit 0 and still write nothing (see the -x note above). Catch that
            // here, where we still know which source it was, rather than at link time.
            if (!File.Exists(obj))
                throw new InvalidOperationException(
                    $"Compiler reported success but produced no object for {src} (expected {obj}).");
            log?.Invoke($"Compiled {obj}");
            objs.Add(obj);
            anyRecompiled = true;
        }
        return (objs, usesCpp, anyRecompiled);
    }

    /// <summary>
    /// True when <paramref name="obj"/> exists and is at least as new as its source and every header
    /// recorded in the sidecar depfile (<c><paramref name="obj"/>.d</c>), so recompiling it would
    /// reproduce the same object. A missing object or depfile, a vanished dependency, or any input
    /// newer than the object returns false (recompile).
    /// </summary>
    private static bool IsObjUpToDate(string src, string obj)
    {
        if (!File.Exists(obj)) return false;
        var objTime = File.GetLastWriteTimeUtc(obj);
        if (File.GetLastWriteTimeUtc(src) > objTime) return false;

        var depFile = obj + ".d";
        if (!File.Exists(depFile)) return false; // no recorded deps -> can't prove fresh
        foreach (var dep in ParseDepfile(depFile))
        {
            // A dependency that no longer exists means a header was moved/deleted: recompile so the
            // failure (or the newly-resolved header) is seen now, not silently skipped.
            if (!File.Exists(dep)) return false;
            if (File.GetLastWriteTimeUtc(dep) > objTime) return false;
        }
        return true;
    }

    /// <summary>
    /// Parse a clang/Make-style depfile into its prerequisite paths. The format is
    /// <c>target: dep1 dep2 \</c> with line continuations, spaces inside a path escaped as
    /// <c>\ </c>, and the target (everything up to the first unescaped <c>:</c>) dropped.
    /// </summary>
    private static IEnumerable<string> ParseDepfile(string depFile)
    {
        string text;
        try { text = File.ReadAllText(depFile); }
        catch { yield break; }

        // Join line continuations, then drop the "target:" prefix. The target is itself a Windows
        // path, so its drive-letter colon ("C:\...") must NOT be mistaken for the rule separator:
        // split on the first colon that is followed by whitespace (or end), which is the real
        // "target: deps" colon. clang emits a single rule, so the first such colon is enough.
        text = text.Replace("\\\r\n", " ").Replace("\\\n", " ").Replace("\r", " ").Replace("\n", " ");
        var colon = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ':' && (i + 1 >= text.Length || text[i + 1] == ' ' || text[i + 1] == '\t'))
            {
                colon = i;
                break;
            }
        }
        if (colon >= 0) text = text.Substring(colon + 1);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == ' ')
            {
                sb.Append(' '); // escaped space inside a path
                i++;
            }
            else if (c == ' ' || c == '\t')
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>
    /// True when <paramref name="output"/> exists and is at least as new as every input in
    /// <paramref name="inputs"/> (each of which must exist). Used to decide whether a link/archive
    /// step can be skipped: any newer object, library or manifest makes the output stale.
    /// </summary>
    private static bool IsOutputFresh(string output, IEnumerable<string> inputs)
    {
        if (!File.Exists(output)) return false;
        var outTime = File.GetLastWriteTimeUtc(output);
        foreach (var input in inputs)
        {
            if (string.IsNullOrEmpty(input)) continue;
            if (!File.Exists(input)) return false;
            if (File.GetLastWriteTimeUtc(input) > outTime) return false;
        }
        return true;
    }

    /// <summary>Build one library project to a static .lib and return its path.</summary>
    private static async Task<string> BuildLibraryAsync(
        string libRoot, Toolchain tc, string sdkInclude, RxdkOptimizeMode optimize,
        Action<string>? log, CancellationToken ct, RxdkProjectManifest? knownManifest = null)
    {
        // knownManifest is the resolved manifest for a top-level library (native .vcxproj flow,
        // which has no rxdk.project.json on disk); a projectReference dep reads its own.
        var manifest = knownManifest ?? ReadManifest(libRoot);
        if (manifest.Type != RxdkProjectKind.Library)
            throw new InvalidOperationException(
                $"projectReferences must point to type:library projects - {manifest.Name} is not one");
        var outDir = SdkLayout.GetProjectOutDir(libRoot, manifest);
        Directory.CreateDirectory(outDir);

        var includeArgs = new List<string> { "-I", sdkInclude };
        includeArgs.AddRange(ResolveIncludeArgs(libRoot, manifest.IncludePaths, "includePaths"));
        includeArgs.AddRange(ResolveIncludeArgs(libRoot, manifest.PublicIncludePaths, "publicIncludePaths"));
        includeArgs.AddRange(GetTransitivePublicIncludeArgs(libRoot, manifest));
        var defineArgs = ProjectDefineArgs(manifest);

        log?.Invoke($"== Building library {manifest.Name} ==");
        var (objs, _, anyRecompiled) = await CompileProjectSourcesAsync(
            libRoot, manifest, tc, outDir, includeArgs, defineArgs, optimize, log, ct);
        if (objs.Count == 0)
            throw new InvalidOperationException($"Library {manifest.Name} has no sources to archive");

        var lib = Path.Combine(outDir, $"{manifest.Name}.lib");
        // Incremental: if no object was recompiled and the archive is already newer than all of
        // them, it is current -- skip the re-archive so a dependent build can also short-circuit.
        if ((manifest.Incremental ?? true) && !anyRecompiled && File.Exists(lib)
            && IsOutputFresh(lib, objs))
        {
            log?.Invoke($"Up to date {lib}");
            return lib;
        }
        if (File.Exists(lib)) File.Delete(lib);
        // A library with hundreds of objects overflows the Windows command line
        // ("The filename or extension is too long"). llvm-ar reads an @response-file,
        // one token per line, exactly like the clang driver in XdkLink. Mirror it.
        var arRsp = Path.Combine(outDir, "archive_objs.rsp");
        await File.WriteAllLinesAsync(arRsp, objs.Select(o => "\"" + o.Replace('\\', '/') + "\""), ct);
        var arArgs = new List<string>(tc.ArchiveSubcommand()) { "rcs", lib, "@" + arRsp };
        var ar = await ProcessRunner.RunStreamedAsync(tc.ArchiverExe, arArgs, log, ct: ct);
        if (!ar.Success) throw new InvalidOperationException($"Archiving {lib} failed (exit {ar.ExitCode})");
        log?.Invoke($"Archived {lib}");
        return lib;
    }

    // ---- main ----

    public static async Task<BuildResult> BuildAsync(BuildOptions opts, CancellationToken ct = default)
    {
        var log = opts.Log;
        try
        {
            var projectRoot = Path.GetFullPath(opts.ProjectRoot);
            var manifest = RxdkManifestLoader.Load(projectRoot)
                .ResolveConfiguration(opts.Configuration);
            var projectName = manifest.Name;
            var outDir = SdkLayout.GetProjectOutDir(projectRoot, manifest);
            Directory.CreateDirectory(outDir);
            // Optimize follows the config's debug/release flag, regardless of the configuration's
            // own name. Which SDK library variant links is unrelated -- see BuildOptions.Configuration.
            var optimize = DeriveOptimize(manifest);

            // Prerequisite preflight: on a machine where nothing has been set up yet (a user who
            // just opened a sample and hit Build), fail with one clear, actionable message instead
            // of a cryptic header/tool-not-found error partway through. Only the tools actually
            // needed for THIS build are required — a library / compile-only build never links, so
            // it doesn't need imagebld.
            {
                var missing = new List<string>();
                if (!File.Exists(Path.Combine(SdkLayout.GetSdkIncludeDir(), "d3d8.h")))
                    missing.Add("SDK headers/libraries");
                // Either backend satisfies the toolchain prerequisite: the opt-in LLVM fork
                // (RXDK_LLVM env / managed install) or the vendored Zig.
                if (!LlvmRuntime.IsAvailable()
                    && await ZigRuntime.ResolveZigExecutableAsync(opts.ZigExecutable, ct) is null)
                    missing.Add("compiler toolchain (Zig or RXDK LLVM)");
                var needsHostTools = !opts.CompileOnly && !manifest.IsLibrary;
                if (needsHostTools && !File.Exists(RxdkPaths.ResolveHostTool("imagebld")))
                    missing.Add("host tools (imagebld, xdvdfs, …)");
                if (missing.Count > 0)
                {
                    var msg = $"RXDK isn't set up yet — missing: {string.Join("; ", missing)}. " +
                              "Open the RXDK tool window (View > Other Windows > RXDK) and click " +
                              "\"Install Prerequisites\", then build again.";
                    log?.Invoke(msg);
                    return new BuildResult(false, outDir, msg);
                }
            }

            // Resource pipeline: compile any .rdf files with the bundler BEFORE the C/C++
            // sources, so the generated Resource.h exists at compile time and the packed .xpr
            // is written (to the out_packedresource path named in the .rdf) for deploy.
            await CompileResourcesAsync(projectRoot, manifest, log, ct);

            // Shader pipeline: assemble .vsh/.psh sources to .xvu/.xpu microcode with xsasm so
            // titles that load precompiled shaders (e.g. "Shaders\\Foo.xvu") find them in media.
            await CompileShadersAsync(projectRoot, manifest, log, ct);

            var sdkInclude = SdkLayout.GetSdkIncludeDir();
            var sdkLib = SdkLayout.GetSdkLibDir();
            if (!Directory.Exists(sdkInclude))
                throw new DirectoryNotFoundException("Missing sdk/include - run RXDK prerequisites (SDK install)");

            // Opt-in LLVM: Toolchain.ResolveAsync picks the RXDK clang/lld fork when it resolves
            // (RXDK_LLVM env / managed install), otherwise the vendored Zig. The rest of the build
            // is backend-agnostic and goes through `tc`.
            var tc = await Toolchain.ResolveAsync(opts.ZigExecutable, null, ct);
            log?.Invoke($"Toolchain: {tc.Name}");

            var sdkLibDir = sdkLib;
            log?.Invoke($"Linking SDK libraries (configuration: {manifest.EffectiveConfiguration.ToString().ToLowerInvariant()})");
            // Library search dirs: the flat SDK lib dir first, then any user libraryPaths.
            var libSearchDirs = new List<string> { sdkLibDir };
            foreach (var rel in manifest.LibraryPaths ?? new())
            {
                if (string.IsNullOrWhiteSpace(rel)) continue;
                var dir = Path.GetFullPath(Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (Directory.Exists(dir)) libSearchDirs.Add(dir);
                else log?.Invoke($"Warning: libraryPath not found: {dir}");
            }
            string? ResolveLib(string name)
            {
                foreach (var dir in libSearchDirs)
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return candidate;
                }
                return null;
            }

            // Referenced library projects, in dependency order. If a dep's .lib is already
            // built (native .vcxproj flow: VS builds the child project first via a
            // ProjectReference), link it directly; otherwise build it now (CLI / no VS).
            var depOrder = GetDependencyOrder(projectRoot, manifest);
            var userLibs = new List<string>();
            foreach (var dep in depOrder)
            {
                var depManifest = ReadManifest(dep);
                var prebuilt = Path.Combine(SdkLayout.GetProjectOutDir(dep, depManifest), $"{depManifest.Name}.lib");
                if (File.Exists(prebuilt))
                {
                    log?.Invoke($"Using prebuilt library {prebuilt}");
                    userLibs.Add(prebuilt);
                }
                else
                {
                    userLibs.Add(await BuildLibraryAsync(dep, tc, sdkInclude, optimize, log, ct, depManifest));
                }
            }

            // Explicit prebuilt .lib files (additionalLibraries), linked verbatim alongside deps.
            foreach (var rel in manifest.AdditionalLibraries ?? new())
            {
                if (string.IsNullOrWhiteSpace(rel)) continue;
                var lib = Path.GetFullPath(Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(lib)) { log?.Invoke($"Linking additional library {lib}"); userLibs.Add(lib); }
                else throw new FileNotFoundException($"additionalLibraries: not found: {lib}");
            }

            // A library root builds to a .lib and stops (no link / imagebld / deploy).
            if (manifest.Type == RxdkProjectKind.Library)
            {
                var lib = await BuildLibraryAsync(projectRoot, tc, sdkInclude, optimize, log, ct, manifest);
                log?.Invoke($"OK: library {projectName} build complete -> {lib}");
                return new BuildResult(true, outDir);
            }

            // Compile this executable's own sources.
            var projectIncludeArgs = new List<string> { "-I", sdkInclude };
            projectIncludeArgs.AddRange(ResolveIncludeArgs(projectRoot, manifest.IncludePaths, "includePaths"));
            projectIncludeArgs.AddRange(ResolveIncludeArgs(projectRoot, manifest.PublicIncludePaths, "publicIncludePaths"));
            projectIncludeArgs.AddRange(GetTransitivePublicIncludeArgs(projectRoot, manifest));
            var projectDefines = ProjectDefineArgs(manifest);

            log?.Invoke($"== Building executable {projectName} ==");
            var (objs, _, anyRecompiled) = await CompileProjectSourcesAsync(
                projectRoot, manifest, tc, outDir, projectIncludeArgs, projectDefines, optimize, log, ct);

            if (opts.CompileOnly)
            {
                log?.Invoke("Compile OK (compileOnly).");
                return new BuildResult(true, outDir);
            }

            // SDK libraries to link: executable's own + every referenced library's, deduped in
            // first-seen order, libkernel forced last so other archives resolve kernel imports.
            var libNames = new List<string>();
            void AddLibName(string n) { if (!string.IsNullOrWhiteSpace(n) && !libNames.Contains(n)) libNames.Add(n); }
            foreach (var n in manifest.Libraries ?? new()) AddLibName(n);
            foreach (var dep in depOrder)
                foreach (var n in ReadManifest(dep).Libraries ?? new()) AddLibName(n);
            if (libNames.Contains("libkernel.lib"))
            {
                libNames.Remove("libkernel.lib");
                libNames.Add("libkernel.lib");
            }

            var isDxt = manifest.Type == RxdkProjectKind.Dxt;
            // A title linking xAPI enters at XapiTitleStartup (which runs XapiInitProcess:
            // per-title/utility drive setup, etc.) rather than the bare libc `start`. The
            // library is named by its exact file, so the Debug build lists libxapid.lib --
            // match either spelling, or a Debug title silently falls back to `start` and
            // skips the whole xAPI init (e.g. no T:/U:/Z: drives).
            var linksXapi = libNames.Contains("libxapi.lib") || libNames.Contains("libxapid.lib");
            var entry = isDxt ? "DxtEntry" : linksXapi ? "XapiTitleStartup" : "start";

            var linkLibs = new List<string>();
            if (isDxt) linkLibs.Add("-Wl,--dynamicbase"); // DXT keeps its base-reloc table.

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

            // Incremental link/package skip: when no object was recompiled, the final product
            // (ISO, or the XBE when createIso=false, or the DXT) may already be current. It is stale
            // only if a linked library, an embedded/deployed asset, or the manifest itself is newer
            // than it -- so gather those as inputs and short-circuit the link + imagebld + pack tail
            // when the product out-dates them all. (A recompiled object always forces the relink.)
            if ((manifest.Incremental ?? true) && !anyRecompiled)
            {
                var linkInputs = new List<string>(objs);
                foreach (var l in linkLibs)
                    if (!l.StartsWith("-", StringComparison.Ordinal)) linkInputs.Add(l);
                linkInputs.Add(Path.Combine(projectRoot, RxdkManifestLoader.ManifestFileName));
                foreach (var item in manifest.Embed ?? new())
                    if (!string.IsNullOrEmpty(item.Path))
                        linkInputs.Add(Path.Combine(projectRoot, item.Path.Replace('/', Path.DirectorySeparatorChar)));
                foreach (var e in PackXiso.ResolveDeployPaths(projectRoot, manifest.DeployPaths))
                    linkInputs.Add(e.Source);

                var product = isDxt
                    ? Path.GetFullPath(Path.Combine(outDir, $"{projectName}.dxt"))
                    : (manifest.CreateIso ?? true)
                        ? Path.GetFullPath(Path.Combine(outDir, "XISO", $"{projectName}.iso"))
                        : Path.GetFullPath(Path.Combine(outDir, $"{projectName}.xbe"));

                if (IsOutputFresh(product, linkInputs))
                {
                    log?.Invoke($"Up to date {product}");
                    log?.Invoke($"OK: {projectName} build up to date -> {outDir}");
                    return new BuildResult(true, outDir);
                }
            }

            var exe = Path.GetFullPath(Path.Combine(outDir, $"{projectName}.exe"));
            var linkResult = await XdkLink.LinkAsync(
                tc, objs, linkLibs, exe, entry,
                OptimizeMode.KeepsDebugInfo(optimize), log, ct);
            if (!linkResult.Success)
                throw new InvalidOperationException($"Link failed (exit {linkResult.ExitCode})");
            log?.Invoke($"Linked {exe}");

            // A DXT is a raw flat PE, not an XBE.
            if (isDxt)
            {
                var imageBldDxt = RxdkPaths.ResolveHostTool("imagebld");
                if (!File.Exists(imageBldDxt)) throw new FileNotFoundException($"Missing {imageBldDxt}");
                var dxt = await ImageBuild.BuildDxtAsync(
                    exe, Path.GetFullPath(Path.Combine(outDir, $"{projectName}.dxt")), imageBldDxt, log, ct);
                log?.Invoke($"Built {dxt}");
                log?.Invoke($"OK: DXT {projectName} build complete -> {outDir}");
                return new BuildResult(true, outDir);
            }

            var imageBldPath = RxdkPaths.ResolveHostTool("imagebld");
            var xdvdfsPath = RxdkPaths.ResolveHostTool("xdvdfs");
            if (!File.Exists(imageBldPath)) throw new FileNotFoundException($"Missing {imageBldPath}");
            if (!File.Exists(xdvdfsPath)) throw new FileNotFoundException($"Missing {xdvdfsPath}");

            var insertFiles = new List<string>();
            foreach (var item in manifest.Embed ?? new())
            {
                if (string.IsNullOrEmpty(item.Path) || string.IsNullOrEmpty(item.Name)) continue;
                var embedPath = Path.Combine(projectRoot, item.Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(embedPath))
                {
                    insertFiles.Add($"{Path.GetFullPath(embedPath)},{item.Name},R");
                    log?.Invoke($"Embedding {item.Name} from {embedPath}");
                }
                else
                {
                    log?.Invoke($"Warning: embed path not found: {embedPath}");
                }
            }

            var xbe = await ImageBuild.BuildXbeAsync(exe, imageBldPath, manifest.ImageBuild, insertFiles, projectRoot, log, ct);
            log?.Invoke($"Built {xbe}");

            if (manifest.CreateIso ?? true)
            {
                var stageFiles = PackXiso.ResolveDeployPaths(projectRoot, manifest.DeployPaths, log);
                if (stageFiles.Count > 0)
                    log?.Invoke($"Staging {stageFiles.Count} deployPaths file(s) into ISO");
                try
                {
                    var iso = await PackXiso.PackAsync(xbe, projectName, outDir, xdvdfsPath, stageFiles, log, ct);
                    log?.Invoke($"Packed {iso}");
                }
                catch (Exception err)
                {
                    // Warning-and-continue leaves whatever stale ISO is already on disk, so the
                    // title boots an old image and dies looking for media that is present in the
                    // source tree. A read-only staged file is enough to trigger it.
                    throw new InvalidOperationException(
                        $"ISO pack failed for {projectName}: {err.Message}", err);
                }
            }
            else
            {
                log?.Invoke("ISO creation disabled (createIso=false); .xbe is the final output.");
            }

            log?.Invoke($"OK: {projectName} build complete -> {outDir}");
            return new BuildResult(true, outDir);
        }
        catch (Exception err)
        {
            return new BuildResult(false, "", err.Message);
        }
    }
}
