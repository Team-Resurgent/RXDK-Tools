using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Ui;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.KitServices.Stores;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbShellExt.Shell;

internal static class ShellCommandService
{
    private static FileClipboardService Clipboard => ShellClipboardOperations.SharedClipboard;
    private static FileOperationsService FileOps => ShellClipboardOperations.SharedFileOps;

    public static void Execute(nint hwnd, string folderPath, nint folderPidl, nint childPidl, ShellContextCommand command) =>
        Execute(
            hwnd,
            folderPath,
            folderPidl,
            childPidl == 0 ? Array.Empty<nint>() : new[] { childPidl },
            command);

    public static void Execute(nint hwnd, string folderPath, nint folderPidl, IReadOnlyList<nint> childPidls, ShellContextCommand command)
    {
        ManagedTrace.Line($"ShellCommandService.Execute folder='{folderPath}' command={command} count={childPidls.Count}");
        try
        {
            var primaryChild = childPidls.Count > 0 ? childPidls[0] : 0;
            var selectionPath = ShellSelectionBuilder.GetSelectionPath(folderPath, primaryChild);
            var host = new ShellFileOperationHost(hwnd);

            FileSelection? deletedSelection = null;
            switch (command)
            {
                case ShellContextCommand.Cut:
                    ExecuteCut(folderPath, childPidls, host);
                    break;
                case ShellContextCommand.Copy:
                    ExecuteCopy(folderPath, childPidls, host);
                    break;
                case ShellContextCommand.Paste:
                    ExecutePaste(folderPath, primaryChild, host);
                    break;
                case ShellContextCommand.Delete:
                    deletedSelection = ExecuteDelete(folderPath, childPidls, host);
                    break;
                case ShellContextCommand.Rename:
                    ExecuteRename(folderPath, childPidls, host);
                    break;
                case ShellContextCommand.NewFolder:
                    ExecuteNewFolder(folderPath, host);
                    break;
                case ShellContextCommand.Launch:
                    ExecuteLaunch(folderPath, primaryChild, host);
                    break;
                case ShellContextCommand.SetDefaultConsole:
                    ExecuteSetDefault(selectionPath ?? folderPath, host);
                    break;
                case ShellContextCommand.RemoveConsole:
                    ExecuteRemoveConsole(selectionPath ?? folderPath, host);
                    break;
                case ShellContextCommand.RebootWarm:
                    ExecuteReboot(selectionPath ?? folderPath, cold: false, sameTitle: false, host);
                    break;
                case ShellContextCommand.RebootSameTitle:
                    ExecuteReboot(selectionPath ?? folderPath, cold: false, sameTitle: true, host);
                    break;
                case ShellContextCommand.RebootCold:
                    ExecuteReboot(selectionPath ?? folderPath, cold: true, sameTitle: false, host);
                    break;
                case ShellContextCommand.CaptureScreenshot:
                    ExecuteCapture(selectionPath ?? folderPath, hwnd, host);
                    break;
                case ShellContextCommand.SynchronizeTime:
                    ExecuteSynchronizeTime(selectionPath ?? folderPath, host);
                    break;
            }

            if (deletedSelection != null)
                NotifyDeletedItems(folderPidl, deletedSelection);
            else if (ShouldRefreshFolder(command))
                NotifyFolderRefresh(folderPidl, folderPath);
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"ShellCommandService.Execute threw {ex.GetType().Name}: {ex.Message}");
            ShellUiHost.ShowError(hwnd, ex.Message);
        }
    }

    private static bool ShouldRefreshFolder(ShellContextCommand command) => command switch
    {
        ShellContextCommand.Paste => true,
        ShellContextCommand.Rename => true,
        ShellContextCommand.NewFolder => true,
        _ => false,
    };

    private static void ExecuteCut(string folderPath, IReadOnlyList<nint> childPidls, ShellFileOperationHost host)
    {
        var selection = ShellSelectionBuilder.BuildFileSelection(folderPath, childPidls);
        if (selection == null)
            return;

        ShellClipboardOperations.SetCutCopy(selection, cut: true);
    }

    private static void ExecuteCopy(string folderPath, IReadOnlyList<nint> childPidls, ShellFileOperationHost host)
    {
        var selection = ShellSelectionBuilder.BuildFileSelection(folderPath, childPidls);
        if (selection == null)
            return;

        ShellClipboardOperations.SetCutCopy(selection, cut: false);
    }

    private static void ExecutePaste(string folderPath, nint childPidl, ShellFileOperationHost host)
    {
        if (!ShellClipboardOperations.TryPaste(folderPath, childPidl, host))
            host.ShowError("There is nothing to paste.");
    }

    private static FileSelection? ExecuteDelete(string folderPath, IReadOnlyList<nint> childPidls, ShellFileOperationHost host)
    {
        var selection = ShellSelectionBuilder.BuildFileSelection(folderPath, childPidls);
        if (selection == null)
            return null;

        if (!FileOps.DeleteAsync(selection, host).GetAwaiter().GetResult())
            return null;

        return selection;
    }

    private static void ExecuteRename(string folderPath, IReadOnlyList<nint> childPidls, ShellFileOperationHost host)
    {
        var selection = ShellSelectionBuilder.BuildFileSelection(folderPath, childPidls);
        if (selection == null || selection.Items.Count != 1)
        {
            host.ShowInfo("Select exactly one item to rename.");
            return;
        }

        var currentName = selection.Items[0].Name.TrimEnd(':');
        var newName = host.PromptRenameAsync(currentName).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
            return;

        FileOps.Rename(selection, newName);
    }

    private static void ExecuteNewFolder(string folderPath, ShellFileOperationHost host)
    {
        var consoleName = WirePathService.GetConsoleNameFromDisplayPath(folderPath);
        FileOps.CreateNewFolder(consoleName, folderPath);
    }

    private static void ExecuteLaunch(string folderPath, nint childPidl, ShellFileOperationHost host)
    {
        var selection = ShellSelectionBuilder.BuildFileSelection(folderPath, childPidl);
        if (selection == null)
            return;

        FileOps.LaunchXbe(selection);
        host.ShowInfo("Launch command sent.");
    }

    private static void ExecuteSetDefault(string consolePath, ShellFileOperationHost host)
    {
        if (string.IsNullOrWhiteSpace(consolePath) || consolePath.Contains('\\'))
            throw new InvalidOperationException("Select a console to set as default.");

        new ShellExtensionConsoleStore().SetDefaultConsole(consolePath);
        host.ShowInfo($"'{consolePath}' is now the default console.");
    }

    private static void ExecuteRemoveConsole(string consolePath, ShellFileOperationHost host)
    {
        if (string.IsNullOrWhiteSpace(consolePath) || consolePath.Contains('\\'))
            throw new InvalidOperationException("Select a console to remove.");

        new ShellExtensionConsoleStore().RemoveConsole(consolePath);
        host.ShowInfo($"Removed console '{consolePath}'.");
    }

    private static void ExecuteReboot(string consolePath, bool cold, bool sameTitle, ShellFileOperationHost host)
    {
        if (string.IsNullOrWhiteSpace(consolePath) || consolePath.Contains('\\'))
            throw new InvalidOperationException("Select a console to reboot.");

        string? launch = null;
        if (sameTitle)
        {
            using var conn = XbdmSession.Connect(consolePath);
            launch = conn.GetXbeLaunchPath();
        }

        FileOps.Reboot(consolePath, cold, launch);
        host.ShowInfo("Reboot command sent.");
    }

    private static void ExecuteCapture(string consolePath, nint hwnd, ShellFileOperationHost host)
    {
        if (string.IsNullOrWhiteSpace(consolePath) || consolePath.Contains('\\'))
            throw new InvalidOperationException("Select a console to capture.");

        using var dialog = new SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "Bitmap (*.bmp)|*.bmp",
            FileName = $"{consolePath}-screenshot.bmp",
            DefaultExt = "bmp",
        };

        if (dialog.ShowDialog(System.Windows.Forms.NativeWindow.FromHandle(hwnd)) != DialogResult.OK)
            return;

        using var conn = XbdmSession.Connect(consolePath);
        conn.CaptureScreenshot(dialog.FileName);
        host.ShowInfo("Screenshot saved.");
    }

    private static void ExecuteSynchronizeTime(string consolePath, ShellFileOperationHost host)
    {
        if (string.IsNullOrWhiteSpace(consolePath) || consolePath.Contains('\\'))
            throw new InvalidOperationException("Select a console to synchronize time.");

        using var conn = XbdmSession.Connect(consolePath);
        conn.SyncConsoleClock();
        host.ShowInfo("Console time synchronized.");
    }

    private static void NotifyFolderRefresh(nint folderPidl, string folderPath)
    {
        if (folderPidl != 0)
            ShellFolderNotify.RefreshFolder(folderPidl);
        else
            ShellFolderNotify.RefreshDisplayFolder(folderPath);
    }

    private static void NotifyDeletedItems(nint folderPidl, FileSelection selection)
    {
        foreach (var item in selection.Items)
        {
            nint absolute = 0;
            nint relative = 0;
            try
            {
                relative = PidlHelper.CreateSimple(item.Name.TrimEnd(':'));
                if (folderPidl != 0)
                {
                    absolute = PidlHelper.Concatenate(folderPidl, relative);
                }
                else
                {
                    var itemDisplayPath = string.IsNullOrEmpty(selection.FolderDisplayPath)
                        ? item.Name.TrimEnd(':')
                        : WirePathService.GetItemDisplayPath(selection.FolderDisplayPath, item.Name);
                    absolute = PidlHelper.BuildNamespaceRelativePidl(itemDisplayPath);
                }

                if (absolute != 0)
                    ShellFolderNotify.NotifyItemRemoved(absolute, item.IsDirectory);
            }
            finally
            {
                if (absolute != 0)
                    PidlHelper.Free(absolute);
                if (relative != 0)
                    PidlHelper.Free(relative);
            }
        }
    }
}
