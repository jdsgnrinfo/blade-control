using System.Windows;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>
/// A line over time, with a soft fill under it and faint guides at a quarter,
/// half and three quarters. <see cref="Points"/> are fractions of the width
/// (time) and of the height (value), both 0 to 1; a gap in the readings is a
/// gap in the line.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<Point>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideBrushProperty = DependencyProperty.Register(
        nameof(GuideBrush), typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A reading further from the one before than this (as a fraction of the width) starts a new stretch of line.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(0.1, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Point>? Points
    {
        get => (IReadOnlyList<Point>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush GuideBrush
    {
        get => (Brush)GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 0 || height <= 0)
            return;

        // The background takes the mouse, so the tooltip shows anywhere over it.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var guide = new Pen(GuideBrush, 1) { DashStyle = new DashStyle([3, 4], 0) };
        guide.Freeze();

        foreach (var level in new[] { 0.25, 0.5, 0.75 })
        {
            var y = Math.Round(height * (1 - level)) + 0.5;
            context.DrawLine(guide, new Point(0, y), new Point(width, y));
        }

        var baseline = new Pen(GuideBrush, 1);
        baseline.Freeze();
        context.DrawLine(baseline, new Point(0, height - 0.5), new Point(width, height - 0.5));

        if (Points is not { Count: > 0 } points)
            return;

        var line = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        line.Freeze();

        var fill = FillFor(LineBrush);

        foreach (var stretch in Stretches(points))
        {
            var at = stretch.Select(point => new Point(point.X * width, (1 - Math.Clamp(point.Y, 0, 1)) * (height - 2) + 1)).ToList();

            if (at.Count == 1)
            {
                context.DrawEllipse(LineBrush, null, at[0], 2.5, 2.5);
                continue;
            }

            var area = new StreamGeometry();
            using (var draw = area.Open())
            {
                draw.BeginFigure(new Point(at[0].X, height), isFilled: true, isClosed: true);
                draw.PolyLineTo([.. at, new Point(at[^1].X, height)], isStroked: false, isSmoothJoin: false);
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
            context.DrawGeometry(null, line, path);
        }
    }

    // The readings in runs with no gap wider than Gap between them.
    private IEnumerable<List<Point>> Stretches(IReadOnlyList<Point> points)
    {
        var run = new List<Point> { points[0] };

        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].X - points[i - 1].X > Gap)
            {
                yield return run;
                run = [];
            }

            run.Add(points[i]);
        }

        yield return run;
    }

    // The line's color fading to nothing at the bottom.
    private static Brush FillFor(Brush line)
    {
        var color = line is SolidColorBrush solid ? solid.Color : Colors.LimeGreen;
        var fill = new LinearGradientBrush(Color.FromArgb(70, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        fill.Freeze();
        return fill;
    }
}
