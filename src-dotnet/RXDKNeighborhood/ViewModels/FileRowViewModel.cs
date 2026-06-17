using Avalonia.Media;
using RXDKNeighborhood.Services;

namespace RXDKNeighborhood.ViewModels;

public sealed class FileRowViewModel
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string SizeText { get; init; }
    public required string ModifiedText { get; init; }
    public bool IsDirectory { get; init; }
    public IImage? Icon { get; init; }

    public static FileRowViewModel From(Core.Models.FileEntryModel entry)
    {
        var isDrive = string.Equals(entry.Type, "Drive", StringComparison.OrdinalIgnoreCase);
        var icon = isDrive
            ? ShellIconService.GetDriveIcon()
            : ShellIconService.GetItemIcon(entry.Name, entry.IsDirectory);

        return new FileRowViewModel
        {
            Name = entry.Name,
            Type = entry.Type,
            SizeText = entry.IsDirectory ? "" : FormatSize(entry.Size),
            ModifiedText = entry.Modified?.ToLocalTime().ToString("g") ?? "",
            IsDirectory = entry.IsDirectory,
            Icon = icon,
        };
    }

    private static string FormatSize(ulong size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{size} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
