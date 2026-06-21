namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

public sealed partial class AddConsoleWelcomePage : AddConsoleWizardPageBase
{
    public AddConsoleWelcomePage()
    {
        InitializeComponent();
        BindStatusLabel(statusLabel);
        titleLabel.Font = WizardVisuals.CreateTitleFont();
    }
}
