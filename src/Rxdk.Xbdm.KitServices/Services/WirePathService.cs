namespace Rxdk.Xbdm.KitServices.Services;

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

        var remainderStart = slash + 2;
        if (remainderStart < displayPath.Length && displayPath[remainderStart] == ':')
            remainderStart++;

        var remainder = remainderStart < displayPath.Length
            ? displayPath[remainderStart..].TrimStart('\\')
            : string.Empty;

        wirePath = string.IsNullOrEmpty(remainder)
            ? $"{char.ToUpperInvariant(driveLetter)}:\\"
            : $"{char.ToUpperInvariant(driveLetter)}:\\{remainder}";

        return true;
    }

    public static string BuildDriveDisplayPath(string consoleName, char driveLetter) =>
        $"{consoleName}\\{char.ToUpperInvariant(driveLetter)}";

    public static string BuildChildDisplayPath(string parentPath, string segment)
    {
        if (string.IsNullOrEmpty(parentPath))
            return segment;

        if (IsConsoleDisplayPath(parentPath) &&
            segment.Length is 1 or 2 &&
            char.IsLetter(segment[0]))
        {
            return BuildDriveDisplayPath(parentPath, char.ToUpperInvariant(segment[0]));
        }

        return AppendDisplaySegment(parentPath, segment);
    }

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

    public static bool IsConsoleDisplayPath(string displayPath) =>
        !string.IsNullOrWhiteSpace(displayPath) && displayPath.IndexOf('\\') < 0;

    public static bool IsVolumeDisplayPath(string displayPath)
    {
        if (string.IsNullOrWhiteSpace(displayPath))
            return false;

        var slash = displayPath.IndexOf('\\');
        if (slash < 0 || slash + 1 >= displayPath.Length)
            return false;

        var rest = displayPath[(slash + 1)..];
        if (rest.Length == 1)
            return char.IsLetter(rest[0]);

        return rest.Length == 2 && char.IsLetter(rest[0]) && rest[1] == ':';
    }

    public static char GetVolumeLetterFromDisplayPath(string displayPath)
    {
        var slash = displayPath.IndexOf('\\');
        if (slash < 0 || slash + 1 >= displayPath.Length)
            return '\0';

        return char.ToUpperInvariant(displayPath[slash + 1]);
    }
}
