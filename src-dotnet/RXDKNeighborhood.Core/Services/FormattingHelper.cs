namespace RXDKNeighborhood.Core.Services;

public static class FormattingHelper
{
    public static string FormatFileSize(ulong size)
    {
        if (size >= 1024UL * 1024 * 1024)
            return $"{size / (1024.0 * 1024 * 1024):0.##} GB";
        if (size >= 1024UL * 1024)
            return $"{size / (1024.0 * 1024):0.##} MB";
        if (size >= 1024UL)
            return $"{size / 1024.0:0.##} KB";
        return $"{size} bytes";
    }

    public static string FormatFileSizeBytes(ulong bytes) => $"{bytes:N0} bytes";

    public static string FormatIpAddress(uint address) =>
        $"{(address >> 24) & 0xFF}.{(address >> 16) & 0xFF}.{(address >> 8) & 0xFF}.{address & 0xFF}";

    public static string FormatDateTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";

    public static string BuildLocationString(string displayPath, string consoleName)
    {
        var slash = displayPath.IndexOf('\\');
        if (slash < 0 || slash + 1 >= displayPath.Length)
            return "";

        var rest = displayPath[(slash + 1)..];
        if (rest.Length == 1 || (rest.Length > 1 && rest[1] == '\\'))
            return $"{rest[0]}:{rest[1..]} (On '{consoleName}')";
        return $"{rest[0]}:{rest[1..]} (On '{consoleName}')";
    }

    public static string GetDriveTypeName(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        return letter switch
        {
            'C' => "Main partition",
            'D' => "Boot drive",
            'E' => "Development drive",
            'S' => "Title root",
            'T' => "Current title",
            'U' => "Current saved game",
            'V' => "Saved game root",
            'X' => "Scratch drive",
            'Y' => "Dashboard",
            >= 'F' and <= 'M' => "Memory unit",
            _ => "Unknown drive type",
        };
    }
}
