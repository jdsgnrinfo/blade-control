using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>
/// The battery's day as Windows' Settings draws it: a rounded bar per hour
/// for the charge, guides at 50% and 100%, a soft shade while the charger was
/// in with a dotted line and a bolt where it was plugged in, and the time
/// under every few hours. Hovering a bar shows its hour and charge.
/// </summary>
public sealed class BatteryBarChart : FrameworkElement
{
    private const double ScaleWidth = 44;   // The 100% and 50% at the left.
    private const double TopRoom = 26;      // Above 100%, for the bolts.
    private const double BottomRoom = 32;   // The ticks and times under the bars.
    private const double FontSize = 11;

    public static readonly DependencyProperty BarsProperty = Register<IReadOnlyList<double>>(nameof(Bars));
    public static readonly DependencyProperty ChargingProperty = Register<IReadOnlyList<Point>>(nameof(Charging));
    public static readonly DependencyProperty PlugInsProperty = Register<IReadOnlyList<double>>(nameof(PlugIns));
    public static readonly DependencyProperty TickLabelsProperty = Register<IReadOnlyList<string>>(nameof(TickLabels));
    public static readonly DependencyProperty BarTipsProperty = Register<IReadOnlyList<string>>(nameof(BarTips));
    public static readonly DependencyProperty AccentProperty = Register<Brush>(nameof(Accent));
    public static readonly DependencyProperty LineBrushProperty = Register<Brush>(nameof(LineBrush));
    public static readonly DependencyProperty TextBrushProperty = Register<Brush>(nameof(TextBrush));

    /// <summary>Each hour's charge, 0 to 1, oldest first; NaN for an hour with no record.</summary>
    public IReadOnlyList<double>? Bars { get => (IReadOnlyList<double>?)GetValue(BarsProperty); set => SetValue(BarsProperty, value); }

    /// <summary>The stretches with the charger in, as X from and Y to, fractions of the width.</summary>
    public IReadOnlyList<Point>? Charging { get => (IReadOnlyList<Point>?)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }

    /// <summary>Where the charger went in, fractions of the width: a dotted line and a bolt.</summary>
    public IReadOnlyList<double>? PlugIns { get => (IReadOnlyList<double>?)GetValue(PlugInsProperty); set => SetValue(PlugInsProperty, value); }

    /// <summary>The times under the chart, spread evenly from its left edge to its right.</summary>
    public IReadOnlyList<string>? TickLabels { get => (IReadOnlyList<string>?)GetValue(TickLabelsProperty); set => SetValue(TickLabelsProperty, value); }

    /// <summary>What hovering each bar says.</summary>
    public IReadOnlyList<string>? BarTips { get => (IReadOnlyList<string>?)GetValue(BarTipsProperty); set => SetValue(BarTipsProperty, value); }

    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    private static DependencyProperty Register<T>(string name) =>
        DependencyProperty.Register(name, typeof(T), typeof(BatteryBarChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private Rect Plot => new(ScaleWidth, TopRoom, Math.Max(0, ActualWidth - ScaleWidth - 4), Math.Max(0, ActualHeight - TopRoom - BottomRoom));

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext context)
    {
        var plot = Plot;

        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        var accent = Accent ?? Brushes.LimeGreen;
        var line = LineBrush ?? Brushes.DimGray;
        var text = TextBrush ?? Brushes.Gray;
        var accentColor = accent is SolidColorBrush solid ? solid.Color : Colors.LimeGreen;

        // The background takes the mouse, so a bar's tip shows anywhere in its column.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // The charger's stretches, shaded from above 100% to the bottom.
        var shade = Frozen(new SolidColorBrush(Color.FromArgb(22, accentColor.R, accentColor.G, accentColor.B)));
        foreach (var span in Charging ?? [])
        {
            var from = plot.Left + span.X * plot.Width;
            var to = plot.Left + span.Y * plot.Width;

            if (to > from)
                context.DrawRectangle(shade, null, new Rect(from, plot.Top - TopRoom + 12, to - from, plot.Height + TopRoom - 12));
        }

        // 100% and 50%.
        var guide = Frozen(new Pen(line, 1));
        foreach (var (level, label) in new[] { (1.0, "100%"), (0.5, "50%") })
        {
            var y = Math.Round(plot.Bottom - level * plot.Height) + 0.5;
            context.DrawLine(guide, new Point(plot.Left - 8, y), new Point(plot.Right, y));
            var formatted = Text(label, text);
            context.DrawText(formatted, new Point(plot.Left - 14 - formatted.Width, y - formatted.Height / 2));
        }

        // A bar per hour: rounded, brighter at the top.
        if (Bars is { Count: > 0 } bars)
        {
            var slot = plot.Width / bars.Count;
            var width = Math.Clamp(slot * 0.42, 4, 16);
            var fill = Frozen(new LinearGradientBrush(accentColor, Darker(accentColor, 0.62), 90));

            for (var i = 0; i < bars.Count; i++)
            {
                if (double.IsNaN(bars[i]))
                    continue;

                var height = Math.Max(width, Math.Clamp(bars[i], 0, 1) * plot.Height);
                var x = plot.Left + slot * i + (slot - width) / 2;
                context.DrawRoundedRectangle(fill, null, new Rect(x, plot.Bottom - height, width, height), width / 2, width / 2);
            }
        }

        // Where the charger went in: a dotted line and a bolt above it.
        var dotted = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(150, accentColor.R, accentColor.G, accentColor.B))), 1.2)
        {
            DashStyle = new DashStyle([1, 2.5], 0)
        });
        var bolt = TryFindResource("Icon.Bolt") as Geometry;

        foreach (var at in PlugIns ?? [])
        {
            var x = Math.Round(plot.Left + at * plot.Width) + 0.5;
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
        context.DrawLine(guide, new Point(plot.Left, plot.Bottom + 0.5), new Point(plot.Right, plot.Bottom + 0.5));

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

    // Its own tip, moved with the mouse, so it can change from bar to bar while open.
    private ToolTip? _tip;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var plot = Plot;
        var at = e.GetPosition(this);

        if (BarTips is not { Count: > 0 } tips || plot.Width <= 0 || at.X < plot.Left || at.X > plot.Right)
        {
            HideTip();
            return;
        }

        var index = Math.Clamp((int)((at.X - plot.Left) / (plot.Width / tips.Count)), 0, tips.Count - 1);

        if (string.IsNullOrEmpty(tips[index]))
        {
            HideTip();
            return;
        }

        _tip ??= new ToolTip { PlacementTarget = this, Placement = PlacementMode.Relative };
        _tip.Content = tips[index];
        _tip.HorizontalOffset = at.X + 12;
        _tip.VerticalOffset = at.Y + 18;
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

    private FormattedText Text(string value, Brush brush) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            FontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Color Darker(Color color, double factor) =>
        Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
