using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BladeControl.UI;

/// <summary>
/// A ring with an arc that goes round as far as <see cref="Fraction"/> says,
/// starting at the bottom left, as the concept's temperature gauges do. The arc
/// eases to each new reading instead of jumping.
/// </summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(RingGauge), new PropertyMetadata(0.0, OnFractionChanged));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
        nameof(ArcBrush), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>With an end brush, the arc fades from ArcBrush at its start to this at its tip.</summary>
    public static readonly DependencyProperty ArcEndBrushProperty = DependencyProperty.Register(
        nameof(ArcEndBrush), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SweepProperty = DependencyProperty.Register(
        nameof(Sweep), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How much of the circle the ring covers, open at the bottom: 300 degrees by default, 270 for three quarters.</summary>
    public double Sweep
    {
        get => (double)GetValue(SweepProperty);
        set => SetValue(SweepProperty, value);
    }

    // Bottom left, clockwise from three o'clock, so the gap is centred at the bottom.
    private double StartAngle => 90 + (360 - Sweep) / 2;

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush ArcBrush
    {
        get => (Brush)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    public Brush? ArcEndBrush
    {
        get => (Brush?)GetValue(ArcEndBrushProperty);
        set => SetValue(ArcEndBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    private static void OnFractionChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var ring = (RingGauge)element;
        var animation = new DoubleAnimation(Math.Clamp((double)e.NewValue, 0, 1), TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ring.BeginAnimation(ShownProperty, animation);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var size = Math.Min(ActualWidth, ActualHeight);

        if (size <= Thickness * 2)
            return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - Thickness / 2;

        drawing.DrawGeometry(null, new Pen(TrackBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            Arc(center, radius, StartAngle, Sweep));

        var shown = (double)GetValue(ShownProperty);

        if (shown <= 0.005)
            return;

        if (ArcBrush is SolidColorBrush { Color: var from } && ArcEndBrush is SolidColorBrush { Color: var to } && from != to)
        {
            DrawFading(drawing, center, radius, Sweep * shown, from, to);
            return;
        }

        drawing.DrawGeometry(null, new Pen(ArcBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            Arc(center, radius, StartAngle, Sweep * shown));
    }

    // The arc in short pieces, each a step nearer the end color, so it reads as one gradient
    // along the ring; round ends in the first and last color.
    private void DrawFading(DrawingContext drawing, Point center, double radius, double sweep, Color from, Color to)
    {
        var pieces = Math.Max(2, (int)Math.Ceiling(sweep / 4));
        var piece = sweep / pieces;

        for (var index = 0; index < pieces; index++)
        {
            var color = Blend(from, to, (index + 0.5) / pieces);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            // Each piece overlaps the next a little, so no seam shows between them.
            var length = Math.Min(piece + 0.6, sweep - index * piece);
            drawing.DrawGeometry(null, new Pen(brush, Thickness), Arc(center, radius, StartAngle + index * piece, length));
        }

        DrawCap(drawing, center, radius, StartAngle, from);
        DrawCap(drawing, center, radius, StartAngle + sweep, to);
    }

    private void DrawCap(DrawingContext drawing, Point center, double radius, double degrees, Color color)
    {
        var radians = degrees * Math.PI / 180;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        drawing.DrawEllipse(brush, null, new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians)), Thickness / 2, Thickness / 2);
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        static Point At(Point center, double radius, double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        var figure = new PathFigure { StartPoint = At(center, radius, startDegrees), IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            At(center, radius, startDegrees + sweepDegrees),
            new Size(radius, radius),
            0,
            sweepDegrees > 180,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
