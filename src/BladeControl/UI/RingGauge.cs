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

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // The ring is open at the bottom: 300 degrees of it can fill.
    private const double Sweep = 300;
    private const double StartAngle = 120; // Bottom left, clockwise from three o'clock.

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

        if (shown > 0.005)
        {
            drawing.DrawGeometry(null, new Pen(ArcBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Arc(center, radius, StartAngle, Sweep * shown));
        }
    }

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
