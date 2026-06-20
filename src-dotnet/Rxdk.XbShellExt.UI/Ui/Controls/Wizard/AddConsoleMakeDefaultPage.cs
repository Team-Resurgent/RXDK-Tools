namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

using Rxdk.XbShellExt.Ui;

public sealed partial class AddConsoleMakeDefaultPage : UserControl
{
    public RadioButton MakeDefaultYes => makeDefaultYes;
    public RadioButton MakeDefaultNo => makeDefaultNo;

    public AddConsoleMakeDefaultPage()
    {
        InitializeComponent();
        DesignPreview.ApplyIfDesignTime(() =>
        {
            SetPrompt(DesignPreview.SampleConsoleName);
            SetMakeDefault(true);
        });
    }

    public void SetPrompt(string consoleName)
    {
        promptLabel.Text = $"Would you like to use '{consoleName}' as the default Xbox?";
    }

    public void SetMakeDefault(bool makeDefault)
    {
        makeDefaultYes.Checked = makeDefault;
        makeDefaultNo.Checked = !makeDefault;
    }
}
