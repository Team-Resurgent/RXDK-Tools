using Rxdk.XbNeighborhood.Core.Models;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Shell;

internal static class ShellPropertyRequestBuilder
{
    public static Rxdk.XbNeighborhood.Core.Services.PropertyRequest? BuildPropertyRequest(string folderPath, IReadOnlyList<string> selectionPaths)
    {
        if (selectionPaths.Count == 0)
            return null;

        if (selectionPaths.Count == 1 &&
            string.Equals(selectionPaths[0], folderPath, StringComparison.OrdinalIgnoreCase))
        {
            var self = XboxShellItemFactory.FromPath(folderPath);
            var targetConsole = self.Kind == XboxItemKind.Console
                ? folderPath
                : WirePathService.GetConsoleNameFromDisplayPath(folderPath);

            if (self.Kind == XboxItemKind.Console)
            {
                return Rxdk.XbNeighborhood.Core.Services.PropertyRequest.FromSelection(
                    targetConsole,
                    folderPath,
                    Array.Empty<FileSelectionItem>());
            }

            if (self.Kind == XboxItemKind.Volume)
            {
                var letter = FormattingHelper.NormalizeDriveLetter(self.Segment);
                return Rxdk.XbNeighborhood.Core.Services.PropertyRequest.FromSelection(
                    targetConsole,
                    WirePathService.GetParentDisplayPath(folderPath) ?? targetConsole,
                    [
                        new FileSelectionItem
                        {
                            Name = $"{letter}:",
                            WirePath = $"{letter}:\\",
                            IsDirectory = true,
                        },
                    ]);
            }
        }

        var items = new List<FileSelectionItem>();
        foreach (var path in selectionPaths)
        {
            var shellItem = XboxShellItemFactory.FromPath(path);
            WirePathService.TryBuildWirePath(path, out var wirePath);
            var name = shellItem.Kind == XboxItemKind.Volume
                ? $"{FormattingHelper.NormalizeDriveLetter(shellItem.Segment)}:"
                : shellItem.Segment;
            items.Add(new FileSelectionItem
            {
                Name = name,
                WirePath = wirePath,
                IsDirectory = shellItem.IsDirectory,
            });
        }

        var firstPath = selectionPaths[0];
        var consoleName = XboxShellItemFactory.FromPath(firstPath).Kind == XboxItemKind.Console
            ? firstPath
            : WirePathService.GetConsoleNameFromDisplayPath(firstPath);

        return Rxdk.XbNeighborhood.Core.Services.PropertyRequest.FromSelection(consoleName, folderPath, items);
    }

    public static Rxdk.XbNeighborhood.Core.Services.PropertyRequest? BuildSecurityRequest(string selectionPath)
    {
        if (string.IsNullOrEmpty(selectionPath))
            return null;

        return Rxdk.XbNeighborhood.Core.Services.PropertyRequest.FromSelection(selectionPath, selectionPath, Array.Empty<FileSelectionItem>());
    }
}
