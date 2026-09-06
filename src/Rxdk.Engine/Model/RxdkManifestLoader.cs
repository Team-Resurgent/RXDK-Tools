using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rxdk.Engine.Model;

/// <summary>
/// Loads and parses rxdk.project.json. Mirrors the reads in RXDK-VSCode
/// (stripBom + JSON.parse). Manifests are camelCase with string enums
/// ("executable"/"library"/"dxt", "debug"/"release").
/// </summary>
public static class RxdkManifestLoader
{
    public const string ManifestFileName = "rxdk.project.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Strip a UTF-8 BOM if present (port of xboxSdkPaths.ts stripBom).</summary>
    public static string StripBom(string text) =>
        text.Length > 0 && text[0] == '﻿' ? text[1..] : text;

    /// <summary>Parse a manifest from raw JSON text. Throws on malformed JSON.</summary>
    public static RxdkProjectManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<RxdkProjectManifest>(StripBom(json), JsonOptions)
            ?? throw new InvalidDataException($"{ManifestFileName} parsed to null");
        NormalizeSeparators(manifest);
        return manifest;
    }

    /// <summary>
    /// Rewrite every path in the manifest to forward slashes. Manifests derived from a .vcxproj
    /// (the VS20XX RxdkGenerateManifest target, the samples' generator) carry MSBuild-style
    /// backslashes, and the build only ever converts '/' to the host separator at its call
    /// sites -- so on Linux/macOS a backslash path was a literal backslash filename and the
    /// first source failed to open. Doing it once here, at load, means every consumer sees a
    /// canonical form; Path.Combine/GetFullPath accept '/' on Windows too. Applies recursively
    /// to per-configuration overrides. Non-path strings (defines, library names, test names,
    /// noPreload entries, which are names inside the image) are left alone.
    /// </summary>
    public static void NormalizeSeparators(RxdkProjectManifest m)
    {
        static string? Fix(string? s) => s?.Replace('\\', '/');
        static void FixList(List<string>? list)
        {
            if (list is null) return;
            for (var i = 0; i < list.Count; i++) list[i] = list[i].Replace('\\', '/');
        }

        FixList(m.Sources);
        FixList(m.Resources);
        FixList(m.LibraryPaths);
        FixList(m.AdditionalLibraries);
        FixList(m.ProjectReferences);
        FixList(m.DeployPaths);
        FixList(m.IncludePaths);
        FixList(m.PublicIncludePaths);
        m.OutputDir = Fix(m.OutputDir);
        if (m.Embed is not null)
            foreach (var e in m.Embed) e.Path = e.Path.Replace('\\', '/');
        if (m.ImageBuild is { } ib)
        {
            ib.TitleImage = Fix(ib.TitleImage);
            ib.DefaultSaveImage = Fix(ib.DefaultSaveImage);
            ib.TitleInfo = Fix(ib.TitleInfo);
        }
        if (m.Configurations is not null)
            foreach (var c in m.Configurations.Values) NormalizeSeparators(c);
    }

    /// <summary>Load the manifest at &lt;projectRoot&gt;/rxdk.project.json.</summary>
    public static RxdkProjectManifest Load(string projectRoot)
    {
        var path = Path.Combine(projectRoot, ManifestFileName);
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Load a manifest from an explicit file path (e.g. a build-generated one).</summary>
    public static RxdkProjectManifest LoadFile(string manifestPath) =>
        Parse(File.ReadAllText(manifestPath));

    /// <summary>
    /// Resolve the manifest for a project: an explicit path if given (the native-.vcxproj flow
    /// generates one into out\), else &lt;projectRoot&gt;/rxdk.project.json.
    /// </summary>
    public static RxdkProjectManifest Resolve(string projectRoot, string? manifestPath) =>
        string.IsNullOrEmpty(manifestPath) ? Load(projectRoot) : LoadFile(manifestPath);

    /// <summary>Try to load a manifest; returns null instead of throwing on missing/invalid.</summary>
    public static RxdkProjectManifest? TryLoad(string projectRoot)
    {
        try
        {
            var path = Path.Combine(projectRoot, ManifestFileName);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }
}
