using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rxdk.Engine.Bootstrap;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Build;

/// <summary>
/// Builds an RXDK SDK library (libc / libxapi / libd3d8 / …) from a JSON manifest, with the RXDK
/// LLVM toolchain, as a drop-in replacement for the zig build system (build.zig + build/*.zig +
/// per-lib build.zig/sources.zig). The manifest captures exactly what the zig build did — the
/// per-lib flag sets, include dirs and source "batches" — so the emitted clang argv is
/// byte-for-byte the same as <c>zig build &lt;lib&gt; --verbose</c> in LLVM mode, and the objects
/// (and the packed .lib) come out byte-identical. See docs/llvm-toolchain-plan.md.
///
/// The reproduction recipe (from build/compile_c.zig + coff_lib.zig), which this matches exactly:
///   cwd = repo root
///   clang[++]  -march=pentium3  --target=&lt;triple&gt;  -c -o build-out/obj/&lt;sub&gt;/&lt;stem&gt;.o
///              &lt;batch-flags…&gt;  &lt;opt&gt;  -isystem &lt;resource&gt;  -I&lt;inc&gt;…  &lt;abs-src&gt;
///   llvm-lib /NOLOGO /OUT:build-out/lib/&lt;name&gt;.lib @&lt;rsp&gt;
/// where &lt;stem&gt; is the source path with '/','\\','.',':',' ' → '_' (matching zig's uniqueStem),
/// obj/include paths are repo-relative and the source path is absolute (matching zig's addFileArg).
/// </summary>
public static class SdkLibBuild
{
    // ---- manifest model ----

