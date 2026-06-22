namespace Rxdk.XbShellExt.Shell;

internal sealed class LocalUploadEntry
{
    public required string RelativePath { get; init; }
    public required string LocalPath { get; init; }
    public required string WirePath { get; init; }
    public long Size { get; init; }
    public bool IsDirectory { get; init; }
}

internal static class LocalUploadCatalog
{
    public static IReadOnlyList<LocalUploadEntry> Build(
        IEnumerable<string> localPaths,
        string wireFolder)
    {
        var entries = new List<LocalUploadEntry>();
        var wireBase = wireFolder.TrimEnd('\\');
        foreach (var localPath in localPaths)
        {
            if (string.IsNullOrWhiteSpace(localPath))
                continue;

            var fullPath = Path.GetFullPath(localPath);
            if (File.Exists(fullPath))
            {
                AddFile(entries, wireBase, Path.GetFileName(fullPath), fullPath);
                continue;
            }

            if (!Directory.Exists(fullPath))
                continue;

            var rootName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(rootName))
                continue;

            AddDirectory(entries, wireBase, rootName, fullPath);
            AddDirectoryRecursive(entries, wireBase, rootName, fullPath);
        }

        return entries;
    }

    private static void AddDirectoryRecursive(
        List<LocalUploadEntry> entries,
        string wireBase,
        string relativePrefix,
        string localDir)
    {
        foreach (var dir in Directory.GetDirectories(localDir))
        {
            var name = Path.GetFileName(dir);
            var relativePath = $"{relativePrefix}\\{name}";
            AddDirectory(entries, wireBase, relativePath, dir);
            AddDirectoryRecursive(entries, wireBase, relativePath, dir);
        }

        foreach (var file in Directory.GetFiles(localDir))
        {
            var name = Path.GetFileName(file);
            AddFile(entries, wireBase, $"{relativePrefix}\\{name}", file);
        }
    }

    private static void AddDirectory(
        List<LocalUploadEntry> entries,
        string wireBase,
        string relativePath,
        string localDir)
    {
        var normalized = relativePath.Replace('/', '\\');
        entries.Add(new LocalUploadEntry
        {
            RelativePath = normalized,
            LocalPath = localDir,
            WirePath = $"{wireBase}\\{normalized}",
            Size = 0,
            IsDirectory = true,
        });
    }

    private static void AddFile(List<LocalUploadEntry> entries, string wireBase, string relativePath, string localPath)
    {
        var normalized = relativePath.Replace('/', '\\');
        long size = 0;
        try
        {
            size = new FileInfo(localPath).Length;
        }
        catch
        {
        }

        entries.Add(new LocalUploadEntry
        {
            RelativePath = normalized,
            LocalPath = localPath,
            WirePath = $"{wireBase}\\{normalized}",
            Size = size,
            IsDirectory = false,
        });
    }
}
