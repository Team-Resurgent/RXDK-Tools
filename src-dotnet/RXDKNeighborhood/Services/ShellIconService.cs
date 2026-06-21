using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
#if WINDOWS
using System.Drawing;
#endif

namespace RXDKNeighborhood.Services;

public static class ShellIconService
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint ShgfiUseFileAttributes = 0x10;

    private static readonly ConcurrentDictionary<string, IImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Shfileinfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref Shfileinfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static IImage? GetConsoleIcon(bool isDefault) =>
        LoadAsset(isDefault ? "console-default.ico" : "console.ico");

    public static IImage? GetNeighborhoodIcon() => LoadAsset("xbox.ico");

    public static IImage? GetDriveIcon() => LoadAsset("volume.ico");

    public static IImage? GetFolderIcon() => LoadAsset("folder.ico");

    public static IImage? GetMultiItemIcon() => LoadAsset("muldoc.ico");

    public static IImage? GetItemIcon(string name, bool isDirectory)
    {
        if (isDirectory)
            return GetFolderIcon();

        var ext = Path.GetExtension(name);
        if (string.Equals(ext, ".xbe", StringComparison.OrdinalIgnoreCase))
            return LoadAsset("xbe.ico");

        if (OperatingSystem.IsWindows())
            return GetWindowsShellIcon(name, isDirectory);

        return LoadAsset("placeholder.ico");
    }

    [SupportedOSPlatform("windows")]
    private static IImage? GetWindowsShellIcon(string name, bool isDirectory)
    {
#if WINDOWS
        var cacheKey = $"shell:{name}:{isDirectory}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var attrs = isDirectory ? FileAttributeDirectory : 0u;
        var info = new Shfileinfo();
        var result = SHGetFileInfo(
            name,
            attrs,
            ref info,
            (uint)Marshal.SizeOf<Shfileinfo>(),
            ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return LoadAsset("placeholder.ico");

        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            var image = new Avalonia.Media.Imaging.Bitmap(stream);
            Cache[cacheKey] = image;
            return image;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
#else
        return LoadAsset("placeholder.ico");
#endif
    }

    private static IImage? LoadAsset(string fileName)
    {
        if (Cache.TryGetValue($"asset:{fileName}", out var cached))
            return cached;

        try
        {
            var uri = new Uri($"avares://RXDKNeighborhood/Assets/{fileName}");
            using var stream = AssetLoader.Open(uri);
            var image = new Avalonia.Media.Imaging.Bitmap(stream);
            Cache[$"asset:{fileName}"] = image;
            return image;
        }
        catch
        {
            Cache[$"asset:{fileName}"] = null;
            return null;
        }
    }
}
