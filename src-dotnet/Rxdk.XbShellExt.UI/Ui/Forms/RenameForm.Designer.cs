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
        nameLabel = new Label();
        nameTextBox = new TextBox();
        buttonPanel = new FlowLayoutPanel();
        cancelButton = new Button();
        okButton = new Button();
        buttonPanel.SuspendLayout();
        SuspendLayout();
        // 
        // nameLabel
        // 
        nameLabel.AutoSize = true;
        nameLabel.Location = new Point(12, 16);
        nameLabel.Name = "nameLabel";
        nameLabel.Size = new Size(42, 15);
        nameLabel.TabIndex = 0;
        nameLabel.Text = "Name:";
        // 
        // nameTextBox
        // 
        nameTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        nameTextBox.Location = new Point(12, 40);
        nameTextBox.Name = "nameTextBox";
        nameTextBox.Size = new Size(336, 23);
        nameTextBox.TabIndex = 1;
        // 
        // buttonPanel
        // 
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Location = new Point(0, 68);
        buttonPanel.Margin = new Padding(0);
        buttonPanel.Name = "buttonPanel";
        buttonPanel.Padding = new Padding(8);
        buttonPanel.Size = new Size(360, 52);
        buttonPanel.TabIndex = 2;
        buttonPanel.WrapContents = false;
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(248, 8);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(96, 36);
        cancelButton.TabIndex = 0;
        cancelButton.Text = "Cancel";
        // 
        // okButton
        // 
        okButton.DialogResult = DialogResult.OK;
        okButton.Location = new Point(144, 8);
        okButton.Margin = new Padding(8, 0, 0, 0);
        okButton.Name = "okButton";
        okButton.Size = new Size(96, 36);
        okButton.TabIndex = 1;
        okButton.Text = "OK";
        // 
        // RenameForm
        // 
        AcceptButton = okButton;
        CancelButton = cancelButton;
        ClientSize = new Size(360, 120);
        Controls.Add(nameLabel);
        Controls.Add(nameTextBox);
        Controls.Add(buttonPanel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "RenameForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Rename";
        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
