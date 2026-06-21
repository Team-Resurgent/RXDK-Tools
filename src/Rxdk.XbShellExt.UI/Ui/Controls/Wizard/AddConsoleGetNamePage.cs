namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

using Rxdk.XbShellExt.Ui;

public sealed partial class AddConsoleGetNamePage : AddConsoleWizardPageBase
{
    public TextBox NameTextBox => nameTextBox;

    public AddConsoleGetNamePage()
    {
        InitializeComponent();
        BindStatusLabel(statusLabel);
        pageHeader.HeaderTitle = "Specifying an Xbox Development Kit";
        pageHeader.HeaderSubtitle = "The wizard needs to know which Xbox Development Kit to add.";
        DesignPreview.ApplyIfDesignTime(() => SetConsoleName(DesignPreview.SampleConsoleName));
    }

    public void SetConsoleName(string name) => nameTextBox.Text = name;
}
