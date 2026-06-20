using RXDKNeighborhood.Core.Services;
using Rxdk.XbShellExt.Interop;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.KitServices.Stores;

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

        var browser = CreateBrowser();
        try
        {
            var console = fullPath.Split('\\')[0];
            if (depth > 2)
            {
                var parentPath = Rxdk.Xbdm.KitServices.Services.WirePathService.GetParentDisplayPath(fullPath) ?? fullPath;
                var entries = browser.ListFolder(parentPath.Replace('\\', Path.DirectorySeparatorChar), console);
                var match = entries.FirstOrDefault(e => string.Equals(e.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return new XboxShellItem(
                        segment,
                        fullPath,
                        match.IsDirectory ? XboxItemKind.Directory : XboxItemKind.File,
                        match.IsDirectory ? ShellConstants.DirectoryAttributes : ShellConstants.FileAttributes,
                        match.IsDirectory);
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

        // Folder enumeration only happens for browsable containers. Route by path depth instead
        // of FromPath() kind lookup so a transient DIRLIST failure cannot misclassify a folder
        // as a file and yield an empty listing.
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

        // Match native CXboxRoot::RefreshChildren — registry only, no XBDM init on enum.
        var store = new ShellExtensionConsoleStore();
        foreach (var name in store.GetConsoleNames())
            items.Add(FromPath(name));

        return items;
    }

    private static IReadOnlyList<XboxShellItem> ListConsoleChildren(string consolePath)
    {
        using var conn = Rxdk.Xbdm.Managed.XbdmSession.Connect(consolePath);
        return conn.ListDrives().Select(drive =>
        {
            var letter = char.ToUpperInvariant(drive);
            var fullPath = Rxdk.Xbdm.KitServices.Services.WirePathService.BuildDriveDisplayPath(consolePath, letter);
            ulong? free = null;
            ulong? total = null;
            try
            {
                (free, total) = conn.GetDiskFreeSpace($"{letter}:\\");
            }
            catch
            {
            }

            // Match native CXboxConsole: pidl segment is the drive letter only ("T", not "T:").
            return new XboxShellItem(
                $"{letter}",
                fullPath,
                XboxItemKind.Volume,
                ShellConstants.VolumeAttributes,
                true,
                Rxdk.Xbdm.KitServices.Services.FormattingHelper.GetVolumeDisplayName(letter),
                Rxdk.Xbdm.KitServices.Services.FormattingHelper.GetVolumeTypeDescription(letter),
                free,
                total);
        }).ToArray();
    }

    private static IReadOnlyList<XboxShellItem> ListFolderChildren(string fullPath)
    {
        var parts = fullPath.Split('\\', 2);
        var console = parts[0];
        var browser = CreateBrowser();
        var displayPath = fullPath.Replace('\\', Path.DirectorySeparatorChar);
        return browser.ListFolder(displayPath, console).Select(entry =>
        {
            var childPath = $"{fullPath}\\{entry.Name}";
            return new XboxShellItem(
                entry.Name,
                childPath,
                entry.IsDirectory ? XboxItemKind.Directory : XboxItemKind.File,
                entry.IsDirectory ? ShellConstants.DirectoryAttributes : ShellConstants.FileAttributes,
                entry.IsDirectory);
        }).ToArray();
    }

    private static XboxBrowserService CreateBrowser() =>
        new(new ShellExtensionConsoleStore());
}
