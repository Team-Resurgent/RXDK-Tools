using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

/// <summary>
/// App-facing facade over the managed XBDM client (Neighborhood, tools).
/// </summary>
public sealed class XbdmSession : IDisposable
{
    private static readonly object InitLock = new();
    private static ManagedXbdmClient? _client;
    private static int _initCount;

    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initCount == 0)
            {
                _client = new ManagedXbdmClient();
                _client.Initialize();
            }

            _initCount++;
        }
    }

    public static string GetDefaultConsoleName()
    {
        EnsureInitialized();
        return _client!.GetDefaultConsoleName();
    }

    public static void SetDefaultConsoleName(string name)
    {
        EnsureInitialized();
        _client!.SetDefaultConsoleName(name);
    }

    public static XbdmConnection Connect(string consoleName) =>
        Connect(consoleName, password: null);

    public static XbdmConnection Connect(string consoleName, string? password)
    {
        EnsureInitialized();
        var connection = string.IsNullOrWhiteSpace(password)
            ? _client!.Connect(consoleName)
            : _client!.Connect(consoleName, new XbdmConnectOptions { AdminPassword = password });
        return new XbdmConnection(connection);
    }

    public void Dispose()
    {
        lock (InitLock)
        {
            if (_initCount <= 0)
                return;

            _initCount--;
            if (_initCount == 0)
            {
                _client?.Shutdown();
                _client = null;
            }
        }
    }
}

public sealed class XbdmConnection : IDisposable
{
    private readonly IXbdmConnection _inner;

    internal XbdmConnection(IXbdmConnection inner) => _inner = inner;

    public IXbdmDebugConnection Debug => _inner.Debug;

    public IReadOnlyList<char> ListDrives() => _inner.ListDrives();

    public IReadOnlyList<XbdmDirEntry> ListDirectory(string wirePath, int maxEntries = 512) =>
        _inner.ListDirectory(wirePath, maxEntries);

    public XbdmDirEntry GetFileAttributes(string wirePath) => _inner.GetFileAttributes(wirePath);

    public void SendFile(string localPath, string wirePath) => _inner.SendFile(localPath, wirePath);
    public void ReceiveFile(string wirePath, string localPath) => _inner.ReceiveFile(wirePath, localPath);

    public XbdmFileReceiver OpenFileReceiver(string wirePath)
    {
        if (_inner is ManagedXbdmConnection managed)
            return managed.OpenFileReceiver(wirePath);

        throw new NotSupportedException("Streaming receive requires the managed XBDM connection.");
    }
    public void Delete(string wirePath, bool isDirectory) => _inner.Delete(wirePath, isDirectory);
    public void Rename(string fromWire, string toWire) => _inner.Rename(fromWire, toWire);
    public void CreateDirectory(string wirePath) => _inner.CreateDirectory(wirePath);
    public void Reboot(bool cold, string? launchPath = null) => _inner.Reboot(cold, launchPath);
    public void Reboot(uint flags) => _inner.Reboot(flags);

    public (ulong FreeBytes, ulong TotalBytes) GetDiskFreeSpace(string driveWire) =>
        _inner.GetDiskFreeSpace(driveWire);

    public uint? TryResolveXboxAddress() => _inner.TryResolveXboxAddress();
    public uint? TryGetAltAddress() => _inner.TryGetAltAddress();
    public string GetNameOfXbox(bool resolvable) => _inner.GetNameOfXbox(resolvable);
    public string GetXbeLaunchPath() => _inner.GetXbeLaunchPath();
    public XbdmXbeInfo GetXbeInfo(string name) => _inner.GetXbeInfo(name);
    public void CaptureScreenshot(string localBmpPath) => _inner.CaptureScreenshot(localBmpPath);
    public void SetFileAttributes(string wirePath, uint attributes) => _inner.SetFileAttributes(wirePath, attributes);

    public bool IsSecurityEnabled() => _inner.IsSecurityEnabled();
    public bool SupportsUserPrivileges() => _inner.SupportsUserPrivileges();
    public uint GetUserAccess(string? userName = null) => _inner.GetUserAccess(userName);

    public IReadOnlyList<XbdmUser> ListUsers(int maxUsers = 64) => _inner.ListUsers(maxUsers);

    public void AddUser(string userName, uint access) => _inner.AddUser(userName, access);
    public void RemoveUser(string userName) => _inner.RemoveUser(userName);
    public void SetUserAccess(string userName, uint access) => _inner.SetUserAccess(userName, access);
    public void EnableSecurity(bool enable) => _inner.EnableSecurity(enable);
    public void SetAdminPassword(string password) => _inner.SetAdminPassword(password);
    public void UseSecureConnection(string password) => _inner.UseSecureConnection(password);
    public void UseSharedConnection(bool enable) => _inner.UseSharedConnection(enable);

    public (int HResult, string Line) TrySendCommandRaw(string command)
    {
        if (_inner is ManagedXbdmConnection managed)
            return managed.TrySendCommandRaw(command);
        throw new NotSupportedException("Raw commands require the managed XBDM connection.");
    }

    public string SendCommandLine(string command)
    {
        if (_inner is ManagedXbdmConnection managed)
            return managed.SendCommandLine(command);
        throw new NotSupportedException("Raw commands require the managed XBDM connection.");
    }

    public void Dispose() => _inner.Dispose();
}
