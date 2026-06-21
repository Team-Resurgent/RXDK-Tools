using System.Reflection;

namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

internal static class WizardVisuals
{
    public const int BannerWidth = 164;
    public const int InnerHeaderHeight = 80;

    public static Image? LoadEmbeddedBitmap(string resourceName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            return stream != null ? Image.FromStream(stream) : null;
        }
        catch
        {
            return null;
        }
    }
}
