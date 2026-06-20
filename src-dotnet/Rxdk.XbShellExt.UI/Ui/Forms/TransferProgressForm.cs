namespace Rxdk.XbShellExt.Ui.Forms;

using System.ComponentModel;
using Rxdk.XbShellExt.Ui;

public sealed partial class TransferProgressForm : Form
{
    private bool _cancelRequested;
    private bool _failedMode;

    public TransferProgressForm()
    {
        InitializeComponent();
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            ShellModernChrome.Apply(this);
        cancelButton.Click += (_, _) =>
        {
            if (_failedMode)
                Close();
            else
                _cancelRequested = true;
        };
        ShellDialogLayout.ConfigureButton(cancelButton);
        DesignPreview.ApplyIfDesignTime(() =>
        {
            titleLabel.Text = "Copying from Xbox";
            fileLabel.Text = @"C:\dashboard.xbx";
            filesCaptionLabel.Text = "Overall (2 of 8 files)";
            filesProgressBar.Maximum = 100;
            filesProgressBar.Value = 25;
            fileBytesCaptionLabel.Text = "Current file";
            fileProgressBar.Maximum = 100;
            fileProgressBar.Value = 60;
        });
    }

    public TransferProgressForm(string title)
        : this()
    {
        titleLabel.Text = title;
    }

    public bool IsCancelRequested => _cancelRequested;

    public void Configure(int fileCount)
    {
        filesProgressBar.Style = ProgressBarStyle.Continuous;
        filesProgressBar.Minimum = 0;
        filesProgressBar.Maximum = 100;
        filesProgressBar.Value = 0;

        fileProgressBar.Style = ProgressBarStyle.Continuous;
        fileProgressBar.Minimum = 0;
        fileProgressBar.Maximum = 100;
        fileProgressBar.Value = 0;

        filesCaptionLabel.Text = fileCount == 1
            ? "Overall (1 file)"
            : $"Overall (0 of {fileCount} files)";

        fileBytesCaptionLabel.Text = "Current file";
        fileLabel.Text = fileCount == 1
            ? "Copying 1 item…"
            : $"Copying {fileCount} items…";
    }

    public void Complete()
    {
        filesProgressBar.Value = 100;
        fileProgressBar.Value = 100;
        PumpMessages();
    }

    public void Fail(string message)
    {
        _failedMode = true;
        Text = "Xbox Neighborhood";
        titleLabel.Text = "Transfer failed";
        filesCaptionLabel.Visible = false;
        filesProgressBar.Visible = false;
        fileBytesCaptionLabel.Visible = false;
        fileProgressBar.Visible = false;
        fileLabel.Location = new Point(12, 34);
        fileLabel.Size = new Size(396, 96);
        fileLabel.Text = message;
        cancelButton.Text = "Close";
        cancelButton.Enabled = true;
        ClientSize = new Size(420, 148);
        BringToFront();
        Activate();
        PumpMessages();
    }

    public void SetCurrentFile(string relativePath)
    {
        fileLabel.Text = relativePath;
        PumpMessages();
    }

    public void ReportProgress(int filesPercent, int currentFilePercent, int filesCompleted, int fileCount)
    {
        filesProgressBar.Value = Math.Clamp(filesPercent, 0, 100);
        fileProgressBar.Value = Math.Clamp(currentFilePercent, 0, 100);
        filesCaptionLabel.Text = fileCount == 1
            ? "Overall (1 file)"
            : $"Overall ({filesCompleted} of {fileCount} files)";
        PumpMessages();
    }

    private static void PumpMessages() => Application.DoEvents();
}
