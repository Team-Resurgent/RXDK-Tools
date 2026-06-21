using System.Reflection;

namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

internal static class WizardVisuals
{
    public const int BannerWidth = 164;
    public const int InnerHeaderHeight = 57;

    public static Font CreateTitleFont() =>
        new("Verdana", 12f, FontStyle.Bold, GraphicsUnit.Point);

    public static Font CreateBodyFont() =>
        new("MS Shell Dlg 2", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

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
