using System.Windows;
using System.Windows.Media;

namespace MetadataPatchEditor;

/// <summary>
/// Small code-native vector icon used by the visual status editor. Keeping the
/// artwork in WPF avoids external bitmap assets and lets every status inherit
/// the selected element's accent colour at any DPI.
/// </summary>
public sealed class GameplayIcon : FrameworkElement
{
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(GameplayIcon),
        new FrameworkPropertyMetadata("effect", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(GameplayIcon),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public string IconKey { get => (string)GetValue(IconKeyProperty); set => SetValue(IconKeyProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsNaN(Width) ? 34 : Width;
        double height = double.IsNaN(Height) ? 34 : Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double size = Math.Max(1, Math.Min(ActualWidth, ActualHeight));
        double ox = (ActualWidth - size) / 2;
        double oy = (ActualHeight - size) / 2;
        dc.PushTransform(new TranslateTransform(ox, oy));
        dc.PushTransform(new ScaleTransform(size / 32, size / 32));

        Brush accent = Accent ?? Brushes.DeepSkyBlue;
        var faint = accent.Clone();
        faint.Opacity = 0.13;
        dc.DrawEllipse(faint, new Pen(accent, 1.1), new Point(16, 16), 15, 15);

        var pen = new Pen(accent, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        string key = (IconKey ?? "effect").ToLowerInvariant();
        switch (key)
        {
            case "cold": case "slow": Snowflake(dc, pen, key == "slow" ? 3 : 6); break;
            case "freeze": Diamond(dc, pen); break;
            case "heat": Flame(dc, accent, pen); break;
            case "electricity": case "stun": Lightning(dc, accent); break;
            case "toxin": case "dot": Droplet(dc, accent, pen); break;
            case "gas": case "cloud": Cloud(dc, pen); break;
            case "blast": Burst(dc, pen); break;
            case "radiation": case "confusion": Radiation(dc, pen); break;
            case "magnetic": case "shield": Magnet(dc, pen); break;
            case "viral": case "health": Cell(dc, pen); break;
            case "corrosive": case "armor": Corrosive(dc, pen); break;
            case "impact": Hammer(dc, pen); break;
            case "puncture": Spear(dc, pen); break;
            case "slash": case "bleed": Slash(dc, pen); break;
            case "void": case "orbit": Orbit(dc, pen); break;
            case "tau": Star(dc, pen); break;
            case "vulnerability": Target(dc, pen); break;
            case "damage-down": DownArrow(dc, pen); break;
            case "instances": Layers(dc, pen); break;
            case "duration": Clock(dc, pen); break;
            case "stacks": Layers(dc, pen); break;
            case "scope": Target(dc, pen); break;
            default: Spark(dc, pen); break;
        }
        dc.Pop();
        dc.Pop();
    }

    static void Snowflake(DrawingContext dc, Pen p, int spokes)
    {
        int count = Math.Max(3, spokes);
        for (int i = 0; i < count; i++)
        {
            double a = Math.PI * i / count;
            var v = new Vector(Math.Cos(a) * 9, Math.Sin(a) * 9);
            dc.DrawLine(p, new Point(16 - v.X, 16 - v.Y), new Point(16 + v.X, 16 + v.Y));
        }
        dc.DrawEllipse(null, p, new Point(16, 16), 2.1, 2.1);
    }

    static void Diamond(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M16,6 L25,16 16,26 7,16 Z M16,9 L16,23 M10,16 L22,16"));

    static void Flame(DrawingContext dc, Brush b, Pen p)
    {
        var fill = b.Clone(); fill.Opacity = 0.28;
        dc.DrawGeometry(fill, p, Geometry.Parse("M17,5 C20,11 25,12 24,19 C23,25 18,27 13,25 C8,23 7,18 10,14 C12,12 13,10 13,7 C16,9 17,11 17,14 C19,12 19,9 17,5 Z"));
    }

    static void Lightning(DrawingContext dc, Brush b)
        => dc.DrawGeometry(b, null, Geometry.Parse("M18,5 L9,18 15,18 13,27 24,14 18,14 Z"));

    static void Droplet(DrawingContext dc, Brush b, Pen p)
    {
        var fill = b.Clone(); fill.Opacity = 0.25;
        dc.DrawGeometry(fill, p, Geometry.Parse("M16,5 C16,5 9,14 9,19 C9,24 12,27 16,27 C20,27 23,24 23,19 C23,14 16,5 16,5 Z"));
    }

    static void Cloud(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M8,21 C5,16 9,13 13,14 C14,9 22,9 23,15 C28,15 28,22 23,23 L10,23 C7,23 6,22 8,21 Z"));

    static void Burst(DrawingContext dc, Pen p)
    {
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI * i / 4;
            dc.DrawLine(p, new Point(16 + Math.Cos(a) * 5, 16 + Math.Sin(a) * 5),
                new Point(16 + Math.Cos(a) * 11, 16 + Math.Sin(a) * 11));
        }
        dc.DrawEllipse(null, p, new Point(16, 16), 4, 4);
    }

    static void Radiation(DrawingContext dc, Pen p)
    {
        dc.DrawEllipse(null, p, new Point(16, 16), 2.5, 2.5);
        dc.DrawArc(p, new Point(16, 16), 5, -90, 62);
        dc.DrawArc(p, new Point(16, 16), 5, 30, 62);
        dc.DrawArc(p, new Point(16, 16), 5, 150, 62);
    }

    static void Magnet(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M9,8 L9,18 C9,28 23,28 23,18 L23,8 M9,11 L14,11 M18,11 L23,11"));

    static void Cell(DrawingContext dc, Pen p)
    {
        dc.DrawEllipse(null, p, new Point(16, 16), 8, 8);
        dc.DrawEllipse(null, p, new Point(13, 14), 1.5, 1.5);
        dc.DrawEllipse(null, p, new Point(19, 18), 2, 2);
        dc.DrawLine(p, new Point(7, 11), new Point(10, 12));
        dc.DrawLine(p, new Point(22, 9), new Point(21, 12));
    }

    static void Corrosive(DrawingContext dc, Pen p)
    {
        dc.DrawGeometry(null, p, Geometry.Parse("M8,22 L24,22 M10,25 L22,25 M11,8 L15,14 M20,7 L17,14"));
        dc.DrawEllipse(null, p, new Point(15, 17), 1.5, 2.2);
        dc.DrawEllipse(null, p, new Point(18, 18), 1.3, 1.8);
    }

    static void Hammer(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M8,8 L17,8 20,11 16,15 13,12 7,12 Z M15,14 L23,22 20,25 12,17"));

    static void Spear(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M7,25 L21,11 M18,8 L25,7 24,14 21,11 Z M9,21 L12,24"));

    static void Slash(DrawingContext dc, Pen p)
    {
        dc.DrawLine(p, new Point(9, 25), new Point(18, 7));
        dc.DrawLine(p, new Point(14, 26), new Point(23, 8));
    }

    static void Orbit(DrawingContext dc, Pen p)
    {
        dc.DrawEllipse(null, p, new Point(16, 16), 3, 3);
        dc.DrawEllipse(null, p, new Point(16, 16), 10, 5);
        dc.DrawEllipse(null, p, new Point(16, 16), 5, 10);
    }

    static void Star(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M16,5 L19,13 27,16 19,19 16,27 13,19 5,16 13,13 Z"));

    static void Target(DrawingContext dc, Pen p)
    {
        dc.DrawEllipse(null, p, new Point(16, 16), 8, 8);
        dc.DrawEllipse(null, p, new Point(16, 16), 3, 3);
        dc.DrawLine(p, new Point(16, 5), new Point(16, 9));
        dc.DrawLine(p, new Point(16, 23), new Point(16, 27));
    }

    static void DownArrow(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M16,7 L16,23 M10,17 L16,23 22,17"));

    static void Layers(DrawingContext dc, Pen p)
    {
        dc.DrawGeometry(null, p, Geometry.Parse("M7,12 L16,7 25,12 16,17 Z"));
        dc.DrawGeometry(null, p, Geometry.Parse("M7,17 L16,22 25,17 M7,21 L16,26 25,21"));
    }

    static void Clock(DrawingContext dc, Pen p)
    {
        dc.DrawEllipse(null, p, new Point(16, 16), 9, 9);
        dc.DrawLine(p, new Point(16, 16), new Point(16, 10));
        dc.DrawLine(p, new Point(16, 16), new Point(21, 18));
    }

    static void Spark(DrawingContext dc, Pen p)
        => dc.DrawGeometry(null, p, Geometry.Parse("M16,6 L18,14 26,16 18,18 16,26 14,18 6,16 14,14 Z"));
}

static class DrawingContextExtensions
{
    public static void DrawArc(this DrawingContext dc, Pen pen, Point center, double radius, double startDegrees, double sweepDegrees)
    {
        double start = startDegrees * Math.PI / 180;
        double end = (startDegrees + sweepDegrees) * Math.PI / 180;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X + Math.Cos(start) * radius, center.Y + Math.Sin(start) * radius), false, false);
            context.ArcTo(new Point(center.X + Math.Cos(end) * radius, center.Y + Math.Sin(end) * radius),
                new Size(radius, radius), 0, Math.Abs(sweepDegrees) > 180,
                sweepDegrees >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
