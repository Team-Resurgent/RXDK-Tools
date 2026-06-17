namespace RXDKNeighborhood.Core.Services;

public static class WirePathService
{
    public static bool TryBuildWirePath(string displayPath, out string wirePath)
    {
        wirePath = string.Empty;
        if (string.IsNullOrWhiteSpace(displayPath))
            return false;

        var slash = displayPath.IndexOf('\\');
        if (slash < 0 || slash + 1 >= displayPath.Length)
            return false;

        var driveLetter = displayPath[slash + 1];
        if (!char.IsLetter(driveLetter))
            return false;

        var remainder = displayPath.Length > slash + 2
            ? displayPath[(slash + 2)..].TrimStart('\\')
            : string.Empty;

        wirePath = string.IsNullOrEmpty(remainder)
            ? $"{char.ToUpperInvariant(driveLetter)}:\\"
            : $"{char.ToUpperInvariant(driveLetter)}:\\{remainder}";

        return true;
    }

    public static string BuildDriveDisplayPath(string consoleName, char driveLetter) =>
        $"{consoleName}\\{char.ToUpperInvariant(driveLetter)}";

    public static string AppendDisplaySegment(string displayPath, string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return displayPath;

        return displayPath.EndsWith('\\')
            ? displayPath + segment
            : displayPath + "\\" + segment;
    }

    public static string? GetParentDisplayPath(string displayPath)
    {
        if (string.IsNullOrWhiteSpace(displayPath))
            return null;

        var lastSlash = displayPath.LastIndexOf('\\');
        if (lastSlash <= 0)
            return null;

        return displayPath[..lastSlash];
    }

    public static string GetConsoleNameFromDisplayPath(string displayPath)
    {
        var slash = displayPath.IndexOf('\\');
        return slash < 0 ? displayPath : displayPath[..slash];
    }

    public static string GetItemDisplayPath(string folderPath, string name) =>
        AppendDisplaySegment(folderPath, name);

    public static bool TryBuildWirePathInFolder(string folderDisplayPath, string name, out string wirePath)
    {
        var display = GetItemDisplayPath(folderDisplayPath, name);
        return TryBuildWirePath(display, out wirePath);
    }

    public static bool IsConsoleRoot(string? displayPath, string? consoleName) =>
        !string.IsNullOrWhiteSpace(displayPath) &&
        !string.IsNullOrWhiteSpace(consoleName) &&
        string.Equals(displayPath, consoleName, StringComparison.OrdinalIgnoreCase);

    public static bool IsDriveListing(string? displayPath, string? consoleName) =>
        IsConsoleRoot(displayPath, consoleName);
}
