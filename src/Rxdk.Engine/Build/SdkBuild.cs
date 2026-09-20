using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rxdk.Engine.Build;

/// <summary>
/// Top-level SDK build: compiles + archives every RXDK-Libs library with the LLVM toolchain and
/// stages the public headers, replacing the zig build orchestration (build.zig). Reads
/// build/sdk/sdk.json (lib order, the loose msvc_lldiv object for libcompat, and the header-staging
/// copy operations) plus each lib's build/sdk/&lt;lib&gt;.json. build.ps1 packages the resulting
/// zig-out/{lib,obj,include} into the dist (lib suffixing + libcompat archive + header copy), which
/// is all toolchain-independent file work.
/// </summary>
public static class SdkBuild
{
    public sealed class SdkManifest
    {
        [JsonPropertyName("libs")] public List<string> Libs { get; set; } = new();
        /// <summary>Loose objects compiled but not archived into any lib (e.g. msvc_lldiv, which
        /// build.ps1 packs into libcompat). Same batch shape as a lib batch.</summary>
        [JsonPropertyName("extraObjects")] public List<SdkLibBuild.Batch> ExtraObjects { get; set; } = new();
        /// <summary>The comdat-fix lib (libcompat): specific already-built objects (picolibc math +
        /// msvc_lldiv) archived so an external title link can't lose a COMDAT tie-break — see
        /// build.ps1's Copy-DistCompatLib.</summary>
        [JsonPropertyName("libcompat")] public LibCompatSpec? LibCompat { get; set; }
        [JsonPropertyName("headerExcludeExt")] public List<string> HeaderExcludeExt { get; set; } = new();
        [JsonPropertyName("headers")] public List<HeaderOp> Headers { get; set; } = new();
    }

    public sealed class LibCompatSpec
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "libcompat";
        /// <summary>Repo-relative object paths (already built by the libc/extraObjects steps).</summary>
        [JsonPropertyName("objs")] public List<string> Objs { get; set; } = new();
    }

    public sealed class HeaderOp
    {
        /// <summary>Source directory (recursively copied), or null when this op is a single file.</summary>
        [JsonPropertyName("dir")] public string? Dir { get; set; }
        /// <summary>Source file, or null when this op is a directory.</summary>
        [JsonPropertyName("file")] public string? File { get; set; }
        /// <summary>Destination path relative to zig-out (e.g. "include/xbox").</summary>
        [JsonPropertyName("to")] public string To { get; set; } = "";
    }

    /// <summary>
    /// Build every SDK lib for <paramref name="optimize"/> into zig-out/lib (+ the loose objects into
    /// zig-out/obj), and stage the public headers into zig-out/include. Header staging is
    /// variant-independent; pass <paramref name="stageHeaders"/> = false to skip it on the second
    /// variant of a two-config dist build.
    /// </summary>
    public static async Task BuildAsync(
        string repoRoot, string sdkManifestPath, RxdkOptimizeMode optimize,
        bool stageHeaders = true, string? llvmOverride = null,
        Action<string>? log = null, CancellationToken ct = default)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        var sdk = await LoadAsync(sdkManifestPath, ct);
        var (root, resource) = SdkLibBuild.ResolveToolchain(llvmOverride);
        var sdkDir = Path.GetDirectoryName(Path.GetFullPath(sdkManifestPath))!;

        // 1. Libraries, in dependency order.
        foreach (var lib in sdk.Libs)
        {
            var manifestPath = Path.Combine(sdkDir, lib + ".json");
            var m = await SdkLibBuild.LoadManifestAsync(manifestPath, ct);
            log?.Invoke($"== {lib} ({optimize}) ==");
            await SdkLibBuild.BuildLibAsync(repoRoot, m, optimize, llvmOverride, log, ct);
        }

        // 2. Loose objects (msvc_lldiv → libcompat, packed by build.ps1).
        if (sdk.ExtraObjects.Count > 0)
        {
            log?.Invoke("== loose objects (libcompat inputs) ==");
            await SdkLibBuild.CompileBatchesAsync(
                repoRoot, root, resource, "i686-pc-windows-gnu",
                sdk.ExtraObjects, optimize, SdkLibBuild.OptFlag(optimize), log, ct);
        }

        // 3. libcompat: archive the pre-built picolibc math + msvc_lldiv objects.
        if (sdk.LibCompat is { } lc && lc.Objs.Count > 0)
        {
            var missing = lc.Objs.Where(o => !File.Exists(Path.Combine(repoRoot, o))).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"libcompat inputs missing (build libc/extraObjects first): {string.Join(", ", missing.Take(3))}");
            var libRel = await SdkLibBuild.ArchiveAsync(repoRoot, root, lc.Name, lc.Objs, log, ct);
            log?.Invoke($"Built {libRel} ({lc.Objs.Count} objects)");
        }

        // 4. Public headers (pure file copies; variant-independent).
        if (stageHeaders)
        {
            log?.Invoke("== staging headers ==");
            var excl = new HashSet<string>(sdk.HeaderExcludeExt, StringComparer.OrdinalIgnoreCase);
            foreach (var h in sdk.Headers)
            {
                var dest = Path.Combine(repoRoot, "zig-out", h.To.Replace('/', Path.DirectorySeparatorChar));
                if (h.File is not null)
                {
                    var src = Path.Combine(repoRoot, h.File.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (File.Exists(src)) File.Copy(src, dest, overwrite: true);
                    else log?.Invoke($"Warning: header not found: {h.File}");
                }
                else if (h.Dir is not null)
                {
                    var src = Path.Combine(repoRoot, h.Dir.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(src)) CopyTree(src, dest, excl);
                    else log?.Invoke($"Warning: header dir not found: {h.Dir}");
                }
            }
        }

        log?.Invoke($"OK: SDK build complete ({optimize}) — {sdk.Libs.Count} libs");
    }

    private static void CopyTree(string srcDir, string destDir, HashSet<string> excludeExt)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            if (excludeExt.Contains(Path.GetExtension(file))) continue;
            var rel = Path.GetRelativePath(srcDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    public static async Task<SdkManifest> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var s = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SdkManifest>(s, cancellationToken: ct)
               ?? throw new InvalidDataException($"Empty/invalid SDK manifest: {path}");
    }
}
