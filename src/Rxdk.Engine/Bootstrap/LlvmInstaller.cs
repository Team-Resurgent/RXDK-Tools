using System.IO.Compression;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Bootstrap;

/// <summary>
/// Downloads + installs the RXDK LLVM toolchain (the Team-Resurgent clang/lld/llvm-ar fork built
/// for the xboxog target) into the managed install root, so the LLVM title-build path needs no
/// manual <c>RXDK_LLVM</c> env var. Mirrors <see cref="HostToolsInstaller"/>: pull the
/// per-platform <c>xboxog-&lt;os&gt;-&lt;arch&gt;.zip</c>
/// asset from the shared rolling release, unpack it, and leave <see cref="LlvmRuntime"/> to resolve
/// it. Does not mutate PATH — the build resolves clang by absolute path.
/// </summary>
public static class LlvmInstaller
{
    /// <summary>The llvm-project fork whose CI publishes the toolchain. The OG and 360 toolchains
    /// coexist as separate assets on ONE rolling release tagged <c>latest</c>.</summary>
    private const string LlvmRepo = "Team-Resurgent/llvm-project";

    /// <summary>Marker file recording which release the managed toolchain came from.</summary>
    private const string VersionMarkerFile = "VERSION";

    /// <summary>Per-target build-stamp marker asset on the release (a tiny text file the toolchain
    /// build or a maintainer writes, e.g. "2026-09-21 07:00"). Preferred over the zip asset's GitHub
    /// updated_at when present, so the stamp can be set explicitly. Named per target family so the OG
    /// and 360 toolchains on the shared rolling release each carry their own.</summary>
    private const string VersionAssetName = "xbox_version";

    /// <summary>The xboxog asset for this host, e.g. <c>xboxog-windows-x64.zip</c>. Matches the
    /// unpacked dir name <see cref="LlvmRuntime"/> looks for.</summary>
    private static string AssetFileName => $"{ArchiveDirName}.zip";

