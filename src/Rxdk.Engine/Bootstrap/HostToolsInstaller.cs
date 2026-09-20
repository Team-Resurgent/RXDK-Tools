using System.IO.Compression;
using System.Text.RegularExpressions;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Bootstrap;

/// <summary>
/// Downloads the RXDK host tools into the staged tools root. C# port of RXDK-VSCode
/// hostTools.ts installHostTools. Pulls the RXDK-Tools managed bundle (imagebld, xbcp,
/// xbox-launch, xboxdbg-bridge, xbwatson) plus xdvdfs (separate repo) for the current
/// platform. Layout matches the VS Code extension so both share one …/RXDK/tools.
/// </summary>
public static partial class HostToolsInstaller
{
    private const string RxdkToolsRepo = "Team-Resurgent/RXDK-Tools";
    private const string XdvdfsRepo = "Team-Resurgent/xdvdfs";

    /// <summary>
    /// Host tools that must be present for RXDK to build, pack, and deploy. Mirrors
    /// scripts/required-tools.txt / hostTools.ts REQUIRED_HOST_TOOLS.
    /// </summary>
    public static readonly string[] RequiredHostTools =
    {
        "imagebld", "bundler", "xactbld", "xsasm", "xbcp", "xbox-launch", "xboxdbg-bridge", "xbwatson", "xdvdfs",
    };

    /// <summary>Marker file recording which RXDK-Tools release version is installed.</summary>
    private const string VersionMarkerFile = "VERSION";

    /// <summary>True when every required tool exists in the staged tools root.</summary>
    public static bool IsInstalled()
    {
        var root = RxdkPaths.GetStagedToolsRoot();
        return RequiredHostTools.All(t =>
            File.Exists(Path.Combine(root, RxdkPaths.HostToolExecutableName(t))));
    }

