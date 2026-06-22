using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.XbShellExt.Ui.Forms;

namespace Rxdk.XbShellExt.Ui;

public sealed class ShellFileOperationHost : IFileOperationHost
{
    private readonly nint _ownerHwnd;

    public ShellFileOperationHost(nint ownerHwnd) => _ownerHwnd = ownerHwnd;

    public bool ConfirmRemoveConsole(string consoleName, bool isDefault)
    {
        var message = isDefault
            ? $"You are attempting to delete your default Xbox console. Many Xbox tools will not function without a default Xbox console.{Environment.NewLine}{Environment.NewLine}Are you sure you want to delete '{consoleName}'?"
            : $"Are you sure that you want to remove '{consoleName}' from Xbox Neighborhood?";

        var result = MessageBox.Show(
            NativeWindow.FromHandle(_ownerHwnd),
            message,
            "Remove Xbox Console",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        return result == DialogResult.Yes;
    }

    public Task<bool> ConfirmDeleteAsync(IReadOnlyList<FileSelectionItem> items)
    {
        var message = FormatDeleteMessage(items);
        var caption = items.Count == 1 && items[0].IsDirectory
            ? "Confirm Folder Delete"
            : items.Count > 1
                ? "Confirm Multiple Delete"
                : "Confirm Delete";

        var result = MessageBox.Show(
            NativeWindow.FromHandle(_ownerHwnd),
            message,
            caption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        return Task.FromResult(result == DialogResult.Yes);
    }

    private static string FormatDeleteMessage(IReadOnlyList<FileSelectionItem> items)
    {
        if (items.Count == 0)
            return "Are you sure you want to permanently delete this item?";

        if (items.Count == 1)
        {
            var name = items[0].Name;
            return items[0].IsDirectory
                ? $"Are you sure you want to remove the folder '{name}' and permanently delete its contents?"
                : $"Are you sure you want to permanently delete '{name}'?";
        }

        return $"Are you sure you want to permanently delete these '{items.Count}' items?";
    }

    public Task<string?> PromptRenameAsync(string currentName)
    {
        using var form = new RenameForm(currentName);
        form.SetShellOwner(_ownerHwnd);
        form.EnsureShellDpiScaled();
        return Task.FromResult(
            form.ShowDialog(NativeWindow.FromHandle(_ownerHwnd)) == DialogResult.OK
                ? form.NewName
                : null);
    }

    public Task<string?> PickLocalFolderAsync(string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
        };

        return Task.FromResult(dialog.ShowDialog(NativeWindow.FromHandle(_ownerHwnd)) == DialogResult.OK
            ? dialog.SelectedPath
            : null);
    }

    public void ShowError(string message) =>
        MessageBox.Show(
            NativeWindow.FromHandle(_ownerHwnd),
            message,
            "Xbox Neighborhood",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

    public void ShowInfo(string message) =>
        MessageBox.Show(
            NativeWindow.FromHandle(_ownerHwnd),
            message,
            "Xbox Neighborhood",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
}