    private static string ArchiveDirName
    {
        get
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                       == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
            if (OperatingSystem.IsWindows()) return $"xbox-windows-{arch}";
            if (OperatingSystem.IsMacOS()) return $"xbox-macos-{arch}";
            return $"xbox-linux-{arch}";
        }
    }

    /// <summary>True when a managed LLVM toolchain is already resolvable (bin/clang present).</summary>
    public static bool IsInstalled()
    {
        try { return LlvmRuntime.ResolveRoot() is not null; }
        catch { return false; }
    }

    /// <summary>The installed toolchain version, from the VERSION marker, or null.</summary>
    public static string? GetInstalledVersion()
    {
        var path = Path.Combine(RxdkPaths.GetLlvmInstallRoot(), VersionMarkerFile);
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Download + install the LLVM toolchain into the managed root. <paramref name="tag"/> pins a
    /// release (null = the <c>latest</c> rolling release). Returns the resolved toolchain root
    /// (the dir containing bin/clang). Idempotent: skips the download when already installed.
    /// </summary>
    public static async Task<string> InstallAsync(
        string? tag = null, bool force = false, Action<string>? log = null, CancellationToken ct = default)
    {
        var installRoot = RxdkPaths.GetLlvmInstallRoot();

        log?.Invoke("Resolving RXDK LLVM release…");
        // The rolling release is tagged "latest"; GitHub's /releases/latest also resolves it since
        // it is not a prerelease. A pinned tag is honored for reproducibility.
        var release = await GitHubReleases.FetchReleaseAsync(LlvmRepo, tag ?? "latest", ct);
        var available = await ResolveBuildStampAsync(release, ct);

        // Idempotent unless forced: skip only when an install is present AND its recorded build stamp
        // matches what's available. A mismatch (a newer build, or an old/unknown marker) falls through
        // to re-download, so `install-llvm` actually UPDATES. `force` (a reinstall / update-llvm) always
        // re-downloads with no version check.
        var existing = ResolveRootQuiet();
        if (existing is not null && !force)
        {
            var installedStamp = GetInstalledVersion();
            if (installedStamp is not null && available is not null &&
                string.Equals(installedStamp, available, StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"RXDK: LLVM toolchain up to date ({installedStamp}) at {existing}");
                return existing;
            }
            log?.Invoke($"RXDK: updating LLVM toolchain ({installedStamp ?? "unknown"} -> {available ?? "latest"})");
        }

        Directory.CreateDirectory(installRoot);
        var asset = GitHubReleases.RequireAsset(release, AssetFileName, LlvmRepo);
        log?.Invoke($"RXDK: LLVM toolchain {release.TagName} ({asset.Name}) → {installRoot}");

        var extractDir = Path.Combine(installRoot, "extract");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        Directory.CreateDirectory(extractDir);

        var archivePath = Path.Combine(Path.GetTempPath(), $"rxdk-{AssetFileName}");
        try
        {
            log?.Invoke($"RXDK: downloading {asset.Name} (~{asset.BrowserDownloadUrl})");
            await DownloadFile.DownloadToPathAsync(asset.BrowserDownloadUrl, archivePath, progress: null, ct: ct);

            log?.Invoke($"RXDK: extracting to {installRoot}");
            ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
        }
        finally
        {
            try { File.Delete(archivePath); } catch { /* ignore */ }
        }

        // The zip may hold a top-level xboxog-<os>-<arch>/ dir or unpack flat. Normalize to
        // <installRoot>/<ArchiveDirName>/ (the first candidate LlvmRuntime searches).
        var destRoot = Path.Combine(installRoot, ArchiveDirName);
        var nested = Path.Combine(extractDir, ArchiveDirName);
        static bool HasClang(string dir) =>
            File.Exists(Path.Combine(dir, "bin", OperatingSystem.IsWindows() ? "clang.exe" : "clang"));

        if (HasClang(nested))
        {
            if (Directory.Exists(destRoot)) Directory.Delete(destRoot, recursive: true);
            Directory.Move(nested, destRoot);
        }
        else if (HasClang(extractDir))
        {
            if (Directory.Exists(destRoot)) Directory.Delete(destRoot, recursive: true);
            Directory.Move(extractDir, destRoot);
            Directory.CreateDirectory(extractDir); // recreate so the cleanup below is uniform
        }
        else
        {
            throw new InvalidDataException(
                $"LLVM archive {asset.Name} did not contain bin/clang at the expected layout.");
        }

        try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true); } catch { /* ignore */ }

        var root = LlvmRuntime.ResolveRoot()
            ?? throw new InvalidOperationException(
                "LLVM toolchain was not detected after installation.");
        EnsureUnixExecutable(Path.Combine(root, "bin", "clang"));
        EnsureUnixExecutable(Path.Combine(root, "bin", "clang++"));
        EnsureUnixExecutable(Path.Combine(root, "bin", "ld.lld"));
        EnsureUnixExecutable(Path.Combine(root, "bin", "llvm-ar"));

        // Record the build stamp for the tool window's current-vs-available comparison. The rolling
        // release has no semver, so use the asset's GitHub updated_at (restamped on every re-upload)
        // as the toolchain "version": a newer stamp on the release than the marker means an update.
        try
        {
            if (!string.IsNullOrWhiteSpace(available))
                File.WriteAllText(Path.Combine(installRoot, VersionMarkerFile), available);
        }
        catch { /* best-effort */ }

        // The i686 compiler-rt builtins the title link needs (__divdi3/__alloca/…). CI is expected
        // to package libclang_rt.builtins-i386 in the zip; until then, warn clearly rather than
        // silently producing a toolchain that can compile but not link a title.
        if (LlvmRuntime.BuiltinsArchive(root) is null)
        {
            log?.Invoke(
                "RXDK: WARNING — the LLVM toolchain has no libclang_rt.builtins-i386 archive; a " +
                "title LINK will fail with undefined __divdi3/__alloca/… until it is provided " +
                "(RXDK-Libs tools/build-rt-builtins.ps1, or a toolchain build that packages it at " +
                "lib/clang/<ver>/lib/windows/).");
        }

        log?.Invoke($"RXDK: LLVM toolchain ready ({root})");
        return root;
    }

    /// <summary>The build stamp of the toolchain available on the release (the asset's GitHub
    /// updated_at, formatted), for a current-vs-available comparison. Null if it can't be fetched.
    /// A network call, so callers should treat failures as "unknown".</summary>
    public static async Task<string?> GetAvailableVersionAsync(string? tag = null, CancellationToken ct = default)
    {
        try
        {
            var release = await GitHubReleases.FetchReleaseAsync(LlvmRepo, tag ?? "latest", ct);
            return await ResolveBuildStampAsync(release, ct);
        }
        catch { return null; }
    }

    /// <summary>The toolchain build stamp for a release: the explicit per-target marker asset
    /// (<see cref="VersionAssetName"/>) when present, else the zip asset's GitHub updated_at.</summary>
    private static async Task<string?> ResolveBuildStampAsync(GitHubRelease release, CancellationToken ct)
    {
        var marker = release.Assets.FirstOrDefault(
            a => string.Equals(a.Name, VersionAssetName, StringComparison.OrdinalIgnoreCase));
        if (marker is not null)
        {
            try
            {
                var text = await GitHubReleases.GetAssetTextAsync(marker, ct);
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch { /* fall back to updated_at */ }
        }
        var zip = release.Assets.FirstOrDefault(a => a.Name == AssetFileName);
        return zip is null ? null : FormatBuildStamp(zip.UpdatedAt);
    }

    /// <summary>Turn a GitHub ISO-8601 <c>updated_at</c> into a compact, sortable UTC build stamp
    /// (e.g. <c>2026-09-22 00:15</c>). Falls back to the trimmed raw value if it can't be parsed.</summary>
    private static string FormatBuildStamp(string updatedAt)
    {
        if (string.IsNullOrWhiteSpace(updatedAt)) return "";
        if (DateTimeOffset.TryParse(updatedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
            return dt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return updatedAt.Trim();
    }

    private static string? ResolveRootQuiet()
    {
        try { return LlvmRuntime.ResolveRoot(); }
        catch { return null; }
    }

    private static void EnsureUnixExecutable(string filePath)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(filePath)) return;
        try
        {
            var mode = File.GetUnixFileMode(filePath);
            const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & execute) == 0)
                File.SetUnixFileMode(filePath, mode | UnixFileMode.UserRead | UnixFileMode.UserWrite | execute
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        catch { /* ignore */ }
    }
}
