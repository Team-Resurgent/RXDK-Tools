using System.ComponentModel;

namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

[DesignTimeVisible(false)]
public class AddConsoleWizardPageBase : UserControl, IAddConsoleWizardPage
{
    private Label? _statusLabel;

    protected void BindStatusLabel(Label statusLabel)
    {
        _statusLabel = statusLabel ?? throw new ArgumentNullException(nameof(statusLabel));
    }

    public void ClearStatus()
    {
        if (_statusLabel != null)
            _statusLabel.Text = string.Empty;
    }

    public void SetStatus(string message)
    {
        if (_statusLabel != null)
            _statusLabel.Text = message;
    }
}
