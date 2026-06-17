using RXDKNeighborhood.Core.Models;
using Rxdk.Native;

namespace RXDKNeighborhood.Core.Services;

public sealed class XboxBrowserService : IDisposable
{
    private readonly ConsoleRegistryService _consoles;
    private XbdmSession? _session;

    public XboxBrowserService(ConsoleRegistryService consoles) => _consoles = consoles;

    public void EnsureNativeInitialized()
    {
        _session ??= new XbdmSession();
        XbdmSession.EnsureInitialized();
    }

    public IReadOnlyList<NavigationNode> LoadConsoleTree()
    {
        EnsureNativeInitialized();
        var names = _consoles.GetConsoleNames();
        var defaultName = _consoles.GetDefaultConsoleName();

        if (!string.IsNullOrWhiteSpace(defaultName) &&
            !names.Any(n => string.Equals(n, defaultName, StringComparison.OrdinalIgnoreCase)))
        {
            _consoles.AddConsole(defaultName);
            names = _consoles.GetConsoleNames();
        }

        return names.Select(name => new NavigationNode
        {
            Kind = NavigationNodeKind.Console,
            ConsoleName = name,
            DisplayPath = name,
            Title = name,
        }).ToArray();
    }

    public IReadOnlyList<NavigationNode> LoadDrives(string consoleName)
    {
        using var conn = XbdmSession.Connect(consoleName);
        return conn.ListDrives().Select(drive => new NavigationNode
        {
            Kind = NavigationNodeKind.Drive,
            ConsoleName = consoleName,
            DisplayPath = WirePathService.BuildDriveDisplayPath(consoleName, drive),
            Title = $"{drive}:",
        }).ToArray();
    }

    public IReadOnlyList<FileEntryModel> ListFolder(string displayPath, string consoleName)
    {
        if (!WirePathService.TryBuildWirePath(displayPath, out var wirePath))
            throw new InvalidOperationException($"Invalid display path: {displayPath}");

        using var conn = XbdmSession.Connect(consoleName);
        return conn.ListDirectory(wirePath).Select(ToFileEntry).ToArray();
    }

    public IReadOnlyList<FileEntryModel> ListDrivesAsEntries(string consoleName)
    {
        using var conn = XbdmSession.Connect(consoleName);
        return conn.ListDrives().Select(drive => new FileEntryModel
        {
            Name = $"{drive}:",
            Type = "Drive",
            IsDirectory = true,
        }).ToArray();
    }

    private static FileEntryModel ToFileEntry(XbdmDirEntry entry)
    {
        var isDir = (entry.Attributes & XbdmConstants.AttrDirectory) != 0;
        DateTimeOffset? modified = entry.ChangeTimeUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(entry.ChangeTimeUnix)
            : null;

        return new FileEntryModel
        {
            Name = entry.Name,
            Type = isDir ? "Folder" : "File",
            Size = entry.Size,
            Modified = modified,
            IsDirectory = isDir,
        };
    }

    public void Dispose() => _session?.Dispose();
}
