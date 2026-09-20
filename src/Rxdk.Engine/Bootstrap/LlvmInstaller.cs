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

    /// <summary>The xboxog asset for this host, e.g. <c>xboxog-windows-x64.zip</c>. Matches the
    /// unpacked dir name <see cref="LlvmRuntime"/> looks for.</summary>
    private static string AssetFileName => $"{ArchiveDirName}.zip";

    private static string ArchiveDirName
    {
        get
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                       == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
            if (OperatingSystem.IsWindows()) return $"xboxog-windows-{arch}";
            if (OperatingSystem.IsMacOS()) return $"xboxog-macos-{arch}";
            return $"xboxog-linux-{arch}";
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
        string? tag = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var existing = ResolveRootQuiet();
        if (existing is not null)
        {
            log?.Invoke($"RXDK: LLVM toolchain already installed at {existing}");
            return existing;
        }

        var installRoot = RxdkPaths.GetLlvmInstallRoot();
        Directory.CreateDirectory(installRoot);

        log?.Invoke("Resolving RXDK LLVM release…");
        // The rolling release is tagged "latest"; GitHub's /releases/latest also resolves it since
        // it is not a prerelease. A pinned tag is honored for reproducibility.
        var release = await GitHubReleases.FetchReleaseAsync(LlvmRepo, tag ?? "latest", ct);
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

        // Record the release for the tool window's current-vs-available comparison.
        try
        {
            if (!string.IsNullOrWhiteSpace(release.TagName))
                File.WriteAllText(Path.Combine(installRoot, VersionMarkerFile), release.TagName.Trim());
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
