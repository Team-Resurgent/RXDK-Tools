namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

using Rxdk.XbShellExt.Ui;
using Rxdk.XbShellExt.Ui.Controls;

public sealed partial class AddConsoleFinishPage : UserControl
{
    public Label ConsoleValueLabel => consoleValueLabel;
    public Label DefaultValueLabel => defaultValueLabel;
    public Label PermissionsValueLabel => permissionsValueLabel;

    public AddConsoleFinishPage()
    {
        InitializeComponent();
        titleLabel.Font = ShellWizardChrome.CreateWizardTitleFont();
        DesignPreview.ApplyIfDesignTime(() =>
        {
            consoleValueLabel.Text = $"{DesignPreview.SampleConsoleName}({DesignPreview.SampleConsoleIp})";
            defaultValueLabel.Text = "Yes";
            permissionsValueLabel.Text = "Read, Write, Configure, Control, Manage";
            ShowPermissionsRow(true);
        });
    }

    public void ShowPermissionsRow(bool visible)
    {
        permissionsCaption.Visible = visible;
        permissionsValueLabel.Visible = visible;
    }
}
