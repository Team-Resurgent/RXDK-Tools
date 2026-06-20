using System.Reflection;

namespace Rxdk.XbShellExt.Ui.Controls;

public sealed partial class ShellWizardChrome : UserControl
{
    public const int BannerWidth = 164;
    public const int InnerHeaderHeight = 57;

    public Panel ContentHost => contentHost;

    public int ContentInnerWidth
    {
        get
        {
            var width = contentHost.ClientSize.Width - contentHost.Padding.Horizontal;
            return Math.Max(200, width);
        }
    }

    public ShellWizardChrome()
    {
        InitializeComponent();
        LoadWizardImages();
    }

    public void ShowWelcomeOrFinishLayout()
    {
        layout.ColumnStyles[0].Width = BannerWidth;
        layout.ColumnStyles[0].SizeType = SizeType.Absolute;
        bannerPanel.Visible = true;
        headerPanel.Visible = false;
        headerSeparator.Visible = false;
        contentHost.BackColor = SystemColors.Window;
        contentHost.Padding = new Padding(8, 10, 12, 8);
    }

    public void ShowInnerPageLayout(string title, string subtitle)
    {
        layout.ColumnStyles[0].Width = 0;
        layout.ColumnStyles[0].SizeType = SizeType.Absolute;
        bannerPanel.Visible = false;
        headerPanel.Visible = true;
        headerSeparator.Visible = true;
        headerTitle.Text = title;
        headerSubtitle.Text = subtitle;
        contentHost.BackColor = SystemColors.Control;
        contentHost.Padding = new Padding(12, 10, 12, 8);
    }

    public static Font CreateWizardTitleFont() =>
        new("Verdana", 12f, FontStyle.Bold, GraphicsUnit.Point);

    public static Font CreateWizardBodyFont() =>
        new("MS Shell Dlg 2", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

    public static Label CreateWrapLabel(string text, int width, Font? font = null)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Font = font,
        };
        label.Height = label.PreferredHeight;
        return label;
    }

    private void LoadWizardImages()
    {
        bannerPicture.Image = LoadBitmap("xwmark.bmp");
        headerLogo.Image = LoadBitmap("xheader.bmp");
    }

    private static Image? LoadBitmap(string fileName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith('.' + fileName, StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                    return Image.FromStream(stream);
            }

            var dir = Path.GetDirectoryName(assembly.Location);
            if (!string.IsNullOrEmpty(dir))
            {
                var path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                    return Image.FromFile(path);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
