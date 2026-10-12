using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>
/// The battery's last 24 hours: the charge as a line with a soft fill under
/// it, faint guides at a quarter, half and three quarters, a shade while the
/// charger was in with a dotted line and a bolt where it went in, and the
/// time under every few hours. Hovering shows the time and charge there.
/// </summary>
public sealed class ChargeChart : FrameworkElement
{
    private const double ScaleWidth = 44;   // The 100%, 50% and 0% at the left.
    private const double TopRoom = 26;      // Above 100%, for the bolts.
    private const double BottomRoom = 32;   // The ticks and times under the line.
    private const double FontSize = 11;

    public static readonly DependencyProperty PointsProperty = Register<IReadOnlyList<Point>>(nameof(Points));
    public static readonly DependencyProperty ChargingProperty = Register<IReadOnlyList<Point>>(nameof(Charging));
    public static readonly DependencyProperty PlugInsProperty = Register<IReadOnlyList<double>>(nameof(PlugIns));
    public static readonly DependencyProperty TickLabelsProperty = Register<IReadOnlyList<string>>(nameof(TickLabels));
    public static readonly DependencyProperty AccentProperty = Register<Brush>(nameof(Accent));
    public static readonly DependencyProperty LineBrushProperty = Register<Brush>(nameof(LineBrush));
    public static readonly DependencyProperty TextBrushProperty = Register<Brush>(nameof(TextBrush));

    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(DateTime), typeof(ChargeChart), new PropertyMetadata(default(DateTime)));

    /// <summary>The charge over time: X across the chart and Y the charge, both 0 to 1, oldest first.</summary>
    public IReadOnlyList<Point>? Points { get => (IReadOnlyList<Point>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    /// <summary>The stretches with the charger in, as X from and Y to, fractions of the width.</summary>
    public IReadOnlyList<Point>? Charging { get => (IReadOnlyList<Point>?)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }

    /// <summary>Where the charger went in, fractions of the width: a dotted line and a bolt.</summary>
    public IReadOnlyList<double>? PlugIns { get => (IReadOnlyList<double>?)GetValue(PlugInsProperty); set => SetValue(PlugInsProperty, value); }

    /// <summary>The times under the chart, spread evenly from its left edge to its right.</summary>
    public IReadOnlyList<string>? TickLabels { get => (IReadOnlyList<string>?)GetValue(TickLabelsProperty); set => SetValue(TickLabelsProperty, value); }

    /// <summary>The time at the left edge; the chart covers 24 hours from it, for the hover tip.</summary>
    public DateTime Start { get => (DateTime)GetValue(StartProperty); set => SetValue(StartProperty, value); }

    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    private static DependencyProperty Register<T>(string name) =>
        DependencyProperty.Register(name, typeof(T), typeof(ChargeChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private Rect Plot => new(ScaleWidth, TopRoom, Math.Max(0, ActualWidth - ScaleWidth - 4), Math.Max(0, ActualHeight - TopRoom - BottomRoom));

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext context)
    {
        var plot = Plot;

        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        var accent = Accent ?? Brushes.LimeGreen;
        var lineBrush = LineBrush ?? Brushes.DimGray;
        var text = TextBrush ?? Brushes.Gray;
        var accentColor = accent is SolidColorBrush solid ? solid.Color : Colors.LimeGreen;

        // The background takes the mouse, so the tip shows anywhere over it.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // The charger's stretches, shaded from above 100% to the bottom.
        var shade = Frozen(new SolidColorBrush(Color.FromArgb(18, accentColor.R, accentColor.G, accentColor.B)));
        foreach (var span in Charging ?? [])
        {
            var from = plot.Left + span.X * plot.Width;
            var to = plot.Left + span.Y * plot.Width;

            if (to > from)
                context.DrawRectangle(shade, null, new Rect(from, plot.Top - TopRoom + 12, to - from, plot.Bottom - plot.Top + TopRoom - 12));
        }

        // 100%, 50% and 0% beside the scale, each on its own line: dashed guides at
        // 100% and 50%, the solid baseline (drawn below) at 0%. The digits, not the
        // text's box, are centered on the line, so a label never sits off its guide.
        var dashed = Frozen(new Pen(lineBrush, 1) { DashStyle = new DashStyle([3, 4], 0) });
        foreach (var (level, label) in new[] { (1.0, "100%"), (0.5, "50%"), (0.0, "0%") })
        {
            var y = level == 0 ? plot.Bottom + 0.5 : Math.Round(plot.Bottom - level * plot.Height) + 0.5;

            if (level > 0)
                context.DrawLine(dashed, new Point(plot.Left, y), new Point(plot.Right, y));

            var formatted = Text(label, text);
            var digitsHalf = FontFace().CapsHeight * FontSize / 2;
            context.DrawText(formatted, new Point(plot.Left - 10 - formatted.Width, y - formatted.Baseline + digitsHalf));
        }

        // The charge: a soft fill fading down, and the line over it.
        if (Points is { Count: > 0 } points)
        {
            var at = points.Select(point => new Point(plot.Left + point.X * plot.Width, plot.Bottom - Math.Clamp(point.Y, 0, 1) * (plot.Height - 2) - 1)).ToList();

            if (at.Count == 1)
            {
                context.DrawEllipse(accent, null, at[0], 2.5, 2.5);
            }
            else
            {
                var fill = Frozen(new LinearGradientBrush(
                    Color.FromArgb(70, accentColor.R, accentColor.G, accentColor.B),
                    Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), 90));

                var area = new StreamGeometry();
                using (var draw = area.Open())
                {
                    draw.BeginFigure(new Point(at[0].X, plot.Bottom), isFilled: true, isClosed: true);
                    draw.PolyLineTo([.. at, new Point(at[^1].X, plot.Bottom)], isStroked: false, isSmoothJoin: false);
                }
                area.Freeze();
                context.DrawGeometry(fill, null, area);

                var path = new StreamGeometry();
                using (var draw = path.Open())
                {
                    draw.BeginFigure(at[0], isFilled: false, isClosed: false);
                    draw.PolyLineTo(at.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
                }
                path.Freeze();

                var pen = Frozen(new Pen(accent, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
                context.DrawGeometry(null, pen, path);
            }
        }

        // Where the charger went in: a dotted line and a bolt above it.
        var dotted = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(150, accentColor.R, accentColor.G, accentColor.B))), 1.2)
        {
            DashStyle = new DashStyle([1, 2.5], 0)
        });
        var bolt = TryFindResource("Icon.Bolt") as Geometry;

        foreach (var plugIn in PlugIns ?? [])
        {
            var x = Math.Round(plot.Left + plugIn * plot.Width) + 0.5;
            context.DrawLine(dotted, new Point(x, plot.Top - TopRoom + 2), new Point(x, plot.Bottom));

            if (bolt is not null)
            {
                context.PushTransform(new TranslateTransform(x + 4, plot.Top - TopRoom + 3));
                context.PushTransform(new ScaleTransform(0.6, 0.6));
                context.DrawGeometry(accent, null, bolt);
                context.Pop();
                context.Pop();
            }
        }

        // The base line, its ticks, and the times under them.
        context.DrawLine(Frozen(new Pen(lineBrush, 1)), new Point(plot.Left, plot.Bottom + 0.5), new Point(plot.Right, plot.Bottom + 0.5));

        if (TickLabels is { Count: > 1 } labels)
        {
            var tick = Frozen(new Pen(text, 1.5));

            for (var i = 0; i < labels.Count; i++)
            {
                var x = Math.Round(plot.Left + plot.Width * i / (labels.Count - 1)) + 0.5;
                context.DrawLine(tick, new Point(x, plot.Bottom + 4), new Point(x, plot.Bottom + 12));

                var formatted = Text(labels[i], text);
                var left = Math.Clamp(x - formatted.Width / 2, 0, ActualWidth - formatted.Width);
                context.DrawText(formatted, new Point(left, plot.Bottom + 16));
            }
        }
    }

    // Its own tip, moved with the mouse, so it can change as it goes along the line.
    private ToolTip? _tip;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var plot = Plot;
        var mouse = e.GetPosition(this);

        if (Points is not { Count: > 0 } points || plot.Width <= 0 || mouse.X < plot.Left || mouse.X > plot.Right)
        {
            HideTip();
            return;
        }

        var x = (mouse.X - plot.Left) / plot.Width;

        if (ChargeAt(points, x) is not double charge)
        {
            HideTip();
            return;
        }

        var time = Start.AddHours(24 * x).ToString("t", CultureInfo.CurrentCulture);

        _tip ??= new ToolTip { PlacementTarget = this, Placement = PlacementMode.Relative };
        _tip.Content = $"{time}  ·  {Math.Round(charge * 100)}%";
        _tip.HorizontalOffset = mouse.X + 12;
        _tip.VerticalOffset = mouse.Y + 18;
        _tip.IsOpen = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        HideTip();
    }

    private void HideTip()
    {
        if (_tip is not null)
            _tip.IsOpen = false;
    }

    // The line's height at x, or null outside it.
    private static double? ChargeAt(IReadOnlyList<Point> points, double x)
    {
        if (x < points[0].X || x > points[^1].X)
            return null;

        for (var i = 1; i < points.Count; i++)
        {
            if (x > points[i].X)
                continue;

            var span = points[i].X - points[i - 1].X;
            return span <= 0 ? points[i].Y : points[i - 1].Y + (points[i].Y - points[i - 1].Y) * (x - points[i - 1].X) / span;
        }

        return points[^1].Y;
    }

    private FormattedText Text(string value, Brush brush) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontFace(),
            FontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Typeface FontFace() =>
        new(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
