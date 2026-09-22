using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Bootstrap;

/// <summary>Installed-vs-available version snapshot for one RXDK component.</summary>
public sealed record ComponentVersion(string Name, string? Current, string? Available, bool Blocked = false)
{
    /// <summary>A newer version is published than the one installed (both known and differing) AND the
    /// loaded extension is new enough to use it. Blocked components report no update — the user must
    /// update the extension first (see <see cref="Blocked"/>).</summary>
    public bool UpdateAvailable =>
        !Blocked
        && !string.IsNullOrWhiteSpace(Current) && !string.IsNullOrWhiteSpace(Available)
        && !string.Equals(Normalize(Current!), Normalize(Available!), StringComparison.OrdinalIgnoreCase);

    /// <summary>Installed at all (a current version is known).</summary>
    public bool Installed => !string.IsNullOrWhiteSpace(Current);

    // Compare v-insensitively so "v1.0.0" and "1.0.0" don't read as different.
    private static string Normalize(string v) => v.Trim().TrimStart('v', 'V');
}

/// <summary>
/// Reports installed-vs-available versions for the four staged RXDK components (SDK, Docs, Tools,
/// Samples). Git-backed components (SDK/Docs/Samples) carry a root <c>VERSION</c> file: "current"
/// is the staged copy, "available" is the same file read raw from the repo's default branch. The
/// host tools are release-distributed: "current" is the marker written at install, "available" is
/// the latest release's VERSION asset (or tag). Every lookup is best-effort — a component the
/// network can't reach simply reports a null "available" rather than throwing.
/// </summary>
public static class ComponentVersions
{
    private const string ToolsRepo = "Team-Resurgent/RXDK-Tools";

    /// <summary>
    /// Snapshot all four components (network reads run concurrently). When <paramref name="maxVersion"/>
    /// is the loaded extension's version, any component whose available version is strictly newer is
    /// marked <see cref="ComponentVersion.Blocked"/> — its update is withheld until the extension itself
    /// is updated, so a component can never run ahead of the extension that drives it.
    /// </summary>
    public static async Task<IReadOnlyList<ComponentVersion>> GetAllAsync(
        string? maxVersion = null, CancellationToken ct = default)
    {
        var sdk = GitBackedAsync(
            "SDK", RxdkPaths.GetStagedSdkRoot(),
            SdkStaging.GetSdkGitUrl(), SdkStaging.GetSdkGitRef(), ct);
        var docs = GitBackedAsync(
            "Docs", RxdkPaths.GetStagedDocsRoot(),
            DocsStaging.GetDocsGitUrl(), DocsStaging.GetDocsGitRef(), ct);
        var samples = GitBackedAsync(
            "Samples", RxdkPaths.GetStagedSamplesRoot(),
            SamplesStaging.GetSamplesGitUrl(), SamplesStaging.GetSamplesGitRef(), ct);
        var tools = ToolsAsync(ct);
        var llvm = LlvmAsync(ct);

        // Ordered SDK, Docs, LLVM, Tools, Samples to match the extensions' component list.
        var all = await Task.WhenAll(sdk, docs, llvm, tools, samples);
        if (string.IsNullOrWhiteSpace(maxVersion)) return all;
        return all
            // LLVM's "version" is a build timestamp, not semver, and it's a rolling toolchain rather
            // than a gated RXDK component, so the extension-version gate never applies to it.
            .Select(c => c.Name != "LLVM" && RxdkSemver.IsNewer(c.Available, maxVersion)
                ? c with { Blocked = true } : c)
            .ToArray();
    }

    private static async Task<ComponentVersion> GitBackedAsync(
        string name, string stagedRoot, string gitUrl, string? gitRef, CancellationToken ct)
    {
        var current = ReadLocalVersion(stagedRoot);
        var branch = string.IsNullOrWhiteSpace(gitRef) ? "main" : gitRef!.Trim();
        var rawUrl = RawVersionUrl(gitUrl, branch);
        var available = rawUrl is null ? null : await GitHubReleases.TryGetTextAsync(rawUrl, ct);
        return new ComponentVersion(name, current, available);
    }

    private static async Task<ComponentVersion> ToolsAsync(CancellationToken ct)
    {
        var current = HostToolsInstaller.GetInstalledVersion()
                      ?? (HostToolsInstaller.IsInstalled() ? "installed" : null);
        var available = await GitHubReleases.TryGetLatestVersionAsync(ToolsRepo, ct);
        return new ComponentVersion("Tools", current, available);
    }

    private static async Task<ComponentVersion> LlvmAsync(CancellationToken ct)
    {
        // The rolling LLVM toolchain has no semver, so "version" is the release asset's build stamp
        // (GitHub updated_at): current = the stamp recorded at install, available = the live stamp.
        var current = LlvmInstaller.GetInstalledVersion()
                      ?? (LlvmInstaller.IsInstalled() ? "installed" : null);
        var available = await LlvmInstaller.GetAvailableVersionAsync(ct: ct);
        return new ComponentVersion("LLVM", current, available);
    }

    private static string? ReadLocalVersion(string stagedRoot)
    {
        var path = Path.Combine(stagedRoot, "VERSION");
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Turn a github.com clone URL + branch into the raw.githubusercontent.com URL of its root
    /// VERSION file (e.g. https://raw.githubusercontent.com/Team-Resurgent/RXDK-SDK/main/VERSION).
    /// </summary>
    private static string? RawVersionUrl(string gitUrl, string branch)
    {
        const string prefix = "https://github.com/";
        if (!gitUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var slug = gitUrl.Substring(prefix.Length);
        if (slug.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            slug = slug[..^4];
        slug = slug.TrimEnd('/');
        if (slug.Length == 0) return null;
        return $"https://raw.githubusercontent.com/{slug}/{branch}/VERSION";
    }
}
