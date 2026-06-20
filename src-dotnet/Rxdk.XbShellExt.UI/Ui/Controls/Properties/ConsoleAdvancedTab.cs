using Rxdk.XbShellExt.Ui;
using Rxdk.Xbdm.KitServices.Services;
using KitPropertySession = Rxdk.Xbdm.KitServices.Services.PropertySession;

namespace Rxdk.XbShellExt.Ui.Controls.Properties;

public sealed partial class ConsoleAdvancedTab : UserControl
{
    public ConsoleAdvancedTab()
    {
        InitializeComponent();
        ShellDialogLayout.ConfigureButton(rebootButton);
        DesignPreview.ApplyIfDesignTime(() =>
            statusLabel.Text = "Reboot is available when connected to a development kit.");
    }

    public void WireReboot(KitPropertySession session)
    {
        rebootButton.Click += (_, _) =>
        {
            try
            {
                string? launch = restartSameTitleCheck.Checked ? session.Connection.GetXbeLaunchPath() : null;
                session.Connection.Reboot(cold: !warmRebootCheck.Checked, launch);
                statusLabel.Text = "Reboot command sent.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = ex.Message;
            }
        };
    }
}
