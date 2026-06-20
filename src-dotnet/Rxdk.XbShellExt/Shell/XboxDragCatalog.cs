using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbShellExt.Shell;

internal sealed class XboxDragEntry
{
    public required string RelativePath { get; init; }
    public required string WirePath { get; init; }
    public bool IsDirectory { get; init; }
    public uint Attributes { get; init; }
    public ulong Size { get; init; }
    public long ChangeTimeUnix { get; init; }
    public long? CreationTimeUnix { get; init; }
}

internal static class XboxDragCatalog
{
    public static IReadOnlyList<XboxDragEntry> Build(FileSelection selection)
    {
        if (selection.Items.Count == 0)
            return Array.Empty<XboxDragEntry>();

        using var conn = XbdmSession.Connect(selection.ConsoleName);
        return Build(conn, selection);
    }

    public static IReadOnlyList<XboxDragEntry> Build(XbdmConnection conn, FileSelection selection)
    {
        if (selection.Items.Count == 0)
            return Array.Empty<XboxDragEntry>();

        var entries = new List<XboxDragEntry>();
        foreach (var item in selection.Items)
        {
            var relativePath = item.Name.TrimEnd(':');
            AddEntryRecursive(conn, item.WirePath, relativePath, item.IsDirectory, entries);
        }

        return entries;
    }

    private static void AddEntryRecursive(
        XbdmConnection conn,
        string wirePath,
        string relativePath,
        bool isDirectory,
        List<XboxDragEntry> entries)
    {
        XbdmDirEntry attrs;
        try
        {
            attrs = conn.GetFileAttributes(wirePath);
        }
        catch
        {
            attrs = new XbdmDirEntry(relativePath, 0, isDirectory ? XbdmConstants.AttrDirectory : 0, 0);
        }

        entries.Add(new XboxDragEntry
        {
            RelativePath = relativePath.Replace('/', '\\'),
            WirePath = wirePath,
            IsDirectory = isDirectory,
            Attributes = attrs.Attributes,
            Size = attrs.Size,
            ChangeTimeUnix = attrs.ChangeTimeUnix,
            CreationTimeUnix = attrs.CreationTimeUnix,
        });

        if (!isDirectory)
            return;

        foreach (var child in conn.ListDirectory(wirePath))
        {
            var childIsDir = (child.Attributes & XbdmConstants.AttrDirectory) != 0;
            var childRelative = $"{relativePath}\\{child.Name}";
            var childWire = $"{wirePath.TrimEnd('\\')}\\{child.Name}";
            AddEntryRecursive(conn, childWire, childRelative, childIsDir, entries);
        }
    }
}
