namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

public sealed partial class WizardTopHeaderControl : UserControl
{
    public WizardTopHeaderControl()
    {
        InitializeComponent();
        headerLogo.Image = WizardVisuals.LoadEmbeddedBitmap("Rxdk.XbShellExt.Assets.xheader.bmp");
        headerTitle.AutoSize = true;
        headerSubtitle.AutoSize = true;
        Resize += (_, _) => LayoutHeader();
        LayoutHeader();
    }

    public string HeaderTitle
    {
        get => headerTitle.Text;
        set
        {
            headerTitle.Text = value;
            LayoutHeader();
        }
    }

    public string HeaderSubtitle
    {
        get => headerSubtitle.Text;
        set
        {
            headerSubtitle.Text = value;
            LayoutHeader();
        }
    }

    private void LayoutHeader()
    {
        const int left = 12;
        const int top = 8;
        const int logoWidth = 68;
        const int bottomPad = 8;

        var textWidth = Math.Max(200, Width - left - logoWidth - 8);
        headerTitle.MaximumSize = new Size(textWidth, 0);
        headerSubtitle.MaximumSize = new Size(textWidth, 0);

        headerTitle.Location = new Point(left, top);
        headerSubtitle.Location = new Point(left, headerTitle.Bottom + 4);
        headerLogo.Location = new Point(Math.Max(left, Width - logoWidth), top);

        var contentBottom = string.IsNullOrEmpty(headerSubtitle.Text)
            ? headerTitle.Bottom
            : headerSubtitle.Bottom;
        Height = Math.Max(WizardVisuals.InnerHeaderHeight, contentBottom + bottomPad);
    }
}
