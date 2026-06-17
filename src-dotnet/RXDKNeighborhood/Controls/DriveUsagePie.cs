using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RXDKNeighborhood.Controls;

public class DriveUsagePie : Control
{
  public static readonly StyledProperty<double> UsedPercentProperty =
      AvaloniaProperty.Register<DriveUsagePie, double>(nameof(UsedPercent), 100);

  private static readonly IBrush UsedBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6C, 0xF7));
  private static readonly IBrush FreeBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6));
  private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

  public double UsedPercent
  {
    get => GetValue(UsedPercentProperty);
    set => SetValue(UsedPercentProperty, value);
  }

  static DriveUsagePie()
  {
    AffectsRender<DriveUsagePie>(UsedPercentProperty);
  }

  protected override Size MeasureOverride(Size availableSize) => new(140, 140);

  protected override Size ArrangeOverride(Size finalSize) =>
      new(Math.Min(140, finalSize.Width), Math.Min(140, finalSize.Height));

  public override void Render(DrawingContext context)
  {
    var width = Bounds.Width;
    var height = Bounds.Height;
    if (width <= 1 || height <= 1)
      return;

    var size = Math.Min(width, height) - 8;
    var left = (width - size) / 2;
    var top = (height - size) / 2;
    var rect = new Rect(left, top, size, size);
    var center = rect.Center;
    var radius = size / 2;
    var usedPct = Math.Clamp(UsedPercent, 0, 100);

    context.DrawEllipse(FreeBrush, new Pen(OutlineBrush, 1), center, radius, radius);

    if (usedPct <= 0.01)
      return;

    if (usedPct >= 99.99)
    {
      context.DrawEllipse(UsedBrush, new Pen(OutlineBrush, 1), center, radius, radius);
      return;
    }

    var usedAngle = usedPct / 100 * 360;
    var geometry = new StreamGeometry();
    using (var g = geometry.Open())
    {
      g.BeginFigure(center, true);
      g.LineTo(new Point(center.X, center.Y - radius));
      g.ArcTo(
          Polar(center, radius, -90 + usedAngle),
          new Size(radius, radius),
          0,
          usedAngle > 180,
          SweepDirection.Clockwise);
      g.EndFigure(true);
    }

    context.DrawGeometry(UsedBrush, null, geometry);
    context.DrawEllipse(null, new Pen(OutlineBrush, 1), center, radius, radius);
  }

  private static Point Polar(Point center, double radius, double degrees)
  {
    var radians = degrees * Math.PI / 180;
    return new Point(
        center.X + radius * Math.Cos(radians),
        center.Y + radius * Math.Sin(radians));
  }
}
