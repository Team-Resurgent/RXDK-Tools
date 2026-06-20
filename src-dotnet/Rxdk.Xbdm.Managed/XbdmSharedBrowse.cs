using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

/// <summary>
/// Folder browsing on the per-console SCI shared TCP connection (legacy HrDoOpenSharedConnection).
/// </summary>
public static class XbdmSharedBrowse
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConversationTimeout = TimeSpan.FromSeconds(45);

    public static T Run<T>(string consoleName, Func<XbdmSharedBrowseContext, T> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consoleName);
        ArgumentNullException.ThrowIfNull(action);

        XbdmSession.EnsureInitialized();
        var sci = XbdmSciRegistry.GetOrCreate(consoleName);
        sci.SetConnectionTimeout(ConnectTimeout, ConversationTimeout);
        return sci.WithSession(session => action(new XbdmSharedBrowseContext(sci, session)));
    }

    public static void InvalidateConsole(string consoleName) =>
        XbdmSciRegistry.ReleaseConsole(consoleName);
}

public sealed class XbdmSharedBrowseContext
{
    private readonly XbdmSci _sci;
    private readonly XbdmProtocolSession _session;

    internal XbdmSharedBrowseContext(XbdmSci sci, XbdmProtocolSession session)
    {
        _sci = sci;
        _session = session;
    }

    public IReadOnlyList<char> ListDrives() =>
        XbdmSessionBrowseOps.ListDrives(_session);

    public IReadOnlyList<XbdmDirEntry> ListDirectory(string wirePath, int maxEntries = 512) =>
        XbdmSessionBrowseOps.ListDirectory(_sci, _session, wirePath, maxEntries);

    public (ulong FreeBytes, ulong TotalBytes) GetDiskFreeSpace(string driveWire) =>
        XbdmSessionBrowseOps.GetDiskFreeSpace(_session, driveWire);
}
