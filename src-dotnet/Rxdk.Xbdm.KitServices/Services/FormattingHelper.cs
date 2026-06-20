namespace Rxdk.Xbdm.KitServices.Services;

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

    public static string GetDriveTypeName(char letter) => GetVolumeTypeDescription(letter);

    /// <summary>Legacy xbshlext IDS_DRIVETYPE_* strings.</summary>
    public static string GetVolumeTypeDescription(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter is >= 'F' and <= 'M')
            return "Memory Unit";

        return letter switch
        {
            'C' => "Main Partition [Internal]",
            'D' => "Launch Volume [current title]",
            'E' => "Game Development Volume",
            'S' => "Title Volume [root]",
            'T' => "Title Volume [current title]",
            'U' => "Saved Game Volume [current title]",
            'V' => "Saved Game Volume [root]",
            'X' => "Scratch Volume",
            'Y' => "Xbox Dashboard Volume [internal]",
            _ => "Volume",
        };
    }

    public static string GetVolumeDisplayName(char letter) =>
        $"{GetVolumeTypeDescription(letter)} ({char.ToUpperInvariant(letter)})";

    public static int GetVolumeTypeSortKey(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter is >= 'F' and <= 'M')
            return 375;

        return letter switch
        {
            'C' => 376,
            'D' => 377,
            'E' => 378,
            'S' => 379,
            'T' => 380,
            'U' => 381,
            'V' => 382,
            'X' => 383,
            'Y' => 384,
            _ => 385,
        };
    }

    public static char NormalizeDriveLetter(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return '\0';

        return char.ToUpperInvariant(segment[0]);
    }
}
