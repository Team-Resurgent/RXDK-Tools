namespace Rxdk.XbShellExt.Ui.Controls;

public sealed partial class DriveUsagePieControl : UserControl
{
    public static Color UsedColor { get; } = Color.FromArgb(0x4A, 0x6C, 0xF7);
    public static Color FreeColor { get; } = Color.FromArgb(0x9B, 0x59, 0xB6);
    private static readonly Color OutlineColor = Color.FromArgb(0x60, 0x60, 0x60);

    private double _usedPercent = 100;

    public double UsedPercent
    {
        get => _usedPercent;
        set
        {
            _usedPercent = value;
            Invalidate();
        }
    }

    public DriveUsagePieControl()
    {
        InitializeComponent();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var size = Math.Min(Width, Height) - 8;
        if (size <= 0)
            return;

        var left = (Width - size) / 2f;
        var top = (Height - size) / 2f;
        var rect = new RectangleF(left, top, size, size);

        using var freeBrush = new SolidBrush(FreeColor);
        using var usedBrush = new SolidBrush(UsedColor);
        using var outlinePen = new Pen(OutlineColor);

        g.FillEllipse(freeBrush, rect);
        g.DrawEllipse(outlinePen, rect);

        var usedPct = Math.Clamp(UsedPercent, 0, 100);
        if (usedPct <= 0.01)
            return;

        if (usedPct >= 99.99)
        {
            g.FillEllipse(usedBrush, rect);
            g.DrawEllipse(outlinePen, rect);
            return;
        }

        var sweep = (float)(usedPct / 100 * 360);
        g.FillPie(usedBrush, rect.X, rect.Y, rect.Width, rect.Height, -90, sweep);
        g.DrawEllipse(outlinePen, rect);
    }
}
