using Rxdk.XbShellExt.Ui;

namespace Rxdk.XbShellExt.Ui.Forms;

partial class AddConsoleWizardForm
{
    private Panel pageHost = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button cancelButton = null!;
    private Button nextButton = null!;
    private Button backButton = null!;

    private void InitializeComponent()
    {
        pageHost = new Panel();
        buttonPanel = new FlowLayoutPanel();
        cancelButton = new Button();
        nextButton = new Button();
        backButton = new Button();
        buttonPanel.SuspendLayout();
        SuspendLayout();
        // 
        // pageHost
        // 
        pageHost.Dock = DockStyle.Fill;
        pageHost.Location = new Point(0, 0);
        pageHost.Name = "pageHost";
        pageHost.Size = new Size(544, 330);
        pageHost.TabIndex = 0;
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
        buttonPanel.Padding = new Padding(8);
        buttonPanel.Size = new Size(544, 52);
        buttonPanel.TabIndex = 1;
        buttonPanel.WrapContents = false;
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(432, 8);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(96, 36);
        cancelButton.TabIndex = 2;
        cancelButton.Text = "Cancel";
        // 
        // nextButton
        // 
        nextButton.Location = new Point(328, 8);
        nextButton.Margin = new Padding(8, 0, 0, 0);
        nextButton.Name = "nextButton";
        nextButton.Size = new Size(96, 36);
        nextButton.TabIndex = 1;
        nextButton.Text = "&Next >";
        // 
        // backButton
        // 
        backButton.Location = new Point(224, 8);
        backButton.Margin = new Padding(8, 0, 0, 0);
        backButton.Name = "backButton";
        backButton.Size = new Size(96, 36);
        backButton.TabIndex = 0;
        backButton.Text = "< &Back";
        // 
        // AddConsoleWizardForm
        // 
        AutoScaleMode = AutoScaleMode.None;
        CancelButton = cancelButton;
        ClientSize = new Size(544, 382);
        Controls.Add(pageHost);
        Controls.Add(buttonPanel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "AddConsoleWizardForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Add New Xbox Development Kit";
        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