    public sealed class LibManifest
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>Target ABI: "gnu" (i686-pc-windows-gnu, most libs) or "msvc"
        /// (i686-pc-windows-msvc, libxnet/libxonline).</summary>
        [JsonPropertyName("triple")] public string Triple { get; set; } = "gnu";
        [JsonPropertyName("batches")] public List<Batch> Batches { get; set; } = new();
        /// <summary>For an import library (libkernel/libxbdm): generate the lib from a decorated
        /// .def instead of compiling sources. Mutually exclusive with batches.</summary>
        [JsonPropertyName("import")] public ImportSpec? Import { get; set; }
    }

    public sealed class ImportSpec
    {
        /// <summary>Repo-relative module-definition file.</summary>
        [JsonPropertyName("def")] public string Def { get; set; } = "";
        /// <summary>Librarian /machine (default x86).</summary>
        [JsonPropertyName("machine")] public string Machine { get; set; } = "x86";
    }

    public sealed class Batch
    {
        /// <summary>true → clang++ (C++), false → clang (C / .s assembly).</summary>
        [JsonPropertyName("cpp")] public bool Cpp { get; set; }
        /// <summary>Object output subdir under build-out/obj (matches zig's out_subdir, so member
        /// names in the archive line up).</summary>
        [JsonPropertyName("outSubdir")] public string OutSubdir { get; set; } = "";
        /// <summary>The exact per-batch compile flags between the target triple and the opt flag
        /// (everything the zig build passed as `flags`). NOT including -march/--target/-c/-o/-O*/
        /// -isystem/-I — those are added around this list.</summary>
        [JsonPropertyName("flags")] public List<string> Flags { get; set; } = new();
        /// <summary>Optional per-batch opt-flag override for RELEASE (ReleaseSmall) builds, when the
        /// zig build forced something other than -Os (e.g. libd3d8's se/mpintr.cpp is built -O2 to
        /// dodge an -Os miscompile — see rxdk-msvc-port-hazards / the release-miscompile notes).
        /// Null → the standard -Os.</summary>
        [JsonPropertyName("optRelease")] public string? OptRelease { get; set; }
        /// <summary>Optional per-batch opt-flag override for DEBUG builds, when zig forced something
        /// other than -O0 (e.g. __asm-block files like libxgraphics swizzler that don't compile at
        /// -O0 and are built ≥ -O2 in both configs). Null → the standard -O0.</summary>
        [JsonPropertyName("optDebug")] public string? OptDebug { get; set; }
        [JsonPropertyName("includeDirs")] public List<string> IncludeDirs { get; set; } = new();
        [JsonPropertyName("sources")] public List<string> Sources { get; set; } = new();
    }

    // ---- build ----

    /// <summary>Maps the config's optimize level to the single opt flag zig passed (optFlag in
    /// build.zig): Debug -O0, ReleaseSmall -Os. (ReleaseSafe -O2 / ReleaseFast -O3 kept for
    /// completeness; the dist ships Debug + ReleaseSmall.)</summary>
    public static string OptFlag(RxdkOptimizeMode optimize) => optimize switch
    {
        RxdkOptimizeMode.Debug => "-O0",
        RxdkOptimizeMode.ReleaseSafe => "-O2",
        RxdkOptimizeMode.ReleaseFast => "-O3",
        RxdkOptimizeMode.ReleaseSmall => "-Os",
        _ => "-Os",
    };

    /// <summary>Fixed timestamp for reproducible builds. Pinned for __DATE__/__TIME__ determinism
    /// (this clang does NOT honor it for the COFF object TimeDateStamp — that is zeroed explicitly,
    /// see <see cref="ZeroCoffTimestamp"/>). Value: 2024-01-01T00:00:00Z.</summary>
    public const string ReproEpoch = "1704067200";

    private static readonly Dictionary<string, string> ReproEnv =
        new() { ["SOURCE_DATE_EPOCH"] = ReproEpoch };

    /// <summary>
    /// Zero the COFF header TimeDateStamp (a little-endian uint32 at byte offset 4) that this
    /// clang stamps with the wall-clock build time — the one source of run-to-run non-determinism
    /// in an otherwise byte-identical object (proven timestamp-masked vs the zig build). Zeroing it
    /// makes every engine build of a lib bit-for-bit reproducible.
    /// </summary>
    private static void ZeroCoffTimestamp(string objPath)
    {
        try
        {
            using var fs = new FileStream(objPath, FileMode.Open, FileAccess.ReadWrite);
            if (fs.Length < 8) return;
            fs.Seek(4, SeekOrigin.Begin);
            fs.Write(new byte[] { 0, 0, 0, 0 }, 0, 4);
        }
        catch { /* leave the timestamp if the object can't be rewritten */ }
    }

    private static string TargetTriple(string abi) =>
        abi.Equals("msvc", StringComparison.OrdinalIgnoreCase) ? "i686-pc-windows-msvc" : "i686-pc-windows-gnu";

    /// <summary>zig's uniqueStem: sanitize a source path into an object basename.</summary>
    public static string UniqueStem(string src)
    {
        var sb = new StringBuilder(src.Length);
        foreach (var c in src)
            sb.Append(c is '/' or '\\' or '.' or ':' or ' ' ? '_' : c);
        return sb.ToString();
    }

    /// <summary>
    /// Build one SDK library into <c>&lt;repoRoot&gt;/build-out/lib/&lt;name&gt;.lib</c>, matching the
    /// zig build byte-for-byte. Returns the (repo-relative) lib path. Throws on any compile/pack
    /// failure.
    /// </summary>
    public static async Task<string> BuildLibAsync(
        string repoRoot, LibManifest manifest, RxdkOptimizeMode optimize,
        string? llvmOverride = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var root = LlvmRuntime.ResolveRoot(llvmOverride)
            ?? throw new InvalidOperationException(
                "No RXDK LLVM toolchain found (install-llvm, or set RXDK_LLVM). SDK libs build with clang.");
        var resource = LlvmRuntime.ResourceInclude(root);
        var defaultOpt = OptFlag(optimize);
        var triple = TargetTriple(manifest.Triple);

        repoRoot = Path.GetFullPath(repoRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, "build-out/lib"));

        // Import library: generate from a decorated .def (libkernel/libxbdm), no compilation.
        // Mirrors libs/lib{kernel,xbdm}/build.zig: llvm-lib /NOLOGO /machine:x86 /def: /out:.
        if (manifest.Import is { } imp)
        {
            var importLibRel = $"build-out/lib/{manifest.Name}.lib";
            var impArgs = new[]
            {
                "/NOLOGO", $"/machine:{imp.Machine}", $"/def:{imp.Def}", $"/OUT:{importLibRel}",
            };
            var ir = await ProcessRunner.RunStreamedAsync(
                LlvmRuntime.LibExe(root), impArgs, log, workingDirectory: repoRoot, ct: ct, extraEnv: ReproEnv);
            if (!ir.Success)
                throw new InvalidOperationException($"Import lib {manifest.Name} failed (exit {ir.ExitCode})");
            log?.Invoke($"Built {importLibRel} (import lib from {imp.Def})");
            return importLibRel;
        }

        var objRelPaths = await CompileBatchesAsync(
            repoRoot, root, resource, triple, manifest.Batches, optimize, defaultOpt, log, ct);

        var libRel = await ArchiveAsync(repoRoot, root, manifest.Name, objRelPaths, log, ct);
        log?.Invoke($"Built {libRel} ({objRelPaths.Count} objects)");
        return libRel;
    }

    /// <summary>
    /// Archive objects into build-out/lib/&lt;name&gt;.lib with the MSVC librarian, mirroring
    /// build/coff_lib.zig: an @rsp of quoted, CRLF-separated object paths (in the given order), then
    /// llvm-lib /NOLOGO /OUT: @rsp. llvm-lib records each object's path (as given) as the archive
    /// member name, so the rsp lists ABSOLUTE native paths — the same ones zig writes
    /// (obj.getPath(b)) — for a byte-identical archive. Returns the repo-relative lib path.
    /// </summary>
    public static async Task<string> ArchiveAsync(
        string repoRoot, string root, string name, IReadOnlyList<string> objRelPaths,
        Action<string>? log, CancellationToken ct)
    {
        var libRel = $"build-out/lib/{name}.lib";
        var rspRel = $"build-out/lib/{name}.rsp";
        Directory.CreateDirectory(Path.Combine(repoRoot, "build-out/lib"));
        var rsp = new StringBuilder();
        foreach (var obj in objRelPaths)
            rsp.Append('"').Append(Path.GetFullPath(Path.Combine(repoRoot, obj))).Append("\"\r\n");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, rspRel), rsp.ToString(), ct);

        var packArgs = new[] { "/NOLOGO", $"/OUT:{libRel}", $"@{rspRel}" };
        var pr = await ProcessRunner.RunStreamedAsync(
            LlvmRuntime.LibExe(root), packArgs, log, workingDirectory: repoRoot, ct: ct, extraEnv: ReproEnv);
        if (!pr.Success)
            throw new InvalidOperationException($"Archiving {name}.lib failed (exit {pr.ExitCode})");
        return libRel;
    }

    /// <summary>Compile every source in <paramref name="batches"/> to build-out/obj, returning the
    /// repo-relative object paths in build order (used by both a lib build and the loose msvc_lldiv
    /// object). Matches build/compile_c.zig exactly; the caller resolves the toolchain once.</summary>
    public static async Task<List<string>> CompileBatchesAsync(
        string repoRoot, string root, string? resource, string triple,
        IReadOnlyList<Batch> batches, RxdkOptimizeMode optimize, string defaultOpt,
        Action<string>? log, CancellationToken ct)
    {
        var objRelPaths = new List<string>();
        foreach (var batch in batches)
        {
            // Per-batch opt overrides: RELEASE (e.g. -O2 for an -Os-miscompiling TU) or DEBUG
            // (e.g. -O2 for an __asm-block TU that won't compile at -O0). Else the config default.
            var opt = optimize == RxdkOptimizeMode.ReleaseSmall && !string.IsNullOrEmpty(batch.OptRelease) ? batch.OptRelease!
                : optimize == RxdkOptimizeMode.Debug && !string.IsNullOrEmpty(batch.OptDebug) ? batch.OptDebug!
                : defaultOpt;
            foreach (var srcRel in batch.Sources)
            {
                var ext = Path.GetExtension(srcRel);
                // .asm is skipped entirely (as in compile_c.zig); .s goes through the C driver
                // (clang, never clang++) and skips the C/C++ flags but keeps opt + includes.
                if (ext.Equals(".asm", StringComparison.OrdinalIgnoreCase)) continue;
                var isS = ext.Equals(".s", StringComparison.OrdinalIgnoreCase);
                var useCpp = !isS && batch.Cpp;

                var objRel = $"build-out/obj/{batch.OutSubdir}/{UniqueStem(srcRel)}.o";
                objRelPaths.Add(objRel);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repoRoot, objRel))!);

                var compiler = useCpp ? LlvmRuntime.ClangxxExe(root) : LlvmRuntime.ClangExe(root);
                var args = new List<string> { "-march=pentium3", $"--target={triple}", "-c", "-o", objRel };
                if (!isS) args.AddRange(batch.Flags);
                args.Add(opt);
                if (resource is not null) { args.Add("-isystem"); args.Add(resource); }
                foreach (var inc in batch.IncludeDirs) args.Add($"-I{inc}");
                args.Add(Path.GetFullPath(Path.Combine(repoRoot, srcRel)));  // absolute src (zig addFileArg)

                var r = await ProcessRunner.RunStreamedAsync(compiler, args, log, workingDirectory: repoRoot, ct: ct, extraEnv: ReproEnv);
                if (!r.Success)
                    throw new InvalidOperationException($"Compile failed: {srcRel} (exit {r.ExitCode})");
                if (!File.Exists(Path.Combine(repoRoot, objRel)))
                    throw new InvalidOperationException($"Compiler wrote no object for {srcRel}");
                ZeroCoffTimestamp(Path.Combine(repoRoot, objRel));
            }
        }
        return objRelPaths;
    }

    /// <summary>Resolve the toolchain root + resource include (shared setup for a build).</summary>
    public static (string root, string? resource) ResolveToolchain(string? llvmOverride = null)
    {
        var root = LlvmRuntime.ResolveRoot(llvmOverride)
            ?? throw new InvalidOperationException(
                "No RXDK LLVM toolchain found (install-llvm, or set RXDK_LLVM). SDK libs build with clang.");
        return (root, LlvmRuntime.ResourceInclude(root));
    }

    /// <summary>Load a lib manifest from JSON.</summary>
    public static async Task<LibManifest> LoadManifestAsync(string path, CancellationToken ct = default)
    {
        await using var s = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LibManifest>(s, cancellationToken: ct)
               ?? throw new InvalidDataException($"Empty/invalid SDK lib manifest: {path}");
    }
}
