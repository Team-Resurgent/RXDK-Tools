namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

using Rxdk.XbShellExt.Ui;

public sealed partial class AddConsoleGetNamePage : UserControl
{
    public TextBox NameTextBox => nameTextBox;

    public AddConsoleGetNamePage()
    {
        InitializeComponent();
        DesignPreview.ApplyIfDesignTime(() => SetConsoleName(DesignPreview.SampleConsoleName));
    }

    public void SetConsoleName(string name) => nameTextBox.Text = name;
}
