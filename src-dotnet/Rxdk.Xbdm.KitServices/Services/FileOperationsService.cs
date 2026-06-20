using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.KitServices.Services;

public interface IFileOperationHost
{
    Task<bool> ConfirmDeleteAsync(IReadOnlyList<FileSelectionItem> items);
    Task<string?> PromptRenameAsync(string currentName);
    Task<string?> PickLocalFolderAsync(string title);
    void ShowError(string message);
    void ShowInfo(string message);
}

public class FileOperationsService
{
    private readonly FileClipboardService _clipboard;

    public FileOperationsService(FileClipboardService clipboard) => _clipboard = clipboard;

    public FileClipboardService Clipboard => _clipboard;

    public void Cut(FileSelection selection) => _clipboard.Cut(selection);

    public void Copy(FileSelection selection) => _clipboard.Copy(selection);

    public void Paste(string consoleName, string targetFolderDisplayPath, IFileOperationHost host)
    {
        if (!_clipboard.HasItems)
            return;

        if (WirePathService.IsDriveListing(targetFolderDisplayPath, consoleName))
            throw new InvalidOperationException("Open a drive or folder before pasting.");

        if (!string.Equals(consoleName, _clipboard.ConsoleName, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Cross-console paste is not supported yet.");

        using var conn = XbdmSession.Connect(consoleName);
        foreach (var name in _clipboard.Names)
        {
            if (!WirePathService.TryBuildWirePathInFolder(_clipboard.FolderPath, name, out var srcWire))
                continue;
            if (!WirePathService.TryBuildWirePathInFolder(targetFolderDisplayPath, name, out var dstWire))
                continue;

            var isDir = IsDirectory(conn, srcWire);
            if (_clipboard.Operation == FileClipboardOperation.Cut &&
                string.Equals(_clipboard.FolderPath, targetFolderDisplayPath, StringComparison.OrdinalIgnoreCase))
            {
                conn.Rename(srcWire, dstWire);
            }
            else
            {
                CopyWireItem(conn, srcWire, dstWire, isDir);
                if (_clipboard.Operation == FileClipboardOperation.Cut)
                    DeleteWirePathRecursive(conn, srcWire, isDir);
            }
        }

        if (_clipboard.Operation == FileClipboardOperation.Cut)
            _clipboard.Clear();
    }

    public async Task<bool> DeleteAsync(FileSelection selection, IFileOperationHost host)
    {
        if (selection.Items.Count == 0)
            return false;

        if (!await host.ConfirmDeleteAsync(selection.Items))
            return false;

        DeleteSelectionWithoutPrompt(selection);
        return true;
    }

    public void DeleteSelectionWithoutPrompt(FileSelection selection)
    {
        if (selection.Items.Count == 0)
            return;

        using var conn = XbdmSession.Connect(selection.ConsoleName);
        foreach (var item in selection.Items)
            DeleteWirePathRecursive(conn, item.WirePath, item.IsDirectory);
    }

    public void Rename(FileSelection selection, string newName)
    {
        if (selection.Items.Count != 1)
            throw new InvalidOperationException("Select exactly one item to rename.");

        var item = selection.Items[0];
        var newDisplay = WirePathService.GetItemDisplayPath(selection.FolderDisplayPath, newName);
        if (!WirePathService.TryBuildWirePath(newDisplay, out var newWire))
            throw new InvalidOperationException("Invalid new name.");

        using var conn = XbdmSession.Connect(selection.ConsoleName);
        conn.Rename(item.WirePath, newWire);
    }

    public void CreateNewFolder(string consoleName, string folderDisplayPath)
    {
        if (WirePathService.IsDriveListing(folderDisplayPath, consoleName))
            throw new InvalidOperationException("Open a drive or folder before creating a new folder.");

        using var conn = XbdmSession.Connect(consoleName);
        const string baseName = "New Folder";
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var tryName = attempt == 0 ? baseName : $"{baseName} ({attempt})";
            if (!WirePathService.TryBuildWirePathInFolder(folderDisplayPath, tryName, out var wirePath))
                continue;

            try
            {
                conn.CreateDirectory(wirePath);
                return;
            }
            catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
            {
                // try next name
            }
        }

        throw new InvalidOperationException("Could not create a new folder.");
    }

    public void ExportToPc(FileSelection selection, string localDirectory)
    {
        if (selection.Items.Count == 0)
            return;

        using var conn = XbdmSession.Connect(selection.ConsoleName);
        foreach (var item in selection.Items)
            ReceiveWireToLocal(conn, item.WirePath, localDirectory, item.Name, item.IsDirectory);
    }

    public IReadOnlyList<string> PrepareDragExport(FileSelection selection)
    {
        if (selection.Items.Count == 0)
            return Array.Empty<string>();

        var tempRoot = Path.Combine(Path.GetTempPath(), "RXDKNeighborhoodDrag");
        Directory.CreateDirectory(tempRoot);

        var paths = new List<string>();
        using var conn = XbdmSession.Connect(selection.ConsoleName);
        foreach (var item in selection.Items)
        {
            ReceiveWireToLocal(conn, item.WirePath, tempRoot, item.Name, item.IsDirectory);
            paths.Add(Path.Combine(tempRoot, item.Name));
        }

        return paths;
    }

    public void UploadFromPc(string consoleName, string targetFolderDisplayPath, IEnumerable<string> localPaths)
    {
        if (WirePathService.IsDriveListing(targetFolderDisplayPath, consoleName) ||
            string.Equals(targetFolderDisplayPath, consoleName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Open a drive or folder before dropping files.");

        if (!WirePathService.TryBuildWirePath(targetFolderDisplayPath, out var wireFolder))
            throw new InvalidOperationException("Invalid target folder.");

        using var conn = XbdmSession.Connect(consoleName);
        foreach (var localPath in localPaths)
        {
            var name = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var targetWire = $"{wireFolder.TrimEnd('\\')}\\{name}";
            if (Directory.Exists(localPath))
                SendLocalToWire(conn, localPath, targetWire);
            else if (File.Exists(localPath))
                conn.SendFile(localPath, targetWire);
        }
    }

    public void LaunchXbe(FileSelection selection)
    {
        if (selection.Items.Count != 1)
            throw new InvalidOperationException("Select exactly one file to launch.");

        using var conn = XbdmSession.Connect(selection.ConsoleName);
        conn.Reboot(cold: false, selection.Items[0].WirePath);
    }

    public void Reboot(string consoleName, bool cold, string? launchWirePath = null)
    {
        using var conn = XbdmSession.Connect(consoleName);
        conn.Reboot(cold, launchWirePath);
    }

    private static bool IsDirectory(XbdmConnection conn, string wirePath)
    {
        var attrs = conn.GetFileAttributes(wirePath);
        return (attrs.Attributes & XbdmConstants.AttrDirectory) != 0;
    }

    private static void CopyWireItem(XbdmConnection conn, string srcWire, string dstWire, bool isDir)
    {
        if (!isDir)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "rxdkxfer.tmp");
            try
            {
                conn.ReceiveFile(srcWire, tempPath);
                conn.SendFile(tempPath, dstWire);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            return;
        }

        try
        {
            conn.CreateDirectory(dstWire);
        }
        catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
        {
            // ok
        }

        foreach (var entry in conn.ListDirectory(srcWire))
        {
            var childSrc = $"{srcWire.TrimEnd('\\')}\\{entry.Name}";
            var childDst = $"{dstWire.TrimEnd('\\')}\\{entry.Name}";
            var childIsDir = (entry.Attributes & XbdmConstants.AttrDirectory) != 0;
            CopyWireItem(conn, childSrc, childDst, childIsDir);
        }
    }

    private static void DeleteWirePathRecursive(XbdmConnection conn, string wirePath, bool isDir)
    {
        if (!isDir)
        {
            conn.Delete(wirePath, isDirectory: false);
            return;
        }

        foreach (var entry in conn.ListDirectory(wirePath))
        {
            var childPath = $"{wirePath.TrimEnd('\\')}\\{entry.Name}";
            var childIsDir = (entry.Attributes & XbdmConstants.AttrDirectory) != 0;
            DeleteWirePathRecursive(conn, childPath, childIsDir);
        }

        conn.Delete(wirePath, isDirectory: true);
    }

    private static void ReceiveWireToLocal(XbdmConnection conn, string wirePath, string localDir, string name, bool isDir)
    {
        var localPath = Path.Combine(localDir, name);
        if (!isDir)
        {
            conn.ReceiveFile(wirePath, localPath);
            return;
        }

        Directory.CreateDirectory(localPath);
        foreach (var entry in conn.ListDirectory(wirePath))
        {
            var childWire = $"{wirePath.TrimEnd('\\')}\\{entry.Name}";
            var childIsDir = (entry.Attributes & XbdmConstants.AttrDirectory) != 0;
            ReceiveWireToLocal(conn, childWire, localPath, entry.Name, childIsDir);
        }
    }

    private static void SendLocalToWire(XbdmConnection conn, string localPath, string wirePath)
    {
        if (File.Exists(localPath))
        {
            conn.SendFile(localPath, wirePath);
            return;
        }

        if (!Directory.Exists(localPath))
            return;

        try
        {
            conn.CreateDirectory(wirePath);
        }
        catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
        {
            // ok
        }

        foreach (var file in Directory.GetFiles(localPath))
        {
            var name = Path.GetFileName(file);
            SendLocalToWire(conn, file, $"{wirePath.TrimEnd('\\')}\\{name}");
        }

        foreach (var dir in Directory.GetDirectories(localPath))
        {
            var name = Path.GetFileName(dir);
            SendLocalToWire(conn, dir, $"{wirePath.TrimEnd('\\')}\\{name}");
        }
    }
}
