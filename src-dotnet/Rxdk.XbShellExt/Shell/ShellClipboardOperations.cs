using Rxdk.XbShellExt.Com;
using Rxdk.XbShellExt.Interop;
using OleIDataObject = Rxdk.XbShellExt.Interop.IDataObject;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Shell;

internal static class ShellClipboardOperations
{
    public static void SetCutCopy(FileSelection selection, bool cut)
    {
        if (cut)
            SharedClipboard.Cut(selection);
        else
            SharedClipboard.Copy(selection);

        OleApartment.EnsureInitialized();

        var effect = cut ? OleConstants.DropeffectMove : OleConstants.DropeffectCopy;
        var operation = cut ? FileClipboardOperation.Cut : FileClipboardOperation.Copy;
        var dataObject = new XboxClipboardDataObject(selection, operation, effect);
        var hr = OleSetClipboard(dataObject);
        if (hr < 0)
            throw new InvalidOperationException($"Could not place selection on the clipboard (0x{hr:X8}).");
    }

    public static bool CanPaste()
    {
        if (SharedClipboard.HasItems)
            return true;

        OleApartment.EnsureInitialized();
        var hr = OleGetClipboard(out var dataObject);
        if (hr < 0 || dataObject == null)
            return false;

        try
        {
            return OleDataTransfer.SupportsPasteFormat(dataObject);
        }
        finally
        {
            Marshal.ReleaseComObject(dataObject);
        }
    }

    public static bool TryPaste(string folderPath, nint childPidl, IFileOperationHost host)
    {
        var targetFolder = ShellSelectionBuilder.ResolvePasteTargetFolder(folderPath, childPidl);
        var consoleName = WirePathService.GetConsoleNameFromDisplayPath(targetFolder);

        if (!EnsureClipboardLoaded())
            return false;

        if (SharedClipboard.HasItems)
        {
            SharedFileOps.Paste(consoleName, targetFolder, host);
            return true;
        }

        if (TryPasteExternal(consoleName, targetFolder, host))
            return true;

        return false;
    }

    private static bool EnsureClipboardLoaded()
    {
        if (SharedClipboard.HasItems)
            return true;

        OleApartment.EnsureInitialized();
        var hr = OleGetClipboard(out var dataObject);
        if (hr < 0 || dataObject == null)
            return false;

        try
        {
            if (XboxClipboardPayload.TryReadFromDataObject(dataObject, out var selection, out var operation) &&
                selection != null)
            {
                if (operation == FileClipboardOperation.Cut)
                    SharedClipboard.Cut(selection);
                else
                    SharedClipboard.Copy(selection);
                return true;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dataObject);
        }

        return true;
    }

    private static bool TryPasteExternal(string consoleName, string targetFolder, IFileOperationHost host)
    {
        OleApartment.EnsureInitialized();

        var hr = OleGetClipboard(out var dataObject);
        if (hr < 0 || dataObject == null)
            return false;

        try
        {
            if (!OleDataTransfer.SupportsFormat(dataObject, ClipboardFormats.HDrop))
                return false;

            var paths = OleDataTransfer.ReadHdropPaths(dataObject);
            if (paths.Count == 0)
                return false;

            SharedFileOps.UploadFromPc(consoleName, targetFolder, paths);
            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(dataObject);
        }
    }

    internal static readonly FileClipboardService SharedClipboard = new();
    internal static readonly FileOperationsService SharedFileOps = new(SharedClipboard);

    [DllImport("ole32.dll")]
    private static extern int OleSetClipboard(OleIDataObject pDataObj);

    [DllImport("ole32.dll")]
    private static extern int OleGetClipboard(out OleIDataObject? ppDataObj);
}
