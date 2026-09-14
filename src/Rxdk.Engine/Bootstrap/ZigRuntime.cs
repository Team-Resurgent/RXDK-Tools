using System.IO.Compression;
using System.Runtime.InteropServices;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Bootstrap;

/// <summary>
/// Manages the RXDK-pinned Zig toolchain used to build titles. C# port of RXDK-VSCode
/// zigRuntime.ts. The SDK libraries are built and tested against exactly ZIG_VERSION, so the
/// managed install is preferred over any zig on PATH (a different Clang can diverge in
/// codegen/predefined macros).
/// </summary>
public static class ZigRuntime
{
    public const string ZigVersion = "0.16.0";
    private const string ZigDownloadPage = "https://ziglang.org/download/";

    private static string ZigExe => OperatingSystem.IsWindows() ? "zig.exe" : "zig";

    /// <summary>
    /// Zig release archives are named arch-first: zig-x86_64-linux-0.16.0.tar.xz.
    /// The older os-first names (zig-linux-x86_64-…) 404.
    /// </summary>
    private static string ArchiveBaseName
    {
        get
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64";
            if (OperatingSystem.IsWindows()) return $"zig-x86_64-windows-{ZigVersion}";
            if (OperatingSystem.IsMacOS()) return $"zig-{arch}-macos-{ZigVersion}";
            return $"zig-{arch}-linux-{ZigVersion}";
        }
    }

    private static string ArchiveFileName =>
        OperatingSystem.IsWindows() ? $"{ArchiveBaseName}.zip" : $"{ArchiveBaseName}.tar.xz";

    private static IEnumerable<string> InstalledZigCandidates()
    {
        var root = RxdkPaths.GetZigInstallRoot();
        yield return Path.Combine(root, ZigVersion, ArchiveBaseName, ZigExe);
        yield return Path.Combine(root, ZigVersion, ZigExe);
    }

    /// <summary>
    /// Resolve the zig executable to use for a build. Order: explicit override → RXDK_ZIG env →
    /// managed pinned install → <c>zig</c> on PATH (pinned version only). Returns null if none
    /// is available.
    /// </summary>
    public static async Task<string?> ResolveZigExecutableAsync(
        string? overridePath = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var resolved = Path.GetFullPath(overridePath);
            if (!File.Exists(resolved))
                throw new FileNotFoundException($"Zig not found: {resolved}");
            return resolved;
        }

        var env = Environment.GetEnvironmentVariable("RXDK_ZIG");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var resolved = Path.GetFullPath(env.Trim());
            if (!File.Exists(resolved))
                throw new FileNotFoundException($"RXDK_ZIG points to missing file: {resolved}");
            return resolved;
        }

        foreach (var candidate in InstalledZigCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Fallback: `zig` on PATH — but ONLY when it is exactly the pinned version. A different
        // Zig bundles a different Clang, which diverges from the SDK. Process.Start throws when
        // `zig` is not on PATH (Linux: "No such file or directory"); that is "not installed",
        // not a build failure.
        try
        {
            var probe = await ProcessRunner.RunAsync("zig", new[] { "version" }, ct: ct);
            if (probe.Success && probe.StdOut.Trim().Split('\n')[0].Trim() == ZigVersion)
                return "zig";
        }
        catch
        {
            /* no PATH zig */
        }
        return null;
    }

    public static async Task<bool> IsInstalledAsync(CancellationToken ct = default) =>
        await ResolveZigExecutableAsync(ct: ct) is not null;

    public static async Task<string?> GetVersionLineAsync(CancellationToken ct = default)
    {
        var zig = await ResolveZigExecutableAsync(ct: ct);
        if (zig is null) return null;
        var r = await ProcessRunner.RunAsync(zig, new[] { "version" }, ct: ct);
        return r.Success ? r.StdOut.Trim().Split('\n')[0].Trim() : null;
    }

    /// <summary>
    /// Download + install the pinned Zig into the managed root. Returns the resolved zig path.
    /// Does not mutate PATH — the build resolves Zig by absolute path.
    /// </summary>
    public static async Task<string> InstallAsync(Action<string>? log = null, CancellationToken ct = default)
    {
        foreach (var candidate in InstalledZigCandidates())
        {
            if (File.Exists(candidate))
            {
                log?.Invoke($"RXDK: Zig {ZigVersion} already installed at {candidate}");
                return candidate;
            }
        }

        var url = $"{ZigDownloadPage}{ZigVersion}/{ArchiveFileName}";
        var installRoot = Path.Combine(RxdkPaths.GetZigInstallRoot(), ZigVersion);
        var extractDir = Path.Combine(installRoot, "extract");
        var archivePath = Path.Combine(Path.GetTempPath(), $"rxdk-{ArchiveFileName}");

        Directory.CreateDirectory(installRoot);
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);

        log?.Invoke($"RXDK: downloading Zig {ZigVersion} from {url}");
        await DownloadFile.DownloadToPathAsync(url, archivePath, progress: null, ct: ct);

        log?.Invoke($"RXDK: extracting Zig to {installRoot}");
        Directory.CreateDirectory(extractDir);
        await ExtractArchiveAsync(archivePath, extractDir, ct);

        var nestedDir = Path.Combine(extractDir, ArchiveBaseName);
        var binDir = Path.Combine(installRoot, ArchiveBaseName);
        if (File.Exists(Path.Combine(nestedDir, ZigExe)))
        {
            if (Directory.Exists(binDir))
                Directory.Delete(binDir, recursive: true);
            Directory.Move(nestedDir, binDir);
        }
        else if (File.Exists(Path.Combine(extractDir, ZigExe)))
        {
            File.Copy(Path.Combine(extractDir, ZigExe), Path.Combine(installRoot, ZigExe), overwrite: true);
        }
        else
        {
            throw new InvalidDataException("Zig archive did not contain the expected executable layout.");
        }

        try { Directory.Delete(extractDir, recursive: true); } catch { /* ignore */ }
        try { File.Delete(archivePath); } catch { /* ignore */ }

        var zig = await ResolveZigExecutableAsync(ct: ct)
            ?? throw new InvalidOperationException(
                $"Zig {ZigVersion} was not detected after installation.");
        EnsureUnixExecutable(zig);
        log?.Invoke($"RXDK: Zig {ZigVersion} ready ({zig})");
        return zig;
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destDir, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
            return;
        }

        ProcessResult tar;
        try
        {
            tar = await ProcessRunner.RunAsync("tar", new[] { "-xf", archivePath, "-C", destDir }, ct: ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to run tar to unpack the Zig archive: {ex.Message}", ex);
        }

        if (!tar.Success)
        {
            var detail = string.IsNullOrWhiteSpace(tar.StdErr) ? tar.StdOut : tar.StdErr;
            throw new InvalidOperationException(
                "Failed to extract the Zig archive. On Linux GNU tar needs xz " +
                $"(Arch: pacman -S xz; Debian/Ubuntu: apt install xz-utils). {detail}".Trim());
        }
    }

    private static void EnsureUnixExecutable(string filePath)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var mode = File.GetUnixFileMode(filePath);
            const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & execute) == 0)
                File.SetUnixFileMode(filePath, mode | UnixFileMode.UserRead | UnixFileMode.UserWrite | execute
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        catch
        {
            /* ignore */
        }
    }
}
