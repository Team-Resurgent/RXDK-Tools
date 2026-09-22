using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rxdk.Engine.Bootstrap;

public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    // GitHub restamps this each time the asset is re-uploaded, so for a rolling "latest" release
    // (which has no semver) it serves as the toolchain's build "version".
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
}

/// <summary>
/// GitHub Releases lookup. C# port of hostTools.ts fetchRelease — resolves latest or a
/// pinned tag, forwards GITHUB_TOKEN/GH_TOKEN, and surfaces rate-limit (403/429) clearly.
/// </summary>
public static class GitHubReleases
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        // GitHub requires a User-Agent; also send the versioned Accept header.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RXDK-VS20XX");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }

    // The raw.githubusercontent CDN (and release-asset CDN) cache a URL for minutes, so a version
    // check right after a bump can read a stale VERSION -- which makes the tool window's Refresh look
    // broken. Force a fresh fetch on the version-read paths: a unique query key (distinct CDN cache
    // key) plus no-cache request headers.
    private static string WithCacheBuster(string url)
    {
        var sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}rxdknocache={Guid.NewGuid():N}";
    }

    private static void AddNoCache(HttpRequestMessage request)
    {
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
    }

    public static async Task<GitHubRelease> FetchReleaseAsync(
        string repo, string? tag, CancellationToken ct = default)
    {
        var url = !string.IsNullOrEmpty(tag) && tag != "latest"
            ? $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(tag)}"
            : $"https://api.github.com/repos/{repo}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, WithCacheBuster(url));
        AddNoCache(request);
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                $"GitHub API rate limit reached fetching {repo}. Set GITHUB_TOKEN, or pin a " +
                "release tag, then retry.");
        }
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub API error {(int)response.StatusCode} for {repo}");

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GitHubRelease>(json)
               ?? throw new InvalidDataException($"Empty release JSON for {repo}");
    }

    /// <summary>Find an asset by exact name, or throw with the release tag for context.</summary>
    public static GitHubAsset RequireAsset(GitHubRelease release, string assetName, string repo)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name == assetName);
        return asset ?? throw new InvalidOperationException(
            $"{repo} {release.TagName} has no asset \"{assetName}\"");
    }

    /// <summary>GET an asset's contents as text (used for small marker files like VERSION).</summary>
    public static async Task<string> GetAssetTextAsync(GitHubAsset asset, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithCacheBuster(asset.BrowserDownloadUrl));
        AddNoCache(request);
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(ct)).Trim();
    }

    /// <summary>GET a URL as trimmed text, returning null on any failure (used for raw VERSION reads).</summary>
    public static async Task<string?> TryGetTextAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, WithCacheBuster(url));
            AddNoCache(request);
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                        ?? Environment.GetEnvironmentVariable("GH_TOKEN");
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve the "available" version string for a release-distributed component (e.g. the host
    /// tools). Prefers the text of a published <c>VERSION</c> asset on the latest release; falls
    /// back to the release tag name. Returns null if the release can't be reached.
    /// </summary>
    public static async Task<string?> TryGetLatestVersionAsync(string repo, CancellationToken ct = default)
    {
        try
        {
            var release = await FetchReleaseAsync(repo, null, ct);
            var versionAsset = release.Assets.FirstOrDefault(
                a => string.Equals(a.Name, "VERSION", StringComparison.OrdinalIgnoreCase));
            if (versionAsset is not null)
            {
                var text = await GetAssetTextAsync(versionAsset, ct);
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            return string.IsNullOrWhiteSpace(release.TagName) ? null : release.TagName.Trim();
        }
        catch
        {
            return null;
        }
    }
}