    /// <summary>
    /// The installed host-tools version, from the VERSION marker written at install time.
    /// Null when tools aren't installed or predate the marker.
    /// </summary>
    public static string? GetInstalledVersion()
    {
        var path = Path.Combine(RxdkPaths.GetStagedToolsRoot(), VersionMarkerFile);
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [GeneratedRegex(@"(^|/)tools/[^/]+$")]
    private static partial Regex ToolsEntryRegex();

    // xdvdfs ships a per-OS build (windows x64, linux x64, macos x64/arm64). The engine now
    // runs under VS Code on Linux/macOS as well as VS20XX on Windows, so pick the asset for the
    // current platform rather than always the Windows variant. (Upstream ships no arm64 Windows
    // or arm64 Linux build; those hosts fall back to the x64 asset.)
    private static string XdvdfsAssetPrefix()
    {
        if (OperatingSystem.IsWindows()) return "xdvdfs-windows-";
        if (OperatingSystem.IsMacOS())
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                       == System.Runtime.InteropServices.Architecture.Arm64
                ? "xdvdfs-macos-arm64-"
                : "xdvdfs-macos-x64-";
        return "xdvdfs-linux-";
    }

    /// <summary>
    /// Download + extract host tools into the staged root. <paramref name="hostToolsTag"/> /
    /// <paramref name="xdvdfsTag"/> pin releases (null = latest). Returns the tools root.
    /// </summary>
    public static async Task<string> InstallAsync(
        string? hostToolsTag = null,
        string? xdvdfsTag = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var root = RxdkPaths.GetStagedToolsRoot();
        Directory.CreateDirectory(root);

        // Clear any ".old-*" leftovers from a previous update that ran while a tool was in use.
        SweepStaleUpdates(root);

        // 1. RXDK-Tools managed bundle.
        log?.Invoke("Resolving RXDK-Tools release…");
        var toolsRelease = await GitHubReleases.FetchReleaseAsync(RxdkToolsRepo, hostToolsTag, ct);
        var toolsAsset = GitHubReleases.RequireAsset(
            toolsRelease, $"rxdk-managed-{RxdkPaths.ToolRid}.zip", RxdkToolsRepo);
        log?.Invoke($"RXDK: host tools {toolsRelease.TagName} → {root}");
        var wrote = await DownloadAndExtractAsync(
            toolsAsset.BrowserDownloadUrl, root,
            // Files directly under a tools/ dir inside the archive (dist/rxdk-managed-<rid>/tools/*).
            name => ToolsEntryRegex().IsMatch(name),
            "RXDK-Tools", log, ct);
        log?.Invoke($"RXDK: extracted {wrote} file(s) from RXDK-Tools");

        // 2. xdvdfs (separate repo).
        log?.Invoke("Resolving xdvdfs release…");
        var xdvdfsRelease = await GitHubReleases.FetchReleaseAsync(XdvdfsRepo, xdvdfsTag, ct);
        var prefix = XdvdfsAssetPrefix();
        var xdvdfsAsset = xdvdfsRelease.Assets
            .Where(a => a.Name.StartsWith(prefix, StringComparison.Ordinal)
                        && a.Name.EndsWith(".zip", StringComparison.Ordinal)
                        && !a.Name.StartsWith("xdvdfs-fsd-", StringComparison.Ordinal))
            .OrderByDescending(a => a.Name, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"xdvdfs {xdvdfsRelease.TagName} has no asset matching \"{prefix}*.zip\"");
        log?.Invoke($"RXDK: xdvdfs {xdvdfsRelease.TagName} ({xdvdfsAsset.Name})");
        var xdvdfsName = RxdkPaths.HostToolExecutableName("xdvdfs");
        await DownloadAndExtractAsync(
            xdvdfsAsset.BrowserDownloadUrl, root,
            name => PosixBasename(name) == xdvdfsName,
            "xdvdfs", log, ct);

        var missing = RequiredHostTools
            .Where(t => !File.Exists(Path.Combine(root, RxdkPaths.HostToolExecutableName(t))))
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Host tools install incomplete — missing: {string.Join(", ", missing)}");

        // Record which version we installed so the tool window can compare current vs available.
        // Prefer a published VERSION asset (a clean semver like v1.0.0); fall back to the tag.
        try
        {
            var versionAsset = toolsRelease.Assets.FirstOrDefault(
                a => string.Equals(a.Name, "VERSION", StringComparison.OrdinalIgnoreCase));
            var version = versionAsset is not null
                ? await GitHubReleases.GetAssetTextAsync(versionAsset, ct)
                : toolsRelease.TagName;
            if (!string.IsNullOrWhiteSpace(version))
                File.WriteAllText(Path.Combine(root, VersionMarkerFile), version.Trim());
        }
        catch { /* marker is best-effort; never fail the install over it */ }

        log?.Invoke($"RXDK: host tools ready at {root}");
        return root;
    }

    private static async Task<int> DownloadAndExtractAsync(
        string url, string destRoot, Func<string, bool> pick, string label,
        Action<string>? log, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rxdk-hosttool-{Guid.NewGuid():N}.zip");
        try
        {
            log?.Invoke($"Downloading {label}…");
            await DownloadFile.DownloadToPathAsync(url, tmp, progress: null, ct: ct);

            log?.Invoke($"Extracting {label}…");
            var wrote = 0;
            using (var archive = ZipFile.OpenRead(tmp))
            {
                foreach (var entry in archive.Entries)
                {
                    // Directory entries have empty Name; entry.FullName is forward-slashed.
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var normalized = entry.FullName.Replace('\\', '/');
                    if (!pick(normalized)) continue;

                    var target = Path.Combine(destRoot, PosixBasename(normalized));
                    ExtractOverwriteResilient(entry, target);
                    wrote++;
                }
            }
            if (wrote == 0)
                throw new InvalidOperationException($"No matching files found inside the {label} archive");
            return wrote;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Extract <paramref name="entry"/> to <paramref name="target"/>, overwriting even when the
    /// existing file is locked by a running process. Updating tools while a host tool
    /// (xbox-launch, xbWatson, the debug bridge, Neighborhood) is still running would otherwise
    /// fail with a sharing violation. On NTFS a running executable can still be *renamed*, so we
    /// move the in-use file aside and write the new one in its place; the orphaned .old file is
    /// deleted once its process exits (swept on the next install).
    /// </summary>
    private static void ExtractOverwriteResilient(ZipArchiveEntry entry, string target)
    {
        try
        {
            entry.ExtractToFile(target, overwrite: true);
            return;
        }
        catch (IOException) when (File.Exists(target))
        {
            // Fall through to the rename-aside path below.
        }

        var aside = $"{target}.old-{Guid.NewGuid():N}";
        try
        {
            File.Move(target, aside);
        }
        catch (IOException ex)
        {
            throw new IOException(
                $"Could not update {Path.GetFileName(target)} — it is in use (Visual Studio may " +
                "have launched it). Close and reopen Visual Studio, then run the update again.", ex);
        }

        try
        {
            entry.ExtractToFile(target, overwrite: false);
        }
        catch
        {
            try { File.Move(aside, target); } catch { /* leave aside file; original is gone */ }
            throw;
        }
        // The aside file is the still-running tool, so we don't try to delete it here — it's
        // cleaned by SweepStaleUpdates at the start of the next install, once that process exits.
    }

    /// <summary>
    /// Delete stale ".old-*" files left by a previous in-use update (best-effort). Called once at
    /// the start of an install: by then any tool that held a lock last time has almost certainly
    /// exited, so the leftover deletes cleanly. Only touches the ".old-&lt;guid&gt;" copies we create
    /// when overwriting a locked tool; anything still locked is simply skipped and swept next time.
    /// </summary>
    private static void SweepStaleUpdates(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var stale in Directory.EnumerateFiles(root, "*.old-*"))
        {
            try { File.Delete(stale); } catch { /* still locked; swept next install */ }
        }
    }

    private static string PosixBasename(string p)
    {
        var slash = p.LastIndexOf('/');
        return slash >= 0 ? p[(slash + 1)..] : p;
    }
}
