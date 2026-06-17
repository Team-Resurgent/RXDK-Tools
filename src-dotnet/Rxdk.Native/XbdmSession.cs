namespace Rxdk.Native;

public sealed class XbdmSession : IDisposable
{
    private static int _initCount;
    private static readonly object InitLock = new();

    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initCount == 0)
            {
                var info = new XbdmAbiInfo();
                if (XbdmNative.xbdm_init(ref info) != 0)
                    throw new XbdmException("xbdm_init failed", XbdmNative.xbdm_last_hresult());
            }

            _initCount++;
        }
    }

    public static string GetDefaultConsoleName()
    {
        EnsureInitialized();
        var buf = new byte[XbdmConstants.MaxName];
        if (XbdmNative.xbdm_get_default_console_name(buf, buf.Length) != 0)
            throw XbdmException.FromLastError("Could not read default console name.");

        return XbdmNative.ReadUtf8Buffer(buf);
    }

    public static void SetDefaultConsoleName(string name)
    {
        EnsureInitialized();
        if (XbdmNative.xbdm_set_default_console_name(name) != 0)
            throw XbdmException.FromLastError("Could not set default console name.");
    }

    public static XbdmConnection Connect(string consoleName)
    {
        EnsureInitialized();
        if (XbdmNative.xbdm_connect(consoleName, out var handle) != 0 || handle == IntPtr.Zero)
            throw XbdmException.FromLastError($"Could not connect to '{consoleName}'.");

        return new XbdmConnection(handle);
    }

    public void Dispose()
    {
        lock (InitLock)
        {
            if (_initCount <= 0)
                return;

            _initCount--;
            if (_initCount == 0)
                XbdmNative.xbdm_shutdown();
        }
    }
}

public sealed class XbdmConnection : IDisposable
{
    private IntPtr _handle;

    internal XbdmConnection(IntPtr handle) => _handle = handle;

    public IReadOnlyList<char> ListDrives()
    {
        var drives = new byte[XbdmConstants.MaxDrives];
        var count = drives.Length;
        if (XbdmNative.xbdm_list_drives(_handle, drives, ref count) != 0)
            throw XbdmException.FromLastError("Could not list drives.");

        var result = new List<char>(count);
        for (var i = 0; i < count; i++)
            result.Add((char)drives[i]);
        return result;
    }

    public IReadOnlyList<XbdmDirEntry> ListDirectory(string wirePath, int maxEntries = 512)
    {
        var entries = new XbdmDirEntry[maxEntries];
        var count = 0;
        if (XbdmNative.xbdm_list_dir(_handle, wirePath, entries, maxEntries, ref count) != 0)
            throw XbdmException.FromLastError($"Could not list '{wirePath}'.");

        return entries.Take(count).ToArray();
    }

    public XbdmDirEntry GetFileAttributes(string wirePath)
    {
        if (XbdmNative.xbdm_get_file_attributes(_handle, wirePath, out var entry) != 0)
            throw XbdmException.FromLastError($"Could not get attributes for '{wirePath}'.");
        return entry;
    }

    public void SendFile(string localPath, string wirePath)
    {
        if (XbdmNative.xbdm_send_file(_handle, localPath, wirePath) != 0)
            throw XbdmException.FromLastError($"Could not send '{localPath}'.");
    }

    public void ReceiveFile(string wirePath, string localPath)
    {
        if (XbdmNative.xbdm_receive_file(_handle, wirePath, localPath) != 0)
            throw XbdmException.FromLastError($"Could not receive '{wirePath}'.");
    }

    public void Delete(string wirePath, bool isDirectory)
    {
        if (XbdmNative.xbdm_delete(_handle, wirePath, isDirectory ? 1 : 0) != 0)
            throw XbdmException.FromLastError($"Could not delete '{wirePath}'.");
    }

    public void Rename(string fromWire, string toWire)
    {
        if (XbdmNative.xbdm_rename(_handle, fromWire, toWire) != 0)
            throw XbdmException.FromLastError($"Could not rename '{fromWire}'.");
    }

    public void CreateDirectory(string wirePath)
    {
        if (XbdmNative.xbdm_create_directory(_handle, wirePath) != 0)
            throw XbdmException.FromLastError($"Could not create directory '{wirePath}'.");
    }

    public void Reboot(bool cold, string? launchPath = null)
    {
        if (XbdmNative.xbdm_reboot(_handle, cold ? 1 : 0, launchPath) != 0)
            throw XbdmException.FromLastError("Reboot failed.");
    }

    public (ulong FreeBytes, ulong TotalBytes) GetDiskFreeSpace(string driveWire)
    {
        if (XbdmNative.xbdm_get_disk_free_space(_handle, driveWire, out var freeBytes, out var totalBytes) != 0)
            throw XbdmException.FromLastError($"Could not read disk space for '{driveWire}'.");
        return (freeBytes, totalBytes);
    }

