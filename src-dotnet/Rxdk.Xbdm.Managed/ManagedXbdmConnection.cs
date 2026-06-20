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

    public void SyncConsoleClock() => XbdmTimeCorrection.SyncConsoleClock(_sci);

    public IReadOnlyList<char> ListDrives() =>
        XbdmSessionBrowseOps.ListDrives(_session);

    public IReadOnlyList<XbdmDirEntry> ListDirectory(string wirePath, int maxEntries = 512) =>
        XbdmSessionBrowseOps.ListDirectory(_sci, _session, wirePath, maxEntries);

    public XbdmDirEntry GetFileAttributes(string wirePath)
    {
        _session.SendCommand($"GETFILEATTRIBUTES NAME=\"{wirePath}\"");
        var lines = _session.ReadMultiresponse();
        if (lines.Count == 0)
            throw XbdmException.FromHResult($"Could not get attributes for '{wirePath}'.", XbdmHResults.FileError);

        XbdmTimeCorrection.Ensure(_sci, _session);
        return XbdmProtocol.ParseDmfaLine(lines[0], _sci);
    }

    public void SendFile(string localPath, string wirePath) => SendFile(localPath, wirePath, progress: null, isCancelled: null);

    public void SendFile(
        string localPath,
        string wirePath,
        Action<long, long>? progress,
        Func<bool>? isCancelled)
    {
        var fileInfo = new FileInfo(localPath);
        if (!fileInfo.Exists)
            throw XbdmException.FromHResult($"Could not open '{localPath}'.", XbdmHResults.FileError);

        var size = fileInfo.Length;
        if (size > uint.MaxValue)
            throw XbdmException.FromHResult("File is too large for XBDM transfer.", XbdmHResults.FileError);

        var (hr, line) = _session.SendCommandRaw($"SENDFILE NAME=\"{wirePath}\" LENGTH=0x{(uint)size:x}");
        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult($"Could not send '{localPath}' to '{wirePath}'.", hr, line);

        var buffer = new byte[8192];
        using (var stream = fileInfo.OpenRead())
        {
            long sent = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (isCancelled?.Invoke() == true)
                    throw new OperationCanceledException();

                _session.SendBinary(buffer.AsSpan(0, read));
                sent += read;
                progress?.Invoke(sent, size);
            }
        }

        _session.ReceiveStatusOrThrow($"Could not send '{localPath}'.");
    }

    public void ReceiveFile(string wirePath, string localPath)
    {
        using var receiver = OpenFileReceiver(wirePath);
        receiver.Start();

        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var buffer = new byte[8192];
        try
        {
            using var stream = File.Create(localPath);
            int read;
            while ((read = receiver.Read(buffer)) > 0)
                stream.Write(buffer, 0, read);
        }
        catch
        {
            if (File.Exists(localPath))
                File.Delete(localPath);
            throw;
        }
    }

    public XbdmFileReceiver OpenFileReceiver(string wirePath) => new(_session, wirePath);

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

    public (ulong FreeBytes, ulong TotalBytes) GetDiskFreeSpace(string driveWire) =>
        XbdmSessionBrowseOps.GetDiskFreeSpace(_session, driveWire);

    public uint? TryResolveXboxAddress()
    {
        try
        {
            var peer = GetPeerAddress(_session);
            return XboxNameResolver.AddressToHostUInt32(peer);
        }
        catch (XbdmException)
        {
            return XboxNameResolver.TryResolveAddressUInt32(ConsoleName);
        }
    }

    public uint? TryGetAltAddress()
    {
        var line = _session.SendCommand("ALTADDR");
        var addr = XbdmProtocol.GetUInt(line, "addr");
        return addr;
    }

    public string GetNameOfXbox(bool resolvable)
    {
        var name = TryReadDebugNameAny();
        if (string.IsNullOrEmpty(name))
        {
            var peer = GetPeerAddress(_session);
            if (!XboxNameResolver.TryQueryNameAt(peer, out name))
            {
                if (IsConsoleAddress(peer))
                    name = peer.MapToIPv4().ToString();
                else
                    throw XbdmException.FromHResult("Could not read Xbox name.", XbdmHResults.NoXboxName);
            }
        }

        if (resolvable)
        {
            var peer = GetPeerAddress(_session);
            if (XboxNameResolver.TryResolveNameToAddress(name, out var resolved))
            {
                if (!AddressesMatch(peer, resolved))
                    throw XbdmException.FromHResult($"Xbox name '{name}' is not resolvable to the connected console.", XbdmHResults.CannotConnect);
            }
            else if (!IsConsoleAddress(peer) &&
                     !(IPAddress.TryParse(name, out var nameAsIp) && AddressesMatch(nameAsIp, peer)))
            {
                throw XbdmException.FromHResult($"Xbox name '{name}' is not resolvable to the connected console.", XbdmHResults.CannotConnect);
            }
        }

        return name;
    }

    private bool IsConsoleAddress(IPAddress peer)
    {
        if (IPAddress.TryParse(_consoleName, out var configured))
            return AddressesMatch(configured, peer);

        return false;
    }

    private static bool AddressesMatch(IPAddress left, IPAddress right) =>
        left.MapToIPv4().Equals(right.MapToIPv4());

    private string TryReadDebugNameAny()
    {
        var name = TryReadDebugName(_session);
        if (!string.IsNullOrEmpty(name))
            return name;

        return _sci.WithSession(TryReadDebugName);
    }

    private static string TryReadDebugName(XbdmProtocolSession session)
    {
        try
        {
            var (hr, line) = session.SendCommandRaw("DEBUGNAME");
            if (hr == XbdmHResults.Multiresponse)
            {
                foreach (var responseLine in session.ReadMultiresponse())
                {
                    var parsed = ParseDebugNameLine(responseLine);
                    if (!string.IsNullOrEmpty(parsed))
                        return parsed;
                }
            }
            else if (XbdmProtocol.IsCommandSuccess(hr))
            {
                var parsed = ParseDebugNameLine(line);
                if (!string.IsNullOrEmpty(parsed))
                    return parsed;

                // Some kits return the name on a follow-up line after the status line.
                var nextLine = session.ReceiveLine();
                if (nextLine != ".")
                {
                    parsed = ParseDebugNameLine(nextLine);
                    if (!string.IsNullOrEmpty(parsed))
                        return parsed;
                }
            }
        }
        catch (XbdmException)
        {
        }

        return string.Empty;
    }

    private static string ParseDebugNameLine(string line)
    {
        var fromParam = XbdmParamParser.GetSzParam(line, "name");
        if (!string.IsNullOrEmpty(fromParam))
            return fromParam;

        var stripped = XbdmProtocol.StripResponsePrefix(line).Trim();
        if (stripped.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
            stripped = stripped["name=".Length..].Trim();

        return stripped;
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

        // xbshlext/attrib.cpp + visit.cpp: desired readonly/hidden mask; zero -> NORMAL (0x80)
        // so filexfer.c appends READONLY=0/HIDDEN=0 instead of omitting flags.
        var effectiveAttributes = attributes;
        if (effectiveAttributes == 0)
            effectiveAttributes = XbdmConstants.AttrNormal;

        var readOnly = (effectiveAttributes & XbdmConstants.AttrReadOnly) != 0 ? 1 : 0;
        var hidden = (effectiveAttributes & XbdmConstants.AttrHidden) != 0 ? 1 : 0;

        var command =
            $"SETFILEATTRIBUTES NAME=\"{wirePath}\" CREATEHI=0x{createHigh:x8} CREATELO=0x{createLow:x8} " +
            $"CHANGEHI=0x{changeHigh:x8} CHANGELO=0x{changeLow:x8}";

        if (effectiveAttributes != 0)
            command += $" READONLY={readOnly} HIDDEN={hidden}";

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
        // Matches the original Neighborhood (xbshlext/prop.cpp): probe with a bogus user and treat
        // only XBDM_INVALIDCMD as "not supported". Any other response means the box understands
        // GETUSERPRIV, so the user-privilege feature is available.
        var (hr, _) = _session.SendCommandRaw("GETUSERPRIV NAME=BOGUS");
        return hr != XbdmHResults.InvalidCmd;
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
        _sci.InvalidateSharedConnection();
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

    /// <summary>Used by the shell extension native bridge.</summary>
    public (int HResult, string Line) TrySendCommandRaw(string command) => _session.SendCommandRaw(command);

    /// <summary>Used by the shell extension native bridge.</summary>
    public string SendCommandLine(string command) => _session.SendCommand(command);

    internal void SetConversationTimeout(TimeSpan timeout) => _session.SetReadTimeout(timeout);

    public void Dispose()
    {
        // Only tear down this connection's dedicated session. Invalidating the per-console
        // shared SCI pool here races with other live connections (e.g. Explorer re-enumerating
        // the parent folder right after a delete) and leaves stale sockets behind.
        _session.Dispose();
    }
}
