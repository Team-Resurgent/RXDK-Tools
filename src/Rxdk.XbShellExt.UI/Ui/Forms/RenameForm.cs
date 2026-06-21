namespace Rxdk.XbShellExt.Ui.Forms;

public sealed partial class RenameForm : ShellDialogForm
{
    public RenameForm()
    {
        InitializeComponent();
        ApplyRuntimeChrome();
        DesignPreview.ApplyIfDesignTime(() =>
        {
            nameLabel.Text = "Name:";
            nameTextBox.Text = "Example.xbe";
        });
    }

    public RenameForm(string currentName)
        : this()
    {
        nameTextBox.Text = currentName ?? string.Empty;
        Shown += (_, _) =>
        {
            nameTextBox.SelectAll();
            nameTextBox.Focus();
        };
    }

    public string NewName => nameTextBox.Text.Trim();
}
