using System.Net;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

public sealed class ManagedXbdmConnection : IXbdmConnection
{
    private XbdmProtocolSession _session;
    private readonly XbdmSci _sci;
    private readonly ManagedXbdmDebugConnection _debug;
    private readonly string _consoleName;
    private bool _authenticationAttempted;

    internal ManagedXbdmConnection(string consoleName, XbdmProtocolSession session, XbdmConnectOptions? options = null)
    {
        _consoleName = consoleName;
        _session = session;
        _authenticationAttempted = session.SecurityHandshakeAttempted;
        _sci = XbdmSciRegistry.GetOrCreate(consoleName, options);
        _debug = new ManagedXbdmDebugConnection(_sci);
    }

    public string ConsoleName => _consoleName;

    public IXbdmDebugConnection Debug => _debug;

    public IReadOnlyList<char> ListDrives()
    {
        var line = _session.SendCommand("DRIVELIST");
        const string prefix = "200- ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            throw XbdmException.FromHResult("Unexpected DRIVELIST response.", XbdmHResults.FileError, line);

        return line[prefix.Length..].ToCharArray();
    }

    public IReadOnlyList<XbdmDirEntry> ListDirectory(string wirePath, int maxEntries = 512)
    {
        _session.SendCommand($"DIRLIST NAME=\"{wirePath}\"");
        return _session.ReadMultiresponse()
            .Take(maxEntries)
            .Select(line => XbdmProtocol.ParseDmfaLine(line, _sci))
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToArray();
    }

    public XbdmDirEntry GetFileAttributes(string wirePath)
    {
        _session.SendCommand($"GETFILEATTRIBUTES NAME=\"{wirePath}\"");
        var lines = _session.ReadMultiresponse();
        if (lines.Count == 0)
            throw XbdmException.FromHResult($"Could not get attributes for '{wirePath}'.", XbdmHResults.FileError);

        return XbdmProtocol.ParseDmfaLine(lines[0], _sci);
    }

    public void SendFile(string localPath, string wirePath)
    {
        var fileInfo = new FileInfo(localPath);
        if (!fileInfo.Exists)
            throw XbdmException.FromHResult($"Could not open '{localPath}'.", XbdmHResults.FileError);

        var size = fileInfo.Length;
        if (size > uint.MaxValue)
            throw XbdmException.FromHResult("File is too large for XBDM transfer.", XbdmHResults.FileError);

        var (hr, _) = _session.SendCommandRaw($"SENDFILE NAME=\"{wirePath}\" LENGTH=0x{(uint)size:x}");
        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult($"Could not send '{localPath}'.", hr);

        var buffer = new byte[8192];
        using (var stream = fileInfo.OpenRead())
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                _session.SendBinary(buffer.AsSpan(0, read));
        }

