namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

public sealed partial class WizardSideBannerControl : UserControl
{
    public WizardSideBannerControl()
    {
        InitializeComponent();
        bannerPicture.Image = WizardVisuals.LoadEmbeddedBitmap("Rxdk.XbShellExt.Assets.xwmark.bmp");
    }
}
