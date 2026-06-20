namespace Rxdk.XbShellExt.Ui.Forms;

using System.ComponentModel;
using Rxdk.XbShellExt.Ui;

public sealed partial class TransferProgressForm : Form
{
    private bool _cancelRequested;
    private bool _failedMode;
    private bool _completeMode;

    public event Action? CancelRequested;
    public event Action? CloseRequested;

    public TransferProgressForm()
    {
        InitializeComponent();
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            ShellModernChrome.Apply(this);
        cancelButton.Click += (_, _) => OnCancelClicked();
        FormClosing += OnFormClosing;
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
        _completeMode = true;
        filesProgressBar.Value = 100;
        fileProgressBar.Value = 100;
        PumpMessages();

        var closeTimer = new System.Windows.Forms.Timer { Interval = 750 };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            closeTimer.Dispose();
            if (!IsDisposed && !_failedMode)
                Close();
        };
        closeTimer.Start();
    }

    public void Fail(string message) => ShowTerminalState("Transfer failed", message);

    private void ShowTerminalState(string title, string message)
    {
        _failedMode = true;
        Text = "Xbox Neighborhood";
        titleLabel.Text = title;
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
        if (_failedMode || _completeMode)
            return;

        fileLabel.Text = relativePath;
        PumpMessages();
    }

    public void ReportProgress(int filesPercent, int currentFilePercent, int filesCompleted, int fileCount)
    {
        if (_failedMode || _completeMode)
            return;

        filesProgressBar.Value = Math.Clamp(filesPercent, 0, 100);
        fileProgressBar.Value = Math.Clamp(currentFilePercent, 0, 100);
        filesCaptionLabel.Text = fileCount == 1
            ? "Overall (1 file)"
            : $"Overall ({filesCompleted} of {fileCount} files)";
        PumpMessages();
    }

    private static void PumpMessages() => Application.DoEvents();

    private void OnCancelClicked()
    {
        if (_failedMode)
        {
            Close();
            return;
        }

        if (_cancelRequested)
            return;

        _cancelRequested = true;
        CancelRequested?.Invoke();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_failedMode || _completeMode || _cancelRequested)
            return;

        _cancelRequested = true;
        CloseRequested?.Invoke();
    }
}
