using Rxdk.XbShellExt.Ui;

namespace Rxdk.XbShellExt.Ui.Controls.Properties;

public sealed partial class SecurityTab : UserControl
{
    public SecurityTab()
    {
        InitializeComponent();
        securityCaption.Font = BoldFont();
        accessCaption.Font = BoldFont();
        DesignPreview.ApplyIfDesignTime(() => Bind(false, DesignPreview.SampleAccessText));
    }

    public void Bind(bool isLocked, string accessText)
    {
        securityValue.Text = isLocked ? "Yes" : "No";
        accessValue.Text = accessText;
    }

    private static Font BoldFont() =>
        new(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
}
