namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

using Rxdk.XbShellExt.Ui;

public sealed partial class AddConsoleFinishPage : AddConsoleWizardPageBase
{
    public Label ConsoleValueLabel => consoleValueLabel;
    public Label DefaultValueLabel => defaultValueLabel;
    public Label PermissionsValueLabel => permissionsValueLabel;

    public AddConsoleFinishPage()
    {
        InitializeComponent();
        BindStatusLabel(statusLabel);
        titleLabel.Font = WizardVisuals.CreateTitleFont();
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
