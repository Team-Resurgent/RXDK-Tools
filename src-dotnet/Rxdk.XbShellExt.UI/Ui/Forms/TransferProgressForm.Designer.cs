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

        Text = "Xbox Neighborhood";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(420, 158);
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 9F);

        titleLabel.AutoSize = false;
        titleLabel.Location = new Point(12, 12);
        titleLabel.Size = new Size(396, 18);

        fileLabel.AutoSize = false;
        fileLabel.Location = new Point(12, 34);
        fileLabel.Size = new Size(396, 28);
        fileLabel.Text = "Preparing…";

        filesCaptionLabel.AutoSize = true;
        filesCaptionLabel.Location = new Point(12, 68);
        filesCaptionLabel.Text = "Overall";

        filesProgressBar.Location = new Point(12, 86);
        filesProgressBar.Size = new Size(396, 18);
        filesProgressBar.Style = ProgressBarStyle.Continuous;

        fileBytesCaptionLabel.AutoSize = true;
        fileBytesCaptionLabel.Location = new Point(12, 112);
        fileBytesCaptionLabel.Text = "Current file";

        fileProgressBar.Location = new Point(12, 130);
        fileProgressBar.Size = new Size(320, 18);
        fileProgressBar.Style = ProgressBarStyle.Continuous;

        cancelButton.AutoSize = false;
        cancelButton.Location = new Point(338, 126);
        cancelButton.Size = ShellDialogLayout.ButtonSize;
        cancelButton.Text = "Cancel";

        Controls.Add(cancelButton);
        Controls.Add(fileProgressBar);
        Controls.Add(fileBytesCaptionLabel);
        Controls.Add(filesProgressBar);
        Controls.Add(filesCaptionLabel);
        Controls.Add(fileLabel);
        Controls.Add(titleLabel);
        ResumeLayout(false);
    }
}
