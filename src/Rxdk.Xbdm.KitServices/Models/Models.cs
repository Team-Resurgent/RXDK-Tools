namespace Rxdk.Xbdm.KitServices.Models;

public class ConsoleInfo : Rxdk.KitConfig.Models.ConsoleInfo;

public class ConsoleRegistryData : Rxdk.KitConfig.Models.ConsoleRegistryData;

public class FileEntryModel
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public ulong Size { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public bool IsDirectory { get; init; }
}

public enum FileClipboardOperation
{
    None,
    Cut,
    Copy,
}

public class FileSelection
{
    public required string ConsoleName { get; init; }
    public required string FolderDisplayPath { get; init; }
    public required IReadOnlyList<FileSelectionItem> Items { get; init; }
}

public class FileSelectionItem
{
    public required string Name { get; init; }
    public required string WirePath { get; init; }
    public bool IsDirectory { get; init; }
}

public enum NavigationNodeKind
{
    Root,
    Console,
    Drive,
    Folder
}

public class NavigationNode
{
    public required NavigationNodeKind Kind { get; init; }
    public required string DisplayPath { get; init; }
    public required string ConsoleName { get; init; }
    public required string Title { get; init; }
    public List<NavigationNode> Children { get; init; } = new();
}
