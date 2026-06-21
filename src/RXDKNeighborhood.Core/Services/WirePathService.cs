namespace RXDKNeighborhood.Core.Services;

public static class WirePathService
{
    public static bool TryBuildWirePath(string displayPath, out string wirePath) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.TryBuildWirePath(displayPath, out wirePath);

    public static string BuildDriveDisplayPath(string consoleName, char driveLetter) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.BuildDriveDisplayPath(consoleName, driveLetter);

    public static string AppendDisplaySegment(string displayPath, string segment) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.AppendDisplaySegment(displayPath, segment);

    public static string? GetParentDisplayPath(string displayPath) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.GetParentDisplayPath(displayPath);

    public static string GetConsoleNameFromDisplayPath(string displayPath) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.GetConsoleNameFromDisplayPath(displayPath);

    public static string GetItemDisplayPath(string folderPath, string name) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.GetItemDisplayPath(folderPath, name);

    public static bool TryBuildWirePathInFolder(string folderDisplayPath, string name, out string wirePath) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.TryBuildWirePathInFolder(folderDisplayPath, name, out wirePath);

    public static bool IsConsoleRoot(string? displayPath, string? consoleName) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.IsConsoleRoot(displayPath, consoleName);

    public static bool IsDriveListing(string? displayPath, string? consoleName) =>
        Rxdk.Xbdm.KitServices.Services.WirePathService.IsDriveListing(displayPath, consoleName);
}
