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
///   clang[++]  -march=pentium3  --target=&lt;triple&gt;  -c -o zig-out/obj/&lt;sub&gt;/&lt;stem&gt;.o
///              &lt;batch-flags…&gt;  &lt;opt&gt;  -isystem &lt;resource&gt;  -I&lt;inc&gt;…  &lt;abs-src&gt;
///   llvm-lib /NOLOGO /OUT:zig-out/lib/&lt;name&gt;.lib @&lt;rsp&gt;
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
    }

    public sealed class Batch
    {
        /// <summary>true → clang++ (C++), false → clang (C / .s assembly).</summary>
        [JsonPropertyName("cpp")] public bool Cpp { get; set; }
        /// <summary>Object output subdir under zig-out/obj (matches zig's out_subdir, so member
        /// names in the archive line up).</summary>
        [JsonPropertyName("outSubdir")] public string OutSubdir { get; set; } = "";
        /// <summary>The exact per-batch compile flags between the target triple and the opt flag
        /// (everything the zig build passed as `flags`). NOT including -march/--target/-c/-o/-O*/
        /// -isystem/-I — those are added around this list.</summary>
        [JsonPropertyName("flags")] public List<string> Flags { get; set; } = new();
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
    /// Build one SDK library into <c>&lt;repoRoot&gt;/zig-out/lib/&lt;name&gt;.lib</c>, matching the
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
        var opt = OptFlag(optimize);
        var triple = TargetTriple(manifest.Triple);

        repoRoot = Path.GetFullPath(repoRoot);
        var objRelPaths = new List<string>();

        foreach (var batch in manifest.Batches)
        {
            foreach (var srcRel in batch.Sources)
            {
                var ext = Path.GetExtension(srcRel);
                // .asm is skipped entirely (as in compile_c.zig); .s goes through the C driver
                // (clang, never clang++) and skips the C/C++ flags but keeps opt + includes.
                if (ext.Equals(".asm", StringComparison.OrdinalIgnoreCase)) continue;
                var isS = ext.Equals(".s", StringComparison.OrdinalIgnoreCase);
                var useCpp = !isS && batch.Cpp;

                var objRel = $"zig-out/obj/{batch.OutSubdir}/{UniqueStem(srcRel)}.o";
                objRelPaths.Add(objRel);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repoRoot, objRel))!);

                var compiler = useCpp ? LlvmRuntime.ClangxxExe(root) : LlvmRuntime.ClangExe(root);
                var args = new List<string> { "-march=pentium3", $"--target={triple}", "-c", "-o", objRel };
                if (!isS) args.AddRange(batch.Flags);
                args.Add(opt);
                if (resource is not null) { args.Add("-isystem"); args.Add(resource); }
                foreach (var inc in batch.IncludeDirs) args.Add($"-I{inc}");
                // Source as an absolute path, matching zig's addFileArg(b.path(src)).
                args.Add(Path.GetFullPath(Path.Combine(repoRoot, srcRel)));

                var r = await ProcessRunner.RunStreamedAsync(compiler, args, log, workingDirectory: repoRoot, ct: ct, extraEnv: ReproEnv);
                if (!r.Success)
                    throw new InvalidOperationException($"Compile failed: {srcRel} (exit {r.ExitCode})");
                if (!File.Exists(Path.Combine(repoRoot, objRel)))
                    throw new InvalidOperationException($"Compiler wrote no object for {srcRel}");
                ZeroCoffTimestamp(Path.Combine(repoRoot, objRel));
            }
        }

        // Pack with the MSVC librarian, mirroring build/coff_lib.zig: an @rsp of quoted, CRLF-
        // separated object paths (in build order), then llvm-lib /NOLOGO /OUT: @rsp.
        var libRel = $"zig-out/lib/{manifest.Name}.lib";
        var rspRel = $"zig-out/lib/{manifest.Name}.rsp";
        Directory.CreateDirectory(Path.Combine(repoRoot, "zig-out/lib"));
        var rsp = new StringBuilder();
        foreach (var obj in objRelPaths) rsp.Append('"').Append(obj).Append("\"\r\n");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, rspRel), rsp.ToString(), ct);

        var lib = LlvmRuntime.LibExe(root);
        var packArgs = new[] { "/NOLOGO", $"/OUT:{libRel}", $"@{rspRel}" };
        var pr = await ProcessRunner.RunStreamedAsync(lib, packArgs, log, workingDirectory: repoRoot, ct: ct, extraEnv: ReproEnv);
        if (!pr.Success)
            throw new InvalidOperationException($"Archiving {manifest.Name}.lib failed (exit {pr.ExitCode})");

        log?.Invoke($"Built {libRel} ({objRelPaths.Count} objects)");
        return libRel;
    }

    /// <summary>Load a lib manifest from JSON.</summary>
    public static async Task<LibManifest> LoadManifestAsync(string path, CancellationToken ct = default)
    {
        await using var s = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LibManifest>(s, cancellationToken: ct)
               ?? throw new InvalidDataException($"Empty/invalid SDK lib manifest: {path}");
    }
}