        _session.ReceiveStatusOrThrow($"Could not send '{localPath}'.");
    }

    public void ReceiveFile(string wirePath, string localPath)
    {
        var (hr, _) = _session.SendCommandRaw($"GETFILE NAME=\"{wirePath}\"");
        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult($"Could not receive '{wirePath}'.", hr);

        var size = _session.ReceiveUInt32();
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var buffer = new byte[8192];
        try
        {
            using var stream = File.Create(localPath);
            var remaining = size;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(remaining, buffer.Length);
                _session.ReceiveBinary(buffer.AsSpan(0, chunk));
                stream.Write(buffer, 0, chunk);
                remaining -= (uint)chunk;
            }
        }
        catch
        {
            if (File.Exists(localPath))
                File.Delete(localPath);
            throw;
        }
    }

    public void Delete(string wirePath, bool isDirectory)
    {
        var suffix = isDirectory ? " DIR" : string.Empty;
        _session.SendCommand($"DELETE NAME=\"{wirePath}\"{suffix}");
    }

    public void Rename(string fromWire, string toWire) =>
        _session.SendCommand($"RENAME NAME=\"{fromWire}\" NEWNAME=\"{toWire}\"");

    public void CreateDirectory(string wirePath) =>
        _session.SendCommand($"MKDIR NAME=\"{wirePath}\"");

    public void Reboot(bool cold, string? launchPath = null)
    {
        if (string.IsNullOrEmpty(launchPath))
        {
            Reboot(cold ? 0u : XbdmDebugConstants.DmbootWarm);
            return;
        }

        _session.SendCommand($"magicboot title={launchPath} DEBUG");
    }

    public void Reboot(uint flags) => _debug.Reboot(flags);

    public (ulong FreeBytes, ulong TotalBytes) GetDiskFreeSpace(string driveWire)
    {
        var (hr, line) = _session.SendCommandRaw($"DRIVEFREESPACE NAME=\"{driveWire}\"");
        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult($"Could not read disk space for '{driveWire}'.", hr, line);

        if (hr == XbdmHResults.Multiresponse)
            return XbdmProtocol.ParseDiskFreeSpaceLines(_session.ReadMultiresponse());

        return XbdmProtocol.ParseDiskFreeSpaceLines([XbdmProtocol.StripResponsePrefix(line)]);
    }

    public uint? TryResolveXboxAddress() =>
        XboxNameResolver.TryResolveAddressUInt32(ConsoleName);

    public uint? TryGetAltAddress()
    {
        var line = _session.SendCommand("ALTADDR");
        var addr = XbdmProtocol.GetUInt(line, "addr");
        return addr == 0 ? null : addr;
    }

    public string GetNameOfXbox(bool resolvable)
    {
        string name;
        try
        {
            var line = _session.SendCommand("DEBUGNAME");
            name = XbdmProtocol.StripResponsePrefix(line);
        }
        catch (XbdmException)
        {
            name = string.Empty;
        }

        if (string.IsNullOrEmpty(name))
        {
            var peer = GetPeerAddress(_session);
            if (!XboxNameResolver.TryQueryNameAt(peer, out name))
                throw XbdmException.FromHResult("Could not read Xbox name.", XbdmHResults.NoXboxName);
        }

        if (resolvable)
        {
            var peer = GetPeerAddress(_session);
            if (!XboxNameResolver.TryResolveNameToAddress(name, out var resolved) || !peer.Equals(resolved))
                throw XbdmException.FromHResult($"Xbox name '{name}' is not resolvable to the connected console.", XbdmHResults.CannotConnect);
        }

        return name;
    }

    private static IPAddress GetPeerAddress(XbdmProtocolSession session)
    {
        var endpoint = session.Socket.RemoteEndPoint as IPEndPoint;
        if (endpoint is null)
            throw XbdmException.FromHResult("Could not read Xbox connection endpoint.", XbdmHResults.CannotConnect);
        return endpoint.Address;
    }

    public string GetXbeLaunchPath()
    {
        _session.SendCommand("XBEINFO RUNNING");
        var lines = _session.ReadMultiresponse();
        foreach (var line in lines)
        {
            var name = XbdmProtocol.GetQuotedString(line, "name");
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return string.Empty;
    }

    public XbdmXbeInfo GetXbeInfo(string name) => _debug.GetXbeInfo(name);

    public void CaptureScreenshot(string localBmpPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localBmpPath);

        var (hr, line) = _session.SendCommandRaw("screenshot");
        if (hr != XbdmHResults.Binresponse)
            throw XbdmException.FromHResult("Screenshot command failed.", hr, line);

        var infoLine = _session.ReceiveLine();
        var info = XbdmScreenshot.ParseInfoLine(infoLine);
        XbdmScreenshot.WriteBmp(localBmpPath, info, _session);
    }

    public void SetFileAttributes(string wirePath, uint attributes)
    {
        var entry = GetFileAttributes(wirePath);
        var (changeHigh, changeLow) = XbdmProtocol.UnixToFileTime(entry.ChangeTimeUnix);
        var creationUnix = entry.CreationTimeUnix ?? entry.ChangeTimeUnix;
        var (createHigh, createLow) = XbdmProtocol.UnixToFileTime(creationUnix);
        XbdmTimeCorrection.CorrectToConsole(_sci, ref createHigh, ref createLow);
        XbdmTimeCorrection.CorrectToConsole(_sci, ref changeHigh, ref changeLow);

        var readOnly = (attributes & XbdmConstants.AttrReadOnly) != 0 ? 1 : 0;
        var hidden = (attributes & XbdmConstants.AttrHidden) != 0 ? 1 : 0;
        var command =
            $"SETFILEATTRIBUTES NAME=\"{wirePath}\" CREATEHI=0x{createHigh:x8} CREATELO=0x{createLow:x8} " +
            $"CHANGEHI=0x{changeHigh:x8} CHANGELO=0x{changeLow:x8} " +
            $"READONLY={readOnly} HIDDEN={hidden}";

        _session.SendCommand(command);
    }

    public bool IsSecurityEnabled()
    {
        var (hr, _) = _session.SendCommandRaw("BOXID");
        if (hr == XbdmHResults.NoErr)
            return true;
        if (hr == XbdmHResults.NotLocked)
            return false;
        if (hr == XbdmHResults.InvalidCmd)
            return _authenticationAttempted;
        throw XbdmException.FromHResult("Could not read security state.", hr);
    }

    public bool SupportsUserPrivileges()
    {
        var (hr, _) = _session.SendCommandRaw("GETUSERPRIV NAME=BOGUS");
        return hr == XbdmHResults.InvalidCmd;
    }

    public uint GetUserAccess(string? userName = null)
    {
        var command = userName == null ? "GETUSERPRIV ME" : $"GETUSERPRIV NAME=\"{userName}\"";
        var line = _session.SendCommand(command);
        return XbdmProtocol.ParseAccessPrivileges(XbdmProtocol.StripResponsePrefix(line));
    }

    public IReadOnlyList<XbdmUser> ListUsers(int maxUsers = 64)
    {
        _session.SendCommand("USERLIST");
        return _session.ReadMultiresponse()
            .Take(maxUsers)
            .Select(line =>
            {
                var name = XbdmProtocol.GetQuotedString(line, "name") ?? string.Empty;
                var access = XbdmProtocol.ParseAccessPrivileges(line);
                return new XbdmUser(name, access);
            })
            .Where(user => !string.IsNullOrEmpty(user.UserName))
            .ToArray();
    }

    public void AddUser(string userName, uint access)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var command = $"USER NAME=\"{userName}\"{XbdmProtocol.FormatAccessPrivileges(access)}";
        _session.SendCommand(command);
    }

    public void RemoveUser(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        _session.SendCommand($"USER NAME=\"{userName}\" REMOVE");
    }

    public void SetUserAccess(string userName, uint access)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var command = $"SETUSERPRIV NAME=\"{userName}\"{XbdmProtocol.FormatAccessPrivileges(access)}";
        _session.SendCommand(command);
    }

    public void EnableSecurity(bool enable)
    {
        if (!enable)
        {
            _session.SendCommand("LOCKMODE UNLOCK");
            return;
        }

        var (hr, _) = _session.SendCommandRaw("BOXID");
        if (hr == XbdmHResults.InvalidCmd)
            throw XbdmException.FromHResult("This Xbox does not support security lock.", hr);

        if (hr != XbdmHResults.NoErr && hr != XbdmHResults.NotLocked)
            throw XbdmException.FromHResult("Could not verify security support.", hr);

        var boxId = XbdmSecurity.GenerateLockBoxId();
        _session.SendCommand($"LOCKMODE BOXID=0q{boxId >> 32:x8}{boxId & 0xFFFFFFFF:x8}");
    }

    public void SetAdminPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            _session.SendCommand("ADMINPW NONE");
            return;
        }

        var key = XbdmSecurity.PerformKeyExchange(_session);
        var passwd = 0UL;
        XbcCrypto.HashDataAsciiPassword(ref passwd, password);
        passwd ^= key;
        _session.SendCommand($"ADMINPW PASSWD=0q{passwd >> 32:x8}{passwd & 0xFFFFFFFF:x8}");
    }
    public void UseSecureConnection(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        _session.Dispose();
        _session = XbdmProtocolSession.Connect(
            _consoleName,
            TimeSpan.FromSeconds(10),
            new XbdmConnectOptions { AdminPassword = password });
        _authenticationAttempted = true;
    }
    public void UseSharedConnection(bool enable)
    {
        _sci.UseSharedConnection(enable);
    }

    public void Dispose()
    {
        _sci.InvalidateSharedConnection();
        _session.Dispose();
    }
}
