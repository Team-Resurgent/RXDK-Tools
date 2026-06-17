namespace RXDKNeighborhood.Core.Models;

public enum PropertyTargetKind
{
    Console,
    Drive,
    File,
    Folder,
    MultiFile,
}

public sealed class PropertyContext
{
    public required PropertyTargetKind Kind { get; init; }
    public required string ConsoleName { get; init; }
    public required string Caption { get; init; }
    public ConsoleGeneralInfo? ConsoleGeneral { get; init; }
    public DriveGeneralInfo? Drive { get; init; }
    public FileGeneralInfo? File { get; init; }
}

public sealed class ConsoleGeneralInfo
{
    public required string Name { get; init; }
    public string? IpAddress { get; init; }
    public string? AltIpAddress { get; init; }
    public string? RunningTitle { get; init; }
}

public sealed class DriveGeneralInfo
{
    public required char Letter { get; init; }
    public required string Description { get; init; }
    public required string DriveType { get; init; }
    public ulong TotalBytes { get; init; }
    public ulong FreeBytes { get; init; }

    public ulong UsedBytes => TotalBytes >= FreeBytes ? TotalBytes - FreeBytes : 0;
    public double UsedPercent => TotalBytes == 0 ? 100 : UsedBytes * 100.0 / TotalBytes;
}

public sealed class FileGeneralInfo
{
    public required IReadOnlyList<FileSelectionItem> Items { get; init; }
    public string? DisplayName { get; init; }
    public string? TypeName { get; init; }
    public string? Location { get; init; }
    public ulong TotalSize { get; init; }
    public uint FileCount { get; init; }
    public uint FolderCount { get; init; }
    public DateTimeOffset? Created { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public uint Attributes { get; init; }
    public uint ValidAttributes { get; init; }
    public bool? ReadOnly { get; set; }
    public bool? Hidden { get; set; }
}

public sealed class SecurityUserEntry
{
    public required string UserName { get; init; }
    public uint OriginalAccess { get; set; }
    public uint NewAccess { get; set; }
    public bool MarkedForRemove { get; set; }
    public bool IsNew { get; set; }
}

public sealed class SecurityEditorState
{
    public bool IsLocked { get; set; }
    public bool ManageMode { get; set; }
    public bool SupportsUserPriv { get; set; }
    public uint CurrentAccess { get; set; }
    public List<SecurityUserEntry> Users { get; } = new();
    public string? SelectedUserName { get; set; }
}
