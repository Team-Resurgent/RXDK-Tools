using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Ui;

public sealed class ShellFileOperationHost : IFileOperationHost
{
    private readonly nint _ownerHwnd;

    public ShellFileOperationHost(nint ownerHwnd) => _ownerHwnd = ownerHwnd;

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
        using var form = new Form
        {
            Text = "Rename",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(360, 150),
        };
        ShellModernChrome.Apply(form);

        var label = new Label { Text = "Name:", Left = 12, Top = 16, AutoSize = true };
        var textBox = new TextBox { Left = 12, Top = 40, Width = 332, Text = currentName };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var buttonPanel = ShellDialogLayout.CreateButtonBar(cancel, ok);

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(buttonPanel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return Task.FromResult(form.ShowDialog(NativeWindow.FromHandle(_ownerHwnd)) == DialogResult.OK
            ? textBox.Text.Trim()
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
