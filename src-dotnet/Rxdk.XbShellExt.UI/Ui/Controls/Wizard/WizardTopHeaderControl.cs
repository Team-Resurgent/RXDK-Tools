namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

public sealed partial class WizardTopHeaderControl : UserControl
{
    public WizardTopHeaderControl()
    {
        InitializeComponent();
        headerLogo.Image = WizardVisuals.LoadEmbeddedBitmap("Rxdk.XbShellExt.Assets.xheader.bmp");
    }

    public string HeaderTitle
    {
        get => headerTitle.Text;
        set => headerTitle.Text = value;
    }

    public string HeaderSubtitle
    {
        get => headerSubtitle.Text;
        set => headerSubtitle.Text = value;
    }
}
