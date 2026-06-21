using Rxdk.XbShellExt.Interop;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.KitServices.Stores;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbShellExt.Shell;

internal enum XboxItemKind
{
    Root,
    AddConsole,
    Console,
    Volume,
    Directory,
    File,
}

internal sealed record XboxShellItem(
    string Segment,
    string FullPath,
    XboxItemKind Kind,
    uint Attributes,
    bool IsDirectory,
    string? DisplayName = null,
    string? VolumeTypeName = null,
    ulong? FreeBytes = null,
    ulong? TotalBytes = null);

internal static class XboxShellItemFactory
{
    public static XboxShellItem FromPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return new XboxShellItem(string.Empty, string.Empty, XboxItemKind.Root, ShellConstants.RootAttributes, true);

        if (string.Equals(fullPath, ShellConstants.AddConsoleSegment, StringComparison.Ordinal))
        {
            return new XboxShellItem(
                ShellConstants.AddConsoleSegment,
                ShellConstants.AddConsoleSegment,
                XboxItemKind.AddConsole,
                ShellConstants.AddConsoleAttributes,
                false,
                ShellConstants.AddConsoleDisplayName);
        }

        var depth = fullPath.Count(c => c == '\\') + 1;
        var segment = fullPath.Split('\\')[^1];
        if (depth == 1)
        {
            return new XboxShellItem(segment, fullPath, XboxItemKind.Console, ShellConstants.ConsoleAttributes, true);
        }

        if (depth == 2)
        {
            var letter = char.IsLetter(segment[0])
                ? char.ToUpperInvariant(segment[0]).ToString()
                : segment;
            return new XboxShellItem(letter, fullPath, XboxItemKind.Volume, ShellConstants.VolumeAttributes, true);
        }

        try
        {
            var console = fullPath.Split('\\')[0];
            if (depth > 2)
            {
                var parentPath = WirePathService.GetParentDisplayPath(fullPath) ?? fullPath;
                if (WirePathService.TryBuildWirePath(
                        parentPath.Replace('\\', Path.DirectorySeparatorChar),
                        out var wirePath))
                {
                    var match = ShellXbdm.WithBrowse(console, browse => browse.ListDirectory(wirePath))
                        .FirstOrDefault(e => string.Equals(e.Name, segment, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        var isDir = (match.Attributes & XbdmConstants.AttrDirectory) != 0;
                        return new XboxShellItem(
                            segment,
                            fullPath,
                            isDir ? XboxItemKind.Directory : XboxItemKind.File,
                            isDir ? ShellConstants.DirectoryAttributes : ShellConstants.FileAttributes,
                            isDir);
                    }
                }
            }
        }
        catch
        {
        }

        return new XboxShellItem(
            segment,
            fullPath,
            XboxItemKind.File,
            ShellConstants.FileAttributes,
            false);
    }

    public static IReadOnlyList<XboxShellItem> ListChildren(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return ListRootChildren();

        if (string.Equals(fullPath, ShellConstants.AddConsoleSegment, StringComparison.Ordinal))
            return Array.Empty<XboxShellItem>();

        var depth = fullPath.Count(c => c == '\\') + 1;
        return depth switch
        {
            1 => ListConsoleChildren(fullPath),
            _ => ListFolderChildren(fullPath),
        };
    }

    private static IReadOnlyList<XboxShellItem> ListRootChildren()
    {
        var items = new List<XboxShellItem>
        {
            FromPath(ShellConstants.AddConsoleSegment),
        };

        var store = new ShellExtensionConsoleStore();
        foreach (var name in store.GetConsoleNames())
            items.Add(FromPath(name));

        return items;
    }

    private static IReadOnlyList<XboxShellItem> ListConsoleChildren(string consolePath)
    {
        return ShellXbdm.WithBrowse(consolePath, browse =>
        {
            var drives = browse.ListDrives();
            return drives.Select(drive =>
            {
                var letter = char.ToUpperInvariant(drive);
                var fullPath = WirePathService.BuildDriveDisplayPath(consolePath, letter);
                ulong? free = null;
                ulong? total = null;
                try
                {
                    (free, total) = browse.GetDiskFreeSpace($"{letter}:\\");
                }
                catch
                {
                }

                return new XboxShellItem(
                    $"{letter}",
                    fullPath,
                    XboxItemKind.Volume,
                    ShellConstants.VolumeAttributes,
                    true,
                    FormattingHelper.GetVolumeDisplayName(letter),
                    FormattingHelper.GetVolumeTypeDescription(letter),
                    free,
                    total);
            }).ToArray();
        });
    }

    private static IReadOnlyList<XboxShellItem> ListFolderChildren(string fullPath)
    {
        var parts = fullPath.Split('\\', 2);
        var console = parts[0];
        var displayPath = fullPath.Replace('\\', Path.DirectorySeparatorChar);
        if (!WirePathService.TryBuildWirePath(displayPath, out var wirePath))
            throw new InvalidOperationException($"Invalid display path: {displayPath}");

        return ShellXbdm.WithBrowse(console, browse => browse.ListDirectory(wirePath).Select(entry =>
        {
            var isDir = (entry.Attributes & XbdmConstants.AttrDirectory) != 0;
            var childPath = $"{fullPath}\\{entry.Name}";
            return new XboxShellItem(
                entry.Name,
                childPath,
                isDir ? XboxItemKind.Directory : XboxItemKind.File,
                isDir ? ShellConstants.DirectoryAttributes : ShellConstants.FileAttributes,
                isDir);
        }).ToArray());
    }
}
