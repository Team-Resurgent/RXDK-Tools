using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Ui;

public sealed class ShellFileOperationHost : IFileOperationHost
{
    private readonly nint _ownerHwnd;

    public ShellFileOperationHost(nint ownerHwnd) => _ownerHwnd = ownerHwnd;

    public Task<bool> ConfirmDeleteAsync(IReadOnlyList<FileSelectionItem> items)
    {
        var names = string.Join(Environment.NewLine, items.Select(i => i.Name));
        var message = items.Count == 1
            ? $"Delete '{items[0].Name}'?"
            : $"Delete these {items.Count} items?{Environment.NewLine}{Environment.NewLine}{names}";

        var result = MessageBox.Show(
            NativeWindow.FromHandle(_ownerHwnd),
            message,
            "Xbox Neighborhood",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        return Task.FromResult(result == DialogResult.Yes);
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
