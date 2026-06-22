using System.Runtime.InteropServices;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbFile;

internal static class XbLocalFs
{
    public static FileAttributes GetAttributes(XbPath path)
    {
        var full = path.LocalFullPath;
        if (Directory.Exists(full))
            return FileAttributes.Directory;
        if (File.Exists(full))
            return File.GetAttributes(full);

        throw new XbFileException($"Cannot access file: {path.DisplayPath}");
    }

    public static bool Exists(XbPath path)
    {
        var full = path.LocalFullPath;
        return File.Exists(full) || Directory.Exists(full);
    }

    public static bool IsDirectory(XbPath path) =>
        GetAttributes(path).HasFlag(FileAttributes.Directory);

    public static DateTime GetChangeTime(XbPath path)
    {
        var full = path.LocalFullPath;
        if (Directory.Exists(full))
            return Directory.GetLastWriteTimeUtc(full);
        return File.GetLastWriteTimeUtc(full);
    }

    public static long GetSize(XbPath path) =>
        new FileInfo(path.LocalFullPath).Length;

    public static void CreateDirectory(XbPath path)
    {
        var full = path.LocalFullPath;
        if (Directory.Exists(full))
            return;

        Directory.CreateDirectory(full);
    }

    public static void EnsureDirectory(XbPath path)
    {
        var full = path.LocalFullPath;
        if (Directory.Exists(full))
            return;

        Directory.CreateDirectory(full);
    }

    public static void CopyFile(XbPath src, XbPath dst, bool overwrite)
    {
        File.Copy(src.LocalFullPath, dst.LocalFullPath, overwrite);
        CopyAttributes(src, dst);
    }

    public static void CopyAttributes(XbPath src, XbPath dst)
    {
        var srcAttrs = GetAttributes(src);
        var dstFull = dst.LocalFullPath;
        if (srcAttrs.HasFlag(FileAttributes.Directory))
            return;

        File.SetAttributes(dstFull, srcAttrs);
        File.SetCreationTimeUtc(dstFull, File.GetCreationTimeUtc(src.LocalFullPath));
        File.SetLastWriteTimeUtc(dstFull, File.GetLastWriteTimeUtc(src.LocalFullPath));
    }

    public static IEnumerable<string> EnumerateNames(XbPath path, bool includeHidden)
    {
        var (dir, pattern) = ResolveListingDirectory(path);

        foreach (var entry in Directory.EnumerateFileSystemEntries(dir, pattern))
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrEmpty(name))
                continue;

            if (!includeHidden)
            {
                var attrs = File.GetAttributes(entry);
                if (attrs.HasFlag(FileAttributes.Hidden))
                    continue;
            }

            yield return name;
        }
    }

    private static (string Dir, string Pattern) ResolveListingDirectory(XbPath path)
    {
        if (path.Name is "." or "")
            return (path.DirectoryPath, "*");

        var full = path.LocalFullPath;
        if (Directory.Exists(full))
            return (full, "*");

        return (path.DirectoryPath, path.Name);
    }

    public static (ulong free, ulong total) GetDiskFreeSpace(XbPath path)
    {
        var root = Path.GetPathRoot(path.LocalFullPath);
        if (string.IsNullOrEmpty(root))
            throw new XbFileException($"Cannot get disk free space for {path.DisplayPath}");

        if (!GetDiskFreeSpaceEx(root, out var free, out _, out var total))
            throw new XbFileException($"Cannot get disk free space for {path.DisplayPath}");

        return (free, total);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}

internal static class XbXboxFs
{
    public static XbdmDirEntry GetAttributes(XbdmConnection conn, XbPath path) =>
        conn.GetFileAttributes(path.WirePath);

    public static bool TryGetAttributes(XbdmConnection conn, XbPath path, out XbdmDirEntry? entry)
    {
        try
        {
            entry = conn.GetFileAttributes(path.WirePath);
            return true;
        }
        catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.NoSuchFile)
        {
            entry = null;
            return false;
        }
    }

    public static bool IsDirectory(XbdmDirEntry entry) =>
        (entry.Attributes & XbdmConstants.AttrDirectory) != 0;

    public static void CreateDirectory(XbdmConnection conn, XbPath path)
    {
        try
        {
            conn.CreateDirectory(path.WirePath);
        }
        catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
        {
        }
    }

    public static void EnsureDirectoryTree(XbdmConnection conn, XbPath path)
    {
        var wire = path.WirePath.TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(wire))
            return;

        var parts = wire.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return;

        var drive = parts[0].Length > 0 && parts[0][^1] == ':'
            ? parts[0][..^1]
            : parts[0];
        var current = $"{drive}:\\";
        for (var i = 1; i < parts.Length; i++)
        {
            current = $"{current.TrimEnd('\\')}\\{parts[i]}";
            CreateDirectory(conn, XbPath.Parse("x" + current));
        }
    }

    public static void SetAttributesFromLocal(XbdmConnection conn, XbPath wirePath, FileAttributes localAttrs)
    {
        var attrs = ToXbdmAttributes(localAttrs);
        conn.SetFileAttributes(wirePath.WirePath, attrs);
    }

    public static uint ToXbdmAttributes(FileAttributes attrs)
    {
        uint value = 0;
        if (attrs.HasFlag(FileAttributes.ReadOnly))
            value |= XbdmConstants.AttrReadOnly;
        if (attrs.HasFlag(FileAttributes.Hidden))
            value |= XbdmConstants.AttrHidden;
        return value;
    }

    public static IEnumerable<XbdmDirEntry> ListDirectory(XbdmConnection conn, XbPath path) =>
        conn.ListDirectory(path.WireDirectoryPath);

    public static void SendFile(XbdmConnection conn, string localPath, string wirePath) =>
        conn.SendFile(localPath, wirePath);

    public static void ReceiveFile(XbdmConnection conn, string wirePath, string localPath) =>
        conn.ReceiveFile(wirePath, localPath);

    public static void CopyWireToWire(XbdmConnection conn, string srcWire, string dstWire, bool isDirectory)
    {
        if (!isDirectory)
        {
            var temp = Path.Combine(Path.GetTempPath(), "xbcp.tmp");
            try
            {
                conn.ReceiveFile(srcWire, temp);
                conn.SendFile(temp, dstWire);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            return;
        }

        CreateDirectory(conn, XbPath.Parse("x" + dstWire));
        foreach (var entry in conn.ListDirectory(srcWire))
        {
            var childSrc = $"{srcWire.TrimEnd('\\')}\\{entry.Name}";
            var childDst = $"{dstWire.TrimEnd('\\')}\\{entry.Name}";
            var childIsDir = (entry.Attributes & XbdmConstants.AttrDirectory) != 0;
            CopyWireToWire(conn, childSrc, childDst, childIsDir);
        }
    }
}
