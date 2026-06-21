namespace Rxdk.XbShellExt.Ui.Controls.Properties;

using Rxdk.XbShellExt.Ui;

partial class ConsoleAdvancedTab
{
    private CheckBox warmRebootCheck = null!;
    private CheckBox restartSameTitleCheck = null!;
    private Button rebootButton = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        warmRebootCheck = new CheckBox();
        restartSameTitleCheck = new CheckBox();
        rebootButton = new Button();
        statusLabel = new Label();
        SuspendLayout();
        // 
        // warmRebootCheck
        // 
        warmRebootCheck.AutoSize = true;
        warmRebootCheck.Checked = true;
        warmRebootCheck.CheckState = CheckState.Checked;
        warmRebootCheck.Location = new Point(0, 4);
        warmRebootCheck.Name = "warmRebootCheck";
        warmRebootCheck.Size = new Size(144, 29);
        warmRebootCheck.TabIndex = 3;
        warmRebootCheck.Text = "Warm reboot";
        // 
        // restartSameTitleCheck
        // 
        restartSameTitleCheck.AutoSize = true;
        restartSameTitleCheck.Location = new Point(0, 28);
        restartSameTitleCheck.Name = "restartSameTitleCheck";
        restartSameTitleCheck.Size = new Size(173, 29);
        restartSameTitleCheck.TabIndex = 2;
        restartSameTitleCheck.Text = "Restart same title";
        // 
        // rebootButton
        // 
        rebootButton.AutoSize = false;
        rebootButton.Location = new Point(0, 54);
        rebootButton.Name = "rebootButton";
        rebootButton.Size = ShellDialogLayout.ButtonSize;
        rebootButton.TabIndex = 1;
        rebootButton.Text = "Reboot";
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Location = new Point(0, 88);
        statusLabel.MaximumSize = new Size(380, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(0, 25);
        statusLabel.TabIndex = 0;
        // 
        // ConsoleAdvancedTab
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        AutoSize = true;
        Controls.Add(statusLabel);
        Controls.Add(rebootButton);
        Controls.Add(restartSameTitleCheck);
        Controls.Add(warmRebootCheck);
        Name = "ConsoleAdvancedTab";
        Padding = new Padding(0, 4, 0, 0);
        Size = new Size(3287, 113);
        ResumeLayout(false);
        PerformLayout();
    }
}
