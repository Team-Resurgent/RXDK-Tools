namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

using Rxdk.XbShellExt.Ui.Controls;

public sealed partial class AddConsoleWelcomePage : UserControl
{
    public AddConsoleWelcomePage()
    {
        InitializeComponent();
        titleLabel.Font = ShellWizardChrome.CreateWizardTitleFont();
    }
}
