namespace Rxdk.Xbdm;

/// <summary>
/// Client for XBDM kit access. The Avalonia app and bridge use the managed protocol stack
/// in <c>Rxdk.Xbdm.Managed</c>.
/// </summary>
public interface IXbdmClient : IDisposable
{
    string BackendName { get; }

    void Initialize();
    void Shutdown();

    string GetDefaultConsoleName();
    void SetDefaultConsoleName(string name);

    IXbdmConnection Connect(string consoleName);
}

/// <summary>Per-console XBDM session matching <c>xbdm.h</c> connection APIs.</summary>
public interface IXbdmConnection : IDisposable
{
    string ConsoleName { get; }

    /// <summary>Debugger APIs from <c>xboxdbg.h</c>.</summary>
    IXbdmDebugConnection Debug { get; }

    IReadOnlyList<char> ListDrives();
    IReadOnlyList<XbdmDirEntry> ListDirectory(string wirePath, int maxEntries = 512);
    XbdmDirEntry GetFileAttributes(string wirePath);

    void SendFile(string localPath, string wirePath);
    void ReceiveFile(string wirePath, string localPath);
    void Delete(string wirePath, bool isDirectory);
    void Rename(string fromWire, string toWire);
    void CreateDirectory(string wirePath);

    void Reboot(bool cold, string? launchPath = null);

    /// <summary>Full <c>DmReboot</c> flags (<see cref="XbdmDebugConstants"/>).</summary>
    void Reboot(uint flags);

    (ulong FreeBytes, ulong TotalBytes) GetDiskFreeSpace(string driveWire);

    uint? TryResolveXboxAddress();
    uint? TryGetAltAddress();
    string GetNameOfXbox(bool resolvable);
    string GetXbeLaunchPath();
    XbdmXbeInfo GetXbeInfo(string name);

    void CaptureScreenshot(string localBmpPath);
    void SyncConsoleClock();
    void SetFileAttributes(string wirePath, uint attributes);

    bool IsSecurityEnabled();
    bool SupportsUserPrivileges();
    uint GetUserAccess(string? userName = null);
    IReadOnlyList<XbdmUser> ListUsers(int maxUsers = 64);
    void AddUser(string userName, uint access);
    void RemoveUser(string userName);
    void SetUserAccess(string userName, uint access);
    void EnableSecurity(bool enable);
    void SetAdminPassword(string password);
    void UseSecureConnection(string password);
    void UseSharedConnection(bool enable);
}
