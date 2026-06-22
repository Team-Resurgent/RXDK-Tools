using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

/// <summary>
/// Browse helpers that run on an acquired XBDM session (shared SCI connection).
/// </summary>
internal static class XbdmSessionBrowseOps
{
    internal static IReadOnlyList<char> ListDrives(XbdmProtocolSession session)
    {
        var line = session.SendCommand("DRIVELIST");
        const string prefix = "200- ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            throw XbdmException.FromHResult("Unexpected DRIVELIST response.", XbdmHResults.FileError, line);

        return line[prefix.Length..].ToCharArray();
    }

    internal static IReadOnlyList<XbdmDirEntry> ListDirectory(
        XbdmSci sci,
        XbdmProtocolSession session,
        string wirePath,
        int maxEntries = 512)
    {
        // Time correction must complete before DIRLIST; never inject commands between the
        // multiresponse status line and ReadMultiresponse (legacy reads the body first).
        XbdmTimeCorrection.Ensure(sci, session);
        session.SendCommand($"DIRLIST NAME=\"{wirePath}\"");
        return session.ReadMultiresponse()
            .Take(maxEntries)
            .Select(line => XbdmProtocol.ParseDmfaLine(line, sci))
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToArray();
    }

    internal static (ulong FreeBytes, ulong TotalBytes) GetDiskFreeSpace(
        XbdmProtocolSession session,
        string driveWire)
    {
        var (hr, line) = session.SendCommandRaw($"DRIVEFREESPACE NAME=\"{driveWire}\"");
        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult($"Could not read disk space for '{driveWire}'.", hr, line);

        if (hr == XbdmHResults.Multiresponse)
            return XbdmProtocol.ParseDiskFreeSpaceLines(session.ReadMultiresponse());

        return XbdmProtocol.ParseDiskFreeSpaceLines([XbdmProtocol.StripResponsePrefix(line)]);
    }
}
