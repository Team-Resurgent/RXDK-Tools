using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Build;

/// <summary>A file to stage into the ISO/deploy tree at a project-relative destination.</summary>
public sealed record StageFileEntry(string Source, string RelativeDest);

/// <summary>
/// Packs a .xbe (+ staged files) into an XISO via the xdvdfs host tool, and resolves a
/// project's deployPaths into stage entries. C# port of RXDK-VSCode packXiso.ts +
/// resolveDeployPaths (xboxDeploy.ts).
/// </summary>
public static class PackXiso
{
    /// <summary>Pack a .xbe (+ any staged files) into an XISO. Returns the .iso path.</summary>
    public static async Task<string> PackAsync(
        string inputXbe, string projectName, string outDir, string toolPath,
        IReadOnlyList<StageFileEntry>? stageFiles = null, Action<string>? log = null,
        CancellationToken ct = default)
    {
        var xbe = Path.GetFullPath(inputXbe);
        if (!File.Exists(xbe)) throw new FileNotFoundException($"xdvdfs: input XBE not found: {xbe}");
        if (string.IsNullOrEmpty(toolPath)) throw new ArgumentException("xdvdfs: toolPath required");

        var resolvedOutDir = Path.GetFullPath(string.IsNullOrEmpty(outDir) ? Path.GetDirectoryName(xbe)! : outDir);
        var packDir = Path.Combine(resolvedOutDir, "Build", projectName);
        var defaultXbe = Path.Combine(packDir, "default.xbe");
        var outputIso = Path.GetFullPath(Path.Combine(resolvedOutDir, "XISO", $"{projectName}.iso"));

        Directory.CreateDirectory(packDir);
        Directory.CreateDirectory(Path.GetDirectoryName(outputIso)!);
        File.Copy(xbe, defaultXbe, overwrite: true);

        var packRoot = Path.GetFullPath(packDir + Path.DirectorySeparatorChar);
        foreach (var entry in stageFiles ?? Array.Empty<StageFileEntry>())
        {
            var src = Path.GetFullPath(entry.Source);
            if (!File.Exists(src)) throw new FileNotFoundException($"StageFile source not found: {src}");
            var dest = Path.GetFullPath(Path.Combine(packDir, entry.RelativeDest.Replace('/', Path.DirectorySeparatorChar)));
            // xdvdfs only packs packDir, so a destination that resolves outside it would be
            // copied but silently left out of the image.
            if (!dest.StartsWith(packRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"StageFile destination escapes the image root: \"{entry.RelativeDest}\" -> {dest}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
        }

        var r = await ProcessRunner.RunStreamedAsync(toolPath, new[] { "pack", packDir, outputIso }, log, ct: ct);
        if (!r.Success) throw new InvalidOperationException($"xdvdfs pack failed (exit {r.ExitCode})");
        return outputIso;
    }

    /// <summary>
    /// Resolve a manifest's deployPaths (project-relative files/dirs) into flat stage entries.
    /// Directories are walked recursively. Missing paths warn and are skipped.
    ///
    /// Each entry follows the XDK "Additional Files" form <c>[Destination=]Source</c>:
    ///   - <c>Source</c> alone: the file/dir lands under its own (cleaned) relative name in the
    ///     image, the same as the XDK deployment tool.
    ///   - <c>Destination=Source</c>: <c>Source</c> (project-relative on the host) is placed at
    ///     <c>Destination</c> (an image-relative path) instead -- for a file that is the full
    ///     target path incl. filename; for a directory it is the target directory the tree lands
    ///     under. The split is on the FIRST '=', which is unambiguous because a Windows source
    ///     path never contains '=' (drive letters use ':').
    /// </summary>
    public static List<StageFileEntry> ResolveDeployPaths(
        string projectRoot, IReadOnlyList<string>? deployPaths, Action<string>? log = null)
    {
        var outList = new List<StageFileEntry>();
        foreach (var rawEntry in deployPaths ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rawEntry)) continue;

            // Split the optional "Destination=" prefix off the source. An empty destination
            // (e.g. "=src") or no '=' at all falls back to the source-derived destination.
            string? explicitDest = null;
            var entry = rawEntry;
            var eq = entry.IndexOf('=');
            if (eq > 0)
            {
                var destPart = CleanRel(entry.Substring(0, eq));
                if (destPart.Length > 0) explicitDest = destPart;
                entry = entry.Substring(eq + 1);
            }

            var cleanRel = entry.Replace('\\', '/').Trim().TrimEnd('/');
            if (cleanRel.Length == 0) continue;
            var localPath = Path.Combine(projectRoot, cleanRel.Replace('/', Path.DirectorySeparatorChar));

            // The destination is always inside the image/remote root: a deploy path may reach
            // outside the project dir (imported XDK samples use "..\media"), but its files still
            // land under their directory's name (or the explicit Destination), matching the XDK
            // deployment tool.
            var destRel = explicitDest ?? CleanRel(cleanRel);
            if (destRel.Length == 0)
                destRel = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar));

            if (File.Exists(localPath))
            {
                // For a file, an explicit Destination is the full target path (incl. filename);
                // the source-derived path already is too.
                outList.Add(new StageFileEntry(localPath, destRel));
                continue;
            }
            if (!Directory.Exists(localPath))
            {
                log?.Invoke($"Warning: deployPaths: not found {localPath}");
                continue;
            }

            var files = Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories).ToList();
            if (files.Count == 0)
            {
                log?.Invoke($"Warning: deployPaths: no files under {localPath}");
                continue;
            }
            foreach (var file in files)
            {
                var relFile = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                // destRel is the target directory the tree lands under (explicit or derived).
                outList.Add(new StageFileEntry(file, destRel.Length > 0 ? $"{destRel}/{relFile}" : relFile));
            }
        }
        return outList;
    }

    /// <summary>Normalise a deploy path fragment to forward slashes and drop empty / "." / ".."
    /// segments so a destination can never escape the image root.</summary>
    private static string CleanRel(string s) =>
        string.Join('/', s.Replace('\\', '/').Split('/')
            .Where(seg => seg.Length > 0 && seg != "." && seg != ".."));
}
