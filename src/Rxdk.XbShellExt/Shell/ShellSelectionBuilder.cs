using Rxdk.Xbdm.KitServices.Models;
using Rxdk.XbShellExt.Interop;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Shell;

internal static class ShellSelectionBuilder
{
    public static string? GetSelectionPath(string folderPath, nint childPidl)
    {
        if (childPidl == 0)
            return null;

        var segment = PidlHelper.GetLastSegment(childPidl);
        return string.IsNullOrEmpty(folderPath)
            ? segment
            : WirePathService.BuildChildDisplayPath(folderPath, segment);
    }

    public static FileSelection? BuildFileSelection(string folderPath, nint childPidl) =>
        BuildFileSelection(folderPath, childPidl == 0 ? Array.Empty<nint>() : new[] { childPidl });

    public static FileSelection? BuildFileSelection(string folderPath, IReadOnlyList<nint> childPidls)
    {
        if (childPidls.Count == 0)
            return null;

        var items = new List<FileSelectionItem>();
        string? consoleName = null;
        string? folderDisplayPath = null;

        foreach (var childPidl in childPidls)
        {
            var itemSelection = BuildSingleItemSelection(folderPath, childPidl);
            if (itemSelection == null)
                return null;

            if (consoleName == null)
            {
                consoleName = itemSelection.ConsoleName;
                folderDisplayPath = itemSelection.FolderDisplayPath;
            }
            else if (!string.Equals(consoleName, itemSelection.ConsoleName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            items.AddRange(itemSelection.Items);
        }

        if (items.Count == 0 || consoleName == null || folderDisplayPath == null)
            return null;

        return new FileSelection
        {
            ConsoleName = consoleName,
            FolderDisplayPath = folderDisplayPath,
            Items = items,
        };
    }

    public static string? ResolveDropTargetFolder(string folderPath, uint cidl, nint apidl)
    {
        if (cidl == 0)
            return ResolveFolderDisplayPath(folderPath);

        if (apidl == 0)
            return null;

        return ResolveDropTargetFolder(folderPath, ReadChildPidl(cidl, apidl));
    }

    public static string? ResolveDropTargetFolder(string folderPath, nint childPidl)
    {
        if (childPidl == 0)
            return ResolveFolderDisplayPath(folderPath);

        var selectionPath = GetSelectionPath(folderPath, childPidl);
        if (string.IsNullOrEmpty(selectionPath))
            return null;

        var item = XboxShellItemFactory.FromPath(selectionPath);
        return item.IsDirectory ? selectionPath : ResolveFolderDisplayPath(folderPath);
    }

    public static bool SupportsDropTarget(string folderPath, uint cidl, nint apidl)
    {
        if (cidl == 0)
            return SupportsDropTarget(folderPath, 0);

        if (apidl == 0)
            return false;

        return SupportsDropTarget(folderPath, ReadChildPidl(cidl, apidl));
    }

    public static bool SupportsDropTarget(string folderPath, nint childPidl)
    {
        var target = ResolveDropTargetFolder(folderPath, childPidl);
        if (string.IsNullOrEmpty(target))
            return false;

        var consoleName = WirePathService.GetConsoleNameFromDisplayPath(target);
        return !WirePathService.IsDriveListing(target, consoleName) &&
               !string.Equals(target, consoleName, StringComparison.OrdinalIgnoreCase);
    }

    public static nint ReadChildPidl(uint cidl, nint apidl)
    {
        if (cidl == 0 || apidl == 0)
            return 0;

        return Marshal.ReadIntPtr(apidl);
    }

    private static string? ResolveFolderDisplayPath(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return null;

        return folderPath;
    }

    public static string ResolvePasteTargetFolder(string folderPath, nint childPidl)
    {
        if (childPidl != 0)
        {
            var selectionPath = GetSelectionPath(folderPath, childPidl);
            if (!string.IsNullOrEmpty(selectionPath))
            {
                var item = XboxShellItemFactory.FromPath(selectionPath);
                if (item.IsDirectory)
                    return selectionPath;
            }
        }

        return ResolveDropTargetFolder(folderPath, 0) ?? folderPath;
    }

    private static FileSelection? BuildSingleItemSelection(string folderPath, nint childPidl)
    {
        var selectionPath = GetSelectionPath(folderPath, childPidl);
        if (string.IsNullOrEmpty(selectionPath))
            return null;

        var item = XboxShellItemFactory.FromPath(selectionPath);
        WirePathService.TryBuildWirePath(selectionPath, out var wirePath);
        var name = item.Kind == XboxItemKind.Volume
            ? $"{FormattingHelper.NormalizeDriveLetter(item.Segment)}:"
            : item.Segment;

        var consoleName = item.Kind == XboxItemKind.Console
            ? selectionPath
            : WirePathService.GetConsoleNameFromDisplayPath(selectionPath);

        var folderDisplayPath = item.Kind switch
        {
            XboxItemKind.Console => selectionPath,
            XboxItemKind.Volume => WirePathService.GetParentDisplayPath(selectionPath) ?? consoleName,
            _ => folderPath,
        };

        return new FileSelection
        {
            ConsoleName = consoleName,
            FolderDisplayPath = folderDisplayPath,
            Items =
            [
                new FileSelectionItem
                {
                    Name = name,
                    WirePath = wirePath,
                    IsDirectory = item.IsDirectory,
                },
            ],
        };
    }
}
