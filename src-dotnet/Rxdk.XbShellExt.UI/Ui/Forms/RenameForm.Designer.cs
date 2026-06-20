#nullable enable
namespace Rxdk.XbShellExt.Ui.Forms;

partial class RenameForm
{
    private System.ComponentModel.IContainer? components = null;
    private Label nameLabel = null!;
    private TextBox nameTextBox = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        nameLabel = new Label();
        nameTextBox = new TextBox();
        buttonPanel = new FlowLayoutPanel();
        okButton = new Button();
        cancelButton = new Button();
        buttonPanel.SuspendLayout();
        SuspendLayout();

        Text = "Rename";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 120);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        nameLabel.AutoSize = true;
        nameLabel.Location = new Point(12, 16);
        nameLabel.Text = "Name:";

        nameTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        nameTextBox.Location = new Point(12, 40);
        nameTextBox.Size = new Size(336, 23);

        buttonPanel.AutoSize = false;
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Height = ShellDialogLayout.ButtonBarHeight;
        buttonPanel.Margin = Padding.Empty;
        buttonPanel.Padding = new Padding(0, ShellDialogLayout.ButtonBarPaddingTop, ShellDialogLayout.ButtonBarPaddingRight, ShellDialogLayout.ButtonBarPaddingBottom);
        buttonPanel.WrapContents = false;

        okButton.AutoSize = false;
        okButton.DialogResult = DialogResult.OK;
        okButton.Margin = new Padding(ShellDialogLayout.ButtonSpacing, 0, 0, 0);
        okButton.Size = ShellDialogLayout.ButtonSize;
        okButton.Text = "OK";

        cancelButton.AutoSize = false;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Margin = new Padding(ShellDialogLayout.ButtonSpacing, 0, 0, 0);
        cancelButton.Size = ShellDialogLayout.ButtonSize;
        cancelButton.Text = "Cancel";

        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        Controls.Add(nameLabel);
        Controls.Add(nameTextBox);
        Controls.Add(buttonPanel);

        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