    public uint? TryResolveXboxAddress()
    {
        if (XbdmNative.xbdm_resolve_xbox_address(_handle, out var address) != 0)
            return null;
        return address;
    }

    public uint? TryGetAltAddress()
    {
        if (XbdmNative.xbdm_get_alt_address(_handle, out var address) != 0)
            return null;
        return address;
    }

    public string GetNameOfXbox(bool resolvable)
    {
        var buf = new byte[XbdmConstants.MaxPath];
        if (XbdmNative.xbdm_get_name_of_xbox(_handle, buf, buf.Length, resolvable ? 1 : 0) != 0)
            throw XbdmException.FromLastError("Could not read Xbox name.");
        return XbdmNative.ReadUtf8Buffer(buf);
    }

    public string GetXbeLaunchPath()
    {
        var buf = new byte[XbdmConstants.MaxPath + 1];
        if (XbdmNative.xbdm_get_xbe_launch_path(_handle, buf, buf.Length) != 0)
            throw XbdmException.FromLastError("Could not read running title.");
        return XbdmNative.ReadUtf8Buffer(buf);
    }

    public void CaptureScreenshot(string localBmpPath)
    {
        if (XbdmNative.xbdm_screenshot(_handle, localBmpPath) != 0)
            throw XbdmException.FromLastError("Screenshot failed.");
    }

    public void SetFileAttributes(string wirePath, uint attributes)
    {
        if (XbdmNative.xbdm_set_file_attributes(_handle, wirePath, attributes) != 0)
            throw XbdmException.FromLastError($"Could not set attributes for '{wirePath}'.");
    }

    public bool IsSecurityEnabled()
    {
        if (XbdmNative.xbdm_is_security_enabled(_handle, out var enabled) != 0)
            throw XbdmException.FromLastError("Could not read security state.");
        return enabled != 0;
    }

    public bool SupportsUserPrivileges()
    {
        if (XbdmNative.xbdm_security_supports_user_priv(_handle, out var supported) != 0)
            return false;
        return supported != 0;
    }

    public uint GetUserAccess(string? userName = null)
    {
        if (XbdmNative.xbdm_get_user_access(_handle, userName, out var access) != 0)
            throw XbdmException.FromLastError("Could not read user access.");
        return access;
    }

    public IReadOnlyList<XbdmUser> ListUsers(int maxUsers = 64)
    {
        var users = new XbdmUser[maxUsers];
        var count = 0;
        if (XbdmNative.xbdm_list_users(_handle, users, maxUsers, ref count) != 0)
            throw XbdmException.FromLastError("Could not list users.");
        return users.Take(count).ToArray();
    }

    public void AddUser(string userName, uint access)
    {
        if (XbdmNative.xbdm_add_user(_handle, userName, access) != 0)
            throw XbdmException.FromLastError($"Could not add user '{userName}'.");
    }

    public void RemoveUser(string userName)
    {
        if (XbdmNative.xbdm_remove_user(_handle, userName) != 0)
            throw XbdmException.FromLastError($"Could not remove user '{userName}'.");
    }

    public void SetUserAccess(string userName, uint access)
    {
        if (XbdmNative.xbdm_set_user_access(_handle, userName, access) != 0)
            throw XbdmException.FromLastError($"Could not set access for '{userName}'.");
    }

    public void EnableSecurity(bool enable)
    {
        if (XbdmNative.xbdm_enable_security(_handle, enable ? 1 : 0) != 0)
            throw XbdmException.FromLastError("Could not change security state.");
    }

    public void SetAdminPassword(string password)
    {
        if (XbdmNative.xbdm_set_admin_password(_handle, password) != 0)
            throw XbdmException.FromLastError("Could not set admin password.");
    }

    public void UseSecureConnection(string password)
    {
        if (XbdmNative.xbdm_use_secure_connection(_handle, password) != 0)
            throw XbdmException.FromLastError("Secure connection failed.");
    }

    public void UseSharedConnection(bool enable)
    {
        if (XbdmNative.xbdm_use_shared_connection(_handle, enable ? 1 : 0) != 0)
            throw XbdmException.FromLastError("Could not change shared connection mode.");
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        XbdmNative.xbdm_disconnect(_handle);
        _handle = IntPtr.Zero;
    }
}

public sealed class XbdmException : Exception
{
    public int HResultCode { get; }

    public XbdmException(string message, int hresultCode) : base(message)
    {
        HResultCode = hresultCode;
    }

    public static XbdmException FromLastError(string message)
    {
        var hr = XbdmNative.xbdm_last_hresult();
        var buf = new byte[512];
        XbdmNative.xbdm_last_error_message(buf, buf.Length);
        var detail = XbdmNative.ReadUtf8Buffer(buf);
        if (!string.IsNullOrWhiteSpace(detail))
            message = $"{message} {detail}";
        return new XbdmException(message, hr);
    }
}
