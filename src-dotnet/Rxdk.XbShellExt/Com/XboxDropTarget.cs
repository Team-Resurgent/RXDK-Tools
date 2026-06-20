using System.Runtime.InteropServices.ComTypes;
using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Shell;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;
using OleIDataObject = Rxdk.XbShellExt.Interop.IDataObject;
using OleIDropTarget = Rxdk.XbShellExt.Interop.IDropTarget;

namespace Rxdk.XbShellExt.Com;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class XboxDropTarget : OleIDropTarget
{
    private readonly string _folderPath;
    private readonly nint _childPidl;
    private readonly uint _cidl;
    private readonly nint _apidl;
    private readonly string _targetFolderPath;
    private readonly string _consoleName;
    private readonly FileOperationsService _fileOps = new(new FileClipboardService());

    public XboxDropTarget(string folderPath, uint cidl, nint apidl)
    {
        _folderPath = folderPath;
        _childPidl = 0;
        _cidl = cidl;
        _apidl = apidl;
        _targetFolderPath = ShellSelectionBuilder.ResolveDropTargetFolder(folderPath, cidl, apidl) ?? folderPath;
        _consoleName = WirePathService.GetConsoleNameFromDisplayPath(_targetFolderPath);
    }

    public XboxDropTarget(string folderPath, nint childPidl)
    {
        _folderPath = folderPath;
        _childPidl = childPidl;
        _cidl = childPidl == 0 ? 0u : 1u;
        _apidl = 0;
        _targetFolderPath = ShellSelectionBuilder.ResolveDropTargetFolder(folderPath, childPidl) ?? folderPath;
        _consoleName = WirePathService.GetConsoleNameFromDisplayPath(_targetFolderPath);
    }

    public int DragEnter(OleIDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        if (!OleDataTransfer.SupportsPasteFormat(pDataObj))
        {
            pdwEffect = OleConstants.DropeffectNone;
            return HResults.Ok;
        }

        ReadPreferredEffect(pDataObj);
        pdwEffect = OleDataTransfer.ChooseDropEffect(grfKeyState, OleConstants.DropeffectCopy | OleConstants.DropeffectMove);
        if (pdwEffect == OleConstants.DropeffectNone)
            pdwEffect = OleConstants.DropeffectCopy;
        return HResults.Ok;
    }

    public int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        pdwEffect = OleDataTransfer.ChooseDropEffect(grfKeyState, OleConstants.DropeffectCopy | OleConstants.DropeffectMove);
        if (pdwEffect == OleConstants.DropeffectNone)
            pdwEffect = OleConstants.DropeffectCopy;
        return HResults.Ok;
    }

    public int DragLeave() => HResults.Ok;

    public int Drop(OleIDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        pdwEffect = OleConstants.DropeffectNone;
        if (!SupportsTarget())
            return HResults.Ok;

        try
        {
            if (XboxClipboardPayload.TryReadFromDataObject(pDataObj, out var selection, out var operation) &&
                selection != null)
            {
                var targetFolder = _targetFolderPath;
                var consoleName = _consoleName;
                if (operation == FileClipboardOperation.Cut)
                    _fileOps.Clipboard.Cut(selection);
                else
                    _fileOps.Clipboard.Copy(selection);

                _fileOps.Paste(consoleName, targetFolder, NullFileOperationHost.Instance);
                pdwEffect = operation == FileClipboardOperation.Cut
                    ? OleConstants.DropeffectMove
                    : OleConstants.DropeffectCopy;
                NotifyFolderChanged();
                return HResults.Ok;
            }

            var paths = OleDataTransfer.ReadHdropPaths(pDataObj);
            if (paths.Count == 0)
                return HResults.Ok;

            ManagedTrace.Line($"XboxDropTarget.Drop upload console='{_consoleName}' target='{_targetFolderPath}' paths={paths.Count}");
            _fileOps.UploadFromPc(_consoleName, _targetFolderPath, paths);
            pdwEffect = OleDataTransfer.ChooseDropEffect(grfKeyState, OleConstants.DropeffectCopy | OleConstants.DropeffectMove);
            if (pdwEffect == OleConstants.DropeffectNone)
                pdwEffect = OleConstants.DropeffectCopy;

            NotifyFolderChanged();
            return HResults.Ok;
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"XboxDropTarget.Drop failed target='{_targetFolderPath}': {ex}");
            MessageBox.Show(ex.Message, "Xbox Neighborhood", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return HResults.Fail;
        }
    }

    private sealed class NullFileOperationHost : IFileOperationHost
    {
        public static readonly NullFileOperationHost Instance = new();

        public Task<bool> ConfirmDeleteAsync(IReadOnlyList<FileSelectionItem> items) =>
            Task.FromResult(true);

        public Task<string?> PromptRenameAsync(string currentName) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickLocalFolderAsync(string title) =>
            Task.FromResult<string?>(null);

        public void ShowError(string message) =>
            MessageBox.Show(message, "Xbox Neighborhood", MessageBoxButtons.OK, MessageBoxIcon.Error);

        public void ShowInfo(string message) { }
    }

    private static void ReadPreferredEffect(OleIDataObject dataObject)
    {
        var format = OleDataTransfer.CreateFormat(ClipboardFormats.PreferredDropEffect);
        if (dataObject.GetData(ref format, out var medium) != HResults.Ok || medium.unionmember == IntPtr.Zero)
            return;

        ReleaseStgMedium(ref medium);
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    private bool SupportsTarget() =>
        _childPidl != 0
            ? ShellSelectionBuilder.SupportsDropTarget(_folderPath, _childPidl)
            : ShellSelectionBuilder.SupportsDropTarget(_folderPath, _cidl, _apidl);

    private void NotifyFolderChanged()
    {
        // NativeDropTarget notifies with the correct folder pidl after PerformDrop.
        // Keep a fallback for any managed-only drop paths.
        ShellFolderNotify.RefreshDisplayFolder(_targetFolderPath);
    }
}
