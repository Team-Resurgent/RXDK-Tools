namespace Rxdk.XbShellExt.Ui.Forms;

using Rxdk.XbShellExt.Ui;

partial class TransferProgressForm
{
    private Label titleLabel = null!;
    private Label fileLabel = null!;
    private Label filesCaptionLabel = null!;
    private ProgressBar filesProgressBar = null!;
    private Label fileBytesCaptionLabel = null!;
    private ProgressBar fileProgressBar = null!;
    private Button cancelButton = null!;

    private void InitializeComponent()
    {
        titleLabel = new Label();
        fileLabel = new Label();
        filesCaptionLabel = new Label();
        filesProgressBar = new ProgressBar();
        fileBytesCaptionLabel = new Label();
        fileProgressBar = new ProgressBar();
        cancelButton = new Button();
        SuspendLayout();
        // 
        // titleLabel
        // 
        titleLabel.Location = new Point(12, 12);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(396, 18);
        titleLabel.TabIndex = 6;
        // 
        // fileLabel
        // 
        fileLabel.Location = new Point(12, 34);
        fileLabel.Name = "fileLabel";
        fileLabel.Size = new Size(396, 28);
        fileLabel.TabIndex = 5;
        fileLabel.Text = "Preparing…";
        // 
        // filesCaptionLabel
        // 
        filesCaptionLabel.AutoSize = true;
        filesCaptionLabel.Location = new Point(12, 68);
        filesCaptionLabel.Name = "filesCaptionLabel";
        filesCaptionLabel.Size = new Size(44, 15);
        filesCaptionLabel.TabIndex = 4;
        filesCaptionLabel.Text = "Overall";
        // 
        // filesProgressBar
        // 
        filesProgressBar.Location = new Point(12, 86);
        filesProgressBar.Name = "filesProgressBar";
        filesProgressBar.Size = new Size(396, 18);
        filesProgressBar.Style = ProgressBarStyle.Continuous;
        filesProgressBar.TabIndex = 3;
        // 
        // fileBytesCaptionLabel
        // 
        fileBytesCaptionLabel.AutoSize = true;
        fileBytesCaptionLabel.Location = new Point(12, 112);
        fileBytesCaptionLabel.Name = "fileBytesCaptionLabel";
        fileBytesCaptionLabel.Size = new Size(66, 15);
        fileBytesCaptionLabel.TabIndex = 2;
        fileBytesCaptionLabel.Text = "Current file";
        // 
        // fileProgressBar
        // 
        fileProgressBar.Location = new Point(12, 130);
        fileProgressBar.Name = "fileProgressBar";
        fileProgressBar.Size = new Size(396, 18);
        fileProgressBar.Style = ProgressBarStyle.Continuous;
        fileProgressBar.TabIndex = 1;
        // 
        // cancelButton
        // 
        cancelButton.Location = new Point(312, 166);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(96, 36);
        cancelButton.TabIndex = 0;
        cancelButton.Text = "Cancel";
        // 
        // TransferProgressForm
        // 
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 215);
        Controls.Add(cancelButton);
        Controls.Add(fileProgressBar);
        Controls.Add(fileBytesCaptionLabel);
        Controls.Add(filesProgressBar);
        Controls.Add(filesCaptionLabel);
        Controls.Add(fileLabel);
        Controls.Add(titleLabel);
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "TransferProgressForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Xbox Neighborhood";
        TopMost = true;
        ResumeLayout(false);
        PerformLayout();
    }
}
