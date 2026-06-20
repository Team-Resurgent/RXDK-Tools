#nullable enable
namespace Rxdk.XbShellExt.Ui.Forms;

partial class ConfirmAttributeChangesForm
{
    private System.ComponentModel.IContainer? components = null;
    private Label introLabel = null!;
    private TextBox changesTextBox = null!;
    private Label questionLabel = null!;
    private RadioButton folderOnlyRadio = null!;
    private RadioButton recursiveRadio = null!;
    private Panel buttonPanel = null!;
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
        introLabel = new Label();
        changesTextBox = new TextBox();
        questionLabel = new Label();
        folderOnlyRadio = new RadioButton();
        recursiveRadio = new RadioButton();
        buttonPanel = new Panel();
        okButton = new Button();
        cancelButton = new Button();
        buttonPanel.SuspendLayout();
        SuspendLayout();

        Text = "Confirm Attribute Changes";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 280);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        introLabel.AutoSize = false;
        introLabel.Location = new Point(12, 12);
        introLabel.Size = new Size(376, 32);
        introLabel.Text = "You have chosen to make the following attribute change(s):";

        changesTextBox.BackColor = SystemColors.Control;
        changesTextBox.BorderStyle = BorderStyle.None;
        changesTextBox.Location = new Point(36, 48);
        changesTextBox.Multiline = true;
        changesTextBox.ReadOnly = true;
        changesTextBox.Size = new Size(340, 40);

        questionLabel.AutoSize = false;
        questionLabel.Location = new Point(12, 96);
        questionLabel.Size = new Size(376, 48);

        folderOnlyRadio.Checked = true;
        folderOnlyRadio.Location = new Point(36, 150);
        folderOnlyRadio.Size = new Size(340, 24);

        recursiveRadio.Location = new Point(36, 178);
        recursiveRadio.Size = new Size(340, 24);

        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.Height = ShellDialogLayout.ButtonBarHeight;
        buttonPanel.Padding = new Padding(0, ShellDialogLayout.ButtonBarPaddingTop, ShellDialogLayout.ButtonBarPaddingRight, ShellDialogLayout.ButtonBarPaddingBottom);

        okButton.DialogResult = DialogResult.OK;
        okButton.Text = "OK";

        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Text = "Cancel";

        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        Controls.Add(introLabel);
        Controls.Add(changesTextBox);
        Controls.Add(questionLabel);
        Controls.Add(folderOnlyRadio);
        Controls.Add(recursiveRadio);
        Controls.Add(buttonPanel);

        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
