using System.Runtime.InteropServices;
using Rxdk.Xbdm.Managed;
using Rxdk.XbShellExt.Interop;

namespace Rxdk.XbShellExt.Shell;

internal static class XboxDragFormats
{
    private const uint FdAttributes = 0x00000004;
    private const uint FdCreationTime = 0x00000008;
    private const uint FdFileSize = 0x00000040;
    private const uint FdWriteTime = 0x00000080;
    private const uint FdProgressUi = 0x00004000;
    private const uint FileAttributeDirectory = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct FileDescriptorW
    {
        public uint dwFlags;
        public Guid clsid;
        public long sizel;
        public long pointl;
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
    }

    public static nint CreateFileGroupDescriptor(IReadOnlyList<XboxDragEntry> entries)
    {
        if (entries.Count == 0)
            return IntPtr.Zero;

        var descriptorSize = Marshal.SizeOf<FileDescriptorW>();
        var totalSize = sizeof(uint) + entries.Count * descriptorSize;
        var hGlobal = GlobalAlloc(0x0042, (nuint)totalSize);
        if (hGlobal == IntPtr.Zero)
            return IntPtr.Zero;

        var locked = GlobalLock(hGlobal);
        try
        {
            Marshal.WriteInt32(locked, entries.Count);
            var offset = sizeof(uint);
            for (var i = 0; i < entries.Count; i++)
            {
                var descriptor = ToFileDescriptor(entries[i]);
                Marshal.StructureToPtr(descriptor, IntPtr.Add(locked, offset), false);
                offset += descriptorSize;
            }
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }

    public static INativeComStream? OpenFileContentsStream(
        XboxDragEntry entry,
        XboxDragTransferSession transferSession)
    {
        if (entry.IsDirectory)
            return null;

        return transferSession.OpenStream(entry);
    }

    private static FileDescriptorW ToFileDescriptor(XboxDragEntry entry)
    {
        var flags = FdAttributes | FdWriteTime | FdProgressUi;
        if (entry.CreationTimeUnix.HasValue)
            flags |= FdCreationTime;
        if (!entry.IsDirectory && entry.Size > 0)
            flags |= FdFileSize;

        var attributes = entry.Attributes;
        if (entry.IsDirectory)
            attributes |= FileAttributeDirectory;

        return new FileDescriptorW
        {
            dwFlags = flags,
            clsid = Guid.Empty,
            sizel = default,
            pointl = default,
            dwFileAttributes = attributes,
            ftCreationTime = entry.CreationTimeUnix.HasValue
                ? UnixToFileTime(entry.CreationTimeUnix.Value)
                : 0,
            ftLastAccessTime = UnixToFileTime(entry.ChangeTimeUnix),
            ftLastWriteTime = UnixToFileTime(entry.ChangeTimeUnix),
            nFileSizeHigh = entry.IsDirectory ? 0 : (uint)(entry.Size >> 32),
            nFileSizeLow = entry.IsDirectory ? 0 : (uint)(entry.Size & 0xFFFFFFFF),
            cFileName = TruncateDescriptorName(entry.RelativePath.Replace('/', '\\')),
        };
    }

    private static string TruncateDescriptorName(string relativePath)
    {
        if (relativePath.Length <= 260)
            return relativePath;

        return relativePath[^260..];
    }

    private static long UnixToFileTime(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return 0;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.ToFileTimeUtc();
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

}
