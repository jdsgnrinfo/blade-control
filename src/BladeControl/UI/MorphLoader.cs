using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BladeControl.UI;

/// <summary>What a <see cref="MorphLoader"/> shows: the job running, or how it ended.</summary>
public enum LoaderStatus
{
    Loading,
    Success,
    Error
}

/// <summary>
/// A small loader for a job that ends well or badly in the same spot: three
/// green dots bouncing in a wave while it runs, which gather into the middle
/// and give way to a green check when it worked or a red cross when it did
/// not, drawn stroke by stroke with a short pop. Drawn on a 24 grid, so it
/// scales to any size. With Windows' animation effects off the dots stand
/// still and the marks appear at once.
/// </summary>
public sealed class MorphLoader : Viewbox
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(LoaderStatus), typeof(MorphLoader), new PropertyMetadata(LoaderStatus.Loading, OnStatusChanged));

    private static readonly Duration Bounce = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan Wave = TimeSpan.FromMilliseconds(110);
    private static readonly Duration Fold = TimeSpan.FromMilliseconds(160);
    private static readonly Duration Draw = TimeSpan.FromMilliseconds(200);
    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Sway = new SineEase { EasingMode = EasingMode.EaseInOut };

    private const double Stroke = 2.5;
    private static readonly double[] DotCenters = [6, 12, 18];

    private readonly Ellipse[] _dots = new Ellipse[3];
    private readonly TranslateTransform[] _lifts = new TranslateTransform[3];
    private readonly Path _check;
    private readonly Path[] _cross;
    private readonly ScaleTransform _pop = new();

    public MorphLoader()
    {
        Width = 24;
        Height = 24;
        Stretch = Stretch.Uniform;

        var canvas = new Canvas
        {
            Width = 24,
            Height = 24,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _pop
        };

        for (var index = 0; index < 3; index++)
        {
            _lifts[index] = new TranslateTransform();
            var dot = _dots[index] = new Ellipse { Width = 4, Height = 4, RenderTransform = _lifts[index] };
            dot.SetResourceReference(Shape.FillProperty, "Brush.Accent");
            Canvas.SetLeft(dot, DotCenters[index] - 2);
            Canvas.SetTop(dot, 10);
            canvas.Children.Add(dot);
        }

        _check = Mark("M5,12.5 L10,17.5 L19,7.5", "Brush.Accent");
        _cross = [Mark("M7,7 L17,17", "Brush.Warm"), Mark("M17,7 L7,17", "Brush.Warm")];
        canvas.Children.Add(_check);
        canvas.Children.Add(_cross[0]);
        canvas.Children.Add(_cross[1]);

        Child = canvas;
        IsVisibleChanged += (_, _) => Show(animate: false);
    }

    public LoaderStatus Status
    {
        get => (LoaderStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private static void OnStatusChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((MorphLoader)sender).Show(animate: true);

    // A stroke of a mark, hidden until its turn; its dashes let it be drawn from one end.
    private static Path Mark(string data, string brush)
    {
        var mark = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = Stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0
        };
        mark.SetResourceReference(Shape.StrokeProperty, brush);
        return mark;
    }

    private void Show(bool animate)
    {
        var still = Motion.Reduced || !IsVisible;

        if (Status == LoaderStatus.Loading)
            ShowDots(still);
        else
            ShowMark(Status == LoaderStatus.Success ? [_check] : _cross, still || !animate);
    }

    // The dots come back to their places, then bounce one after another, for as long as they are seen.
    private void ShowDots(bool still)
    {
        _pop.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _pop.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        foreach (var mark in (Path[])[_check, .. _cross])
        {
            mark.BeginAnimation(OpacityProperty, null);
            mark.Opacity = 0;
        }

        for (var index = 0; index < 3; index++)
        {
            var dot = _dots[index];
            var lift = _lifts[index];
            dot.BeginAnimation(OpacityProperty, null);
            dot.Opacity = 1;
            lift.BeginAnimation(TranslateTransform.XProperty, null);
            lift.X = 0;
            lift.BeginAnimation(TranslateTransform.YProperty, null);

            if (still || !IsVisible)
                continue;

            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -4, Bounce)
            {
                BeginTime = TimeSpan.FromTicks(Wave.Ticks * index),
                EasingFunction = Sway,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
    }

    // The dots gather into the middle and fade, the mark's strokes are drawn in turn, and it pops once.
    private void ShowMark(Path[] strokes, bool still)
    {
        for (var index = 0; index < 3; index++)
        {
            var lift = _lifts[index];
            lift.BeginAnimation(TranslateTransform.YProperty, null);
            lift.Y = 0;

            if (still)
            {
                _dots[index].BeginAnimation(OpacityProperty, null);
                _dots[index].Opacity = 0;
                continue;
            }

            lift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(12 - DotCenters[index], Fold) { EasingFunction = Out });
            _dots[index].BeginAnimation(OpacityProperty, new DoubleAnimation(0, Fold) { EasingFunction = Out });
        }

        foreach (var mark in (Path[])[_check, .. _cross])
        {
            mark.BeginAnimation(OpacityProperty, null);
            mark.Opacity = 0;
        }

        for (var index = 0; index < strokes.Length; index++)
        {
            var mark = strokes[index];

            if (still)
            {
                mark.StrokeDashArray = null;
                mark.Opacity = 1;
                continue;
            }

            // Dash lengths count in stroke widths: one dash as long as the stroke, then a gap as long.
            // The stroke stays unseen until its turn, so its round cap does not show as a dot before.
            var length = Length(mark.Data) / Stroke + 1;
            var begin = Fold.TimeSpan + TimeSpan.FromTicks(Draw.TimeSpan.Ticks * index);
            mark.StrokeDashArray = [length, length];
            mark.StrokeDashOffset = length;
            mark.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(length, 0, Draw) { BeginTime = begin, EasingFunction = Out });

            var appear = new DoubleAnimationUsingKeyFrames();
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(begin + TimeSpan.FromMilliseconds(16))));
            mark.BeginAnimation(OpacityProperty, appear);
        }

        if (still)
            return;

        var popAt = Fold.TimeSpan + TimeSpan.FromTicks(Draw.TimeSpan.Ticks * strokes.Length);
        var pop = new DoubleAnimationUsingKeyFrames { BeginTime = popAt - TimeSpan.FromMilliseconds(80) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), Out));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), Sway));
        _pop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _pop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    // The length of a mark's straight segments.
    private static double Length(Geometry geometry)
    {
        var total = 0.0;

        foreach (var figure in geometry.GetFlattenedPathGeometry().Figures)
        {
            var from = figure.StartPoint;

            foreach (var segment in figure.Segments)
            {
                var points = segment switch
                {
                    PolyLineSegment line => line.Points,
                    LineSegment line => [line.Point],
                    _ => []
                };

                foreach (var point in points)
                {
                    total += (point - from).Length;
                    from = point;
                }
            }
        }

        return total;
    }
}
