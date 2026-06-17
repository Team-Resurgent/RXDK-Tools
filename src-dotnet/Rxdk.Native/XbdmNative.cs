using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Rxdk.Native;

public static class XbdmConstants
{
    public const int AbiVersion = 1;
    public const int MaxName = 256;
    public const int MaxPath = 260;
    public const int MaxDrives = 32;
    public const uint AttrDirectory = 0x10;
    public const uint AttrReadOnly = 0x01;
    public const uint AttrHidden = 0x02;

    public const uint PrivRead = 0x0001;
    public const uint PrivWrite = 0x0002;
    public const uint PrivControl = 0x0004;
    public const uint PrivConfigure = 0x0008;
    public const uint PrivManage = 0x0010;
    public const uint PrivAll = 0x001F;
    public const uint PrivInitial = 0x0003;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct NativeAbiInfo
{
    public uint AbiVersion;
    public uint Build;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct XbdmDirEntry
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = XbdmConstants.MaxName)]
    public string Name;

    public ulong Size;
    public uint Attributes;
    public long ChangeTimeUnix;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct XbdmUser
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = XbdmConstants.MaxName)]
    public string UserName;

    public uint AccessPrivileges;
}

public static class XbdmNative
{
    private const string LibName = "xbdm";

    static XbdmNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(XbdmNative).Assembly, ResolveNative);
    }

    private static IntPtr ResolveNative(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals(LibName, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        if (NativeLibrary.TryLoad(LibName, assembly, searchPath, out var handle))
            return handle;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "xbdm.dll"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "xbdm.dll"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "out", "bin", "x64", "Release", "xbdm.dll")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "out", "bin", "x64", "Release", "xbdm.dll")),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int xbdm_init(ref NativeAbiInfo info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void xbdm_shutdown();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_get_default_console_name(byte[] buf, int bufLen);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_set_default_console_name(string name);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_connect(string consoleName, out IntPtr connection);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void xbdm_disconnect(IntPtr connection);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_list_drives(IntPtr connection, byte[] drives, ref int driveCount);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_list_dir(
        IntPtr connection,
        string wirePath,
        [Out] XbdmDirEntry[] entries,
        int maxEntries,
        ref int entryCount);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_get_file_attributes(IntPtr connection, string wirePath, out XbdmDirEntry entry);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_send_file(IntPtr connection, string localPath, string wirePath);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_receive_file(IntPtr connection, string wirePath, string localPath);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_delete(IntPtr connection, string wirePath, int isDirectory);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_rename(IntPtr connection, string fromWire, string toWire);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_create_directory(IntPtr connection, string wirePath);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_reboot(IntPtr connection, int cold, string? launchPath);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_get_disk_free_space(IntPtr connection, string driveWire, out ulong freeBytes, out ulong totalBytes);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_resolve_xbox_address(IntPtr connection, out uint address);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_get_alt_address(IntPtr connection, out uint address);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_get_name_of_xbox(IntPtr connection, byte[] buf, int bufLen, int resolvable);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_get_xbe_launch_path(IntPtr connection, byte[] buf, int bufLen);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_screenshot(IntPtr connection, string localBmpPath);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_set_file_attributes(IntPtr connection, string wirePath, uint attributes);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_is_security_enabled(IntPtr connection, out int enabled);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_security_supports_user_priv(IntPtr connection, out int supported);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_get_user_access(IntPtr connection, string? userName, out uint access);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_list_users(IntPtr connection, [Out] XbdmUser[] users, int maxUsers, ref int userCount);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_add_user(IntPtr connection, string userName, uint access);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_remove_user(IntPtr connection, string userName);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_set_user_access(IntPtr connection, string userName, uint access);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_enable_security(IntPtr connection, int enable);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_set_admin_password(IntPtr connection, string password);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_use_secure_connection(IntPtr connection, string password);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_use_shared_connection(IntPtr connection, int enable);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int xbdm_last_hresult();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int xbdm_last_error_message(byte[] buf, int bufLen);

    public static string ReadUtf8Buffer(byte[] bytes)
    {
        var len = Array.IndexOf(bytes, (byte)0);
        if (len < 0)
            len = bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, len);
    }
}
