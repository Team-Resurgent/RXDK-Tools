namespace Rxdk.XbShellExt.Ui.Forms;

partial class TransferProgressForm
{
    private Label titleLabel = null!;
    private Label fileLabel = null!;
    private ProgressBar progressBar = null!;
    private Button cancelButton = null!;

    private void InitializeComponent()
    {
        titleLabel = new Label();
        fileLabel = new Label();
        progressBar = new ProgressBar();
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
        ClientSize = new Size(420, 118);
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 9F);

        titleLabel.AutoSize = false;
        titleLabel.Location = new Point(12, 12);
        titleLabel.Size = new Size(396, 18);

        fileLabel.AutoSize = false;
        fileLabel.Location = new Point(12, 34);
        fileLabel.Size = new Size(396, 32);
        fileLabel.Text = "Preparing…";

        progressBar.Location = new Point(12, 72);
        progressBar.Size = new Size(320, 22);
        progressBar.Style = ProgressBarStyle.Continuous;

        cancelButton.Location = new Point(338, 70);
        cancelButton.Size = new Size(70, 26);
        cancelButton.Text = "Cancel";

        Controls.Add(cancelButton);
        Controls.Add(progressBar);
        Controls.Add(fileLabel);
        Controls.Add(titleLabel);
        ResumeLayout(false);
    }
}
