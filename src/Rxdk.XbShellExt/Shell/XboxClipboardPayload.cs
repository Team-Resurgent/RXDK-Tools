using System.Runtime.InteropServices;
using System.Text;
using Rxdk.XbShellExt.Interop;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Shell;

internal static class XboxClipboardPayload
{
    public const string FormatName = "XBOX_FILEDESCRIPTOR";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct FileDescriptorA
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

    public static nint Serialize(FileSelection selection, FileClipboardOperation operation)
    {
        if (selection.Items.Count == 0)
            return IntPtr.Zero;

        var headerSize = sizeof(int) + 260;
        var descriptorSize = Marshal.SizeOf<FileDescriptorA>();
        var totalSize = headerSize + descriptorSize * selection.Items.Count;
        var hGlobal = GlobalAlloc(0x0042, (nuint)totalSize);
        if (hGlobal == IntPtr.Zero)
            return IntPtr.Zero;

        var locked = GlobalLock(hGlobal);
        try
        {
            Marshal.WriteInt32(locked, selection.Items.Count);
            var folderBytes = Encoding.ASCII.GetBytes(selection.FolderDisplayPath);
            var folderOffset = IntPtr.Add(locked, sizeof(int));
            for (var i = 0; i < Math.Min(folderBytes.Length, 259); i++)
                Marshal.WriteByte(folderOffset, i, folderBytes[i]);

            var descriptorOffset = IntPtr.Add(locked, headerSize);
            for (var i = 0; i < selection.Items.Count; i++)
            {
                var item = selection.Items[i];
                var descriptor = new FileDescriptorA
                {
                    dwFlags = 0,
                    clsid = Guid.Empty,
                    cFileName = item.Name.TrimEnd(':'),
                };
                Marshal.StructureToPtr(descriptor, IntPtr.Add(descriptorOffset, i * descriptorSize), false);
            }
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }

    public static bool TryDeserialize(nint hGlobal, out FileSelection? selection, out FileClipboardOperation operation)
    {
        selection = null;
        operation = FileClipboardOperation.Copy;

        if (hGlobal == IntPtr.Zero)
            return false;

        var locked = GlobalLock(hGlobal);
        if (locked == IntPtr.Zero)
            return false;

        try
        {
            var count = Marshal.ReadInt32(locked);
            if (count <= 0 || count > 256)
                return false;

            var folderPath = Marshal.PtrToStringAnsi(IntPtr.Add(locked, sizeof(int))) ?? "";
            if (string.IsNullOrWhiteSpace(folderPath))
                return false;

            var consoleName = WirePathService.GetConsoleNameFromDisplayPath(folderPath);
            var headerSize = sizeof(int) + 260;
            var descriptorSize = Marshal.SizeOf<FileDescriptorA>();
            var items = new List<FileSelectionItem>(count);
            for (var i = 0; i < count; i++)
            {
                var descriptor = Marshal.PtrToStructure<FileDescriptorA>(
                    IntPtr.Add(locked, headerSize + i * descriptorSize));
                var name = descriptor.cFileName?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!WirePathService.TryBuildWirePathInFolder(folderPath, name, out var wirePath))
                    continue;

                items.Add(new FileSelectionItem
                {
                    Name = name,
                    WirePath = wirePath,
                    IsDirectory = false,
                });
            }

            if (items.Count == 0)
                return false;

            selection = new FileSelection
            {
                ConsoleName = consoleName,
                FolderDisplayPath = folderPath,
                Items = items,
            };
            return true;
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }
    }

    public static bool TryReadFromDataObject(Interop.IDataObject dataObject, out FileSelection? selection, out FileClipboardOperation operation)
    {
        selection = null;
        operation = FileClipboardOperation.Copy;

        var format = OleDataTransfer.CreateFormat(ClipboardFormats.XboxFileDescriptor);
        if (dataObject.GetData(ref format, out var medium) != HResults.Ok || medium.unionmember == IntPtr.Zero)
            return false;

        try
        {
            return TryDeserialize(medium.unionmember, out selection, out operation);
        }
        finally
        {
            OleDataTransfer.ReleaseMedium(ref medium);
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
