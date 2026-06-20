using Rxdk.XbShellExt.Ui;
using Rxdk.XbShellExt.Ui.Controls;

namespace Rxdk.XbShellExt.Ui.Forms;

partial class AddConsoleWizardForm
{
    private ShellWizardChrome chrome = null!;
    private Label statusLabel = null!;
    private Panel buttonPanel = null!;
    private Button cancelButton = null!;
    private Button nextButton = null!;
    private Button backButton = null!;

    private void InitializeComponent()
    {
        chrome = new ShellWizardChrome();
        statusLabel = new Label();
        buttonPanel = new Panel();
        backButton = new Button();
        nextButton = new Button();
        cancelButton = new Button();
        buttonPanel.SuspendLayout();
        SuspendLayout();
        // 
        // chrome
        // 
        chrome.Dock = DockStyle.Fill;
        chrome.Location = new Point(0, 0);
        chrome.Name = "chrome";
        chrome.Size = new Size(538, 284);
        chrome.TabIndex = 0;
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.ForeColor = Color.DarkRed;
        statusLabel.Location = new Point(0, 284);
        statusLabel.MaximumSize = new Size(520, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Padding = new Padding(12, 0, 12, 4);
        statusLabel.Size = new Size(24, 29);
        statusLabel.TabIndex = 1;
        // 
        // buttonPanel
        // 
        buttonPanel.Controls.Add(backButton);
        buttonPanel.Controls.Add(nextButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.Location = new Point(0, 313);
        buttonPanel.Name = "buttonPanel";
        buttonPanel.Padding = new Padding(0, 10, 12, 10);
        buttonPanel.Size = new Size(538, 52);
        buttonPanel.TabIndex = 2;
        // 
        // backButton
        // 
        backButton.Location = new Point(122, 8);
        backButton.Name = "backButton";
        backButton.Size = new Size(75, 36);
        backButton.TabIndex = 0;
        backButton.Text = "&Back";
        // 
        // nextButton
        // 
        nextButton.Location = new Point(212, 11);
        nextButton.Name = "nextButton";
        nextButton.Size = new Size(75, 33);
        nextButton.TabIndex = 1;
        nextButton.Text = "&Next";
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(433, 8);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(75, 36);
        cancelButton.TabIndex = 2;
        cancelButton.Text = "Cancel";
        // 
        // AddConsoleWizardForm
        // 
        AutoScaleMode = AutoScaleMode.None;
        CancelButton = cancelButton;
        ClientSize = new Size(538, 365);
        Controls.Add(chrome);
        Controls.Add(statusLabel);
        Controls.Add(buttonPanel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MaximumSize = new Size(560, 421);
        MinimizeBox = false;
        MinimumSize = new Size(560, 421);
        Name = "AddConsoleWizardForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Add New Xbox Development Kit";
        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
