namespace Rxdk.XbShellExt.Ui.Forms;

using System.ComponentModel;
using Rxdk.XbShellExt.Ui;

public sealed partial class TransferProgressForm : Form
{
    private bool _cancelRequested;

    public TransferProgressForm()
    {
        InitializeComponent();
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            ShellModernChrome.Apply(this);
        cancelButton.Click += (_, _) => _cancelRequested = true;
        ShellDialogLayout.ConfigureButton(cancelButton);
        DesignPreview.ApplyIfDesignTime(() =>
        {
            titleLabel.Text = "Copying to Xbox";
            fileLabel.Text = @"Games\Halo\default.xbe";
            progressBar.Maximum = 100;
            progressBar.Value = 42;
        });
    }

    public TransferProgressForm(string title)
        : this()
    {
        titleLabel.Text = title;
    }

    public bool IsCancelRequested => _cancelRequested;

    public void Configure(ulong totalBytes, int fileCount)
    {
        if (totalBytes > int.MaxValue)
        {
            progressBar.Maximum = int.MaxValue;
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 30;
        }
        else
        {
            progressBar.Maximum = Math.Max(1, (int)totalBytes);
            progressBar.Value = 0;
        }

        fileLabel.Text = fileCount == 1
            ? "Copying 1 item…"
            : $"Copying {fileCount} items…";
    }

    public void SetCurrentFile(string relativePath)
    {
        fileLabel.Text = relativePath;
        PumpMessages();
    }

    public void ReportBytes(ulong completedBytes, ulong totalBytes)
    {
        if (progressBar.Style == ProgressBarStyle.Continuous)
        {
            var value = totalBytes > int.MaxValue
                ? (int)(completedBytes * int.MaxValue / Math.Max(1UL, totalBytes))
                : (int)Math.Min(completedBytes, (ulong)progressBar.Maximum);
            progressBar.Value = Math.Clamp(value, 0, progressBar.Maximum);
        }

        PumpMessages();
    }

    private static void PumpMessages() => Application.DoEvents();
}
