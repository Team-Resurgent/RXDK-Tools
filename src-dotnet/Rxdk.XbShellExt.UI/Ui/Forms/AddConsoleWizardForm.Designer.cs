using Rxdk.XbShellExt.Ui;
using Rxdk.XbShellExt.Ui.Controls;

namespace Rxdk.XbShellExt.Ui.Forms;

partial class AddConsoleWizardForm
{
    private ShellWizardChrome chrome = null!;
    private Label statusLabel = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button cancelButton = null!;
    private Button nextButton = null!;
    private Button backButton = null!;

    private void InitializeComponent()
    {
        chrome = new ShellWizardChrome();
        statusLabel = new Label();
        buttonPanel = new FlowLayoutPanel();
        cancelButton = new Button();
        nextButton = new Button();
        backButton = new Button();
        buttonPanel.SuspendLayout();
        SuspendLayout();
        // 
        // chrome
        // 
        chrome.Dock = DockStyle.Fill;
        chrome.Location = new Point(0, 0);
        chrome.Name = "chrome";
        chrome.Size = new Size(544, 311);
        chrome.TabIndex = 0;
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.ForeColor = Color.DarkRed;
        statusLabel.Location = new Point(0, 311);
        statusLabel.MaximumSize = new Size(520, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Padding = new Padding(12, 0, 12, 4);
        statusLabel.Size = new Size(24, 19);
        statusLabel.TabIndex = 1;
        // 
        // buttonPanel
        // 
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(nextButton);
        buttonPanel.Controls.Add(backButton);
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Location = new Point(0, 330);
        buttonPanel.Margin = new Padding(0);
        buttonPanel.Name = "buttonPanel";
        buttonPanel.Padding = new Padding(0, 10, 12, 10);
        buttonPanel.Size = new Size(544, 52);
        buttonPanel.TabIndex = 2;
        buttonPanel.WrapContents = false;
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(457, 10);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(75, 23);
        cancelButton.TabIndex = 2;
        cancelButton.Text = "Cancel";
        // 
        // nextButton
        // 
        nextButton.Location = new Point(374, 10);
        nextButton.Margin = new Padding(8, 0, 0, 0);
        nextButton.Name = "nextButton";
        nextButton.Size = new Size(75, 23);
        nextButton.TabIndex = 1;
        nextButton.Text = "&Next";
        // 
        // backButton
        // 
        backButton.Location = new Point(291, 10);
        backButton.Margin = new Padding(8, 0, 0, 0);
        backButton.Name = "backButton";
        backButton.Size = new Size(75, 23);
        backButton.TabIndex = 0;
        backButton.Text = "&Back";
        // 
        // AddConsoleWizardForm
        // 
        AutoScaleMode = AutoScaleMode.None;
        CancelButton = cancelButton;
        ClientSize = new Size(544, 382);
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
