using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BladeControl.Core.Services;

namespace BladeControl.UI;

/// <summary>
/// The equalizer: a slider standing up for each band, its gain above it and
/// its frequency below, on a grid from +12 to -12 dB, with a green curve
/// through the thumbs and a faint fill between it and 0 dB. Drag a thumb (or
/// anywhere in its column) to set the band; the mouse wheel moves the band
/// under the pointer a step, and a double click puts it back to 0. With the
/// focus, Left and Right pick a band and Up and Down move it (Page Up and
/// Page Down by 3 dB). Off, the curve turns grey and the bands stop taking
/// changes.
/// </summary>
/// <remarks>
/// <see cref="Gains"/> is replaced (never changed in place) on every step,
/// so a two-way binding sees each one.
/// </remarks>
public sealed class EqualizerGraph : FrameworkElement
{
    private const double GridTop = 30;      // Room for the gains above.
    private const double GridBottomRoom = 30; // Room for the frequencies below.
    private const double ScaleWidth = 44;   // +12, 0 and -12 at the left.
    private const double ThumbHalfWidth = 11; // The thumb is 22 by 14, as before.
    private const double ThumbHalfHeight = 7;
    private const double LargeStep = 3;

    public static readonly DependencyProperty GainsProperty = DependencyProperty.Register(
        nameof(Gains), typeof(IReadOnlyList<double>), typeof(EqualizerGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty AccentProperty = Register<Brush>(nameof(Accent));
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush>(nameof(TrackBrush));
    public static readonly DependencyProperty LineBrushProperty = Register<Brush>(nameof(LineBrush));
    public static readonly DependencyProperty TextBrushProperty = Register<Brush>(nameof(TextBrush));
    public static readonly DependencyProperty MutedBrushProperty = Register<Brush>(nameof(MutedBrush));

    private int _dragging = -1;
    private int _focusBand;

    public EqualizerGraph()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
    }

    /// <summary>The ten bands' gains in dB, low to high.</summary>
    public IReadOnlyList<double>? Gains { get => (IReadOnlyList<double>?)GetValue(GainsProperty); set => SetValue(GainsProperty, value); }

    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    /// <summary>The curve's color while the equalizer is off.</summary>
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }

    private static DependencyProperty Register<T>(string name) =>
        DependencyProperty.Register(name, typeof(T), typeof(EqualizerGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    static EqualizerGraph()
    {
        IsEnabledProperty.OverrideMetadata(typeof(EqualizerGraph), new UIPropertyMetadata(true, (sender, _) => ((UIElement)sender).InvalidateVisual()));
    }

    private double Top => GridTop;

    private double Bottom => Math.Max(Top + 1, ActualHeight - GridBottomRoom);

    private double ColumnWidth => Math.Max(1, (ActualWidth - ScaleWidth) / EqBands.Count);

    private double XOf(int band) => ScaleWidth + ColumnWidth * (band + 0.5);

    private double YOf(double gain) => Top + (EqBands.Range - gain) / (2 * EqBands.Range) * (Bottom - Top);

    private double GainAt(double y) => EqBands.Snap(EqBands.Range - (y - Top) / (Bottom - Top) * 2 * EqBands.Range);

    private double[] Current => Gains is { Count: var count } gains && count == EqBands.Count ? [.. gains] : EqBands.Flat();

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext context)
    {
        if (ActualWidth <= ScaleWidth || Bottom - Top < 10)
            return;

        var gains = Current;
        var accent = IsEnabled ? Accent ?? Brushes.LimeGreen : MutedBrush ?? Brushes.Gray;
        var text = TextBrush ?? Brushes.Gray;
        var lineBrush = LineBrush ?? Brushes.DimGray;
        var accentColor = accent is SolidColorBrush solid ? solid.Color : Colors.LimeGreen;

        // The column under the mouse takes clicks anywhere in it.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // The grid: +12, 0 and -12, with 0 a little stronger.
        var dashed = Frozen(new Pen(lineBrush, 1) { DashStyle = new DashStyle([3, 4], 0) });
        var solidLine = Frozen(new Pen(lineBrush, 1));

        foreach (var (gain, label) in new[] { (EqBands.Range, "+12 dB"), (0.0, "0 dB"), (-EqBands.Range, "-12 dB") })
        {
            var y = Math.Round(YOf(gain)) + 0.5;
            context.DrawLine(gain == 0 ? solidLine : dashed, new Point(ScaleWidth, y), new Point(ActualWidth, y));
            var formatted = Text(label, text, 10, FontWeights.SemiBold);
            context.DrawText(formatted, new Point(ScaleWidth - 8 - formatted.Width, y - formatted.Height / 2));
        }

        // A track standing up for each band.
        var track = TrackBrush ?? Brushes.DimGray;
        for (var band = 0; band < EqBands.Count; band++)
            context.DrawRoundedRectangle(track, null, new Rect(XOf(band) - 2.5, Top - 2.5, 5, Bottom - Top + 5), 2.5, 2.5);

        // The curve, through the thumbs, with a faint fill to 0 dB.
        var points = Enumerable.Range(0, EqBands.Count).Select(band => new Point(XOf(band), YOf(gains[band]))).ToList();
        var zero = YOf(0);

        var area = new StreamGeometry();
        using (var draw = area.Open())
        {
            draw.BeginFigure(new Point(points[0].X, zero), isFilled: true, isClosed: true);
            draw.LineTo(points[0], isStroked: false, isSmoothJoin: false);
            Smooth(draw, points, stroked: false);
            draw.LineTo(new Point(points[^1].X, zero), isStroked: false, isSmoothJoin: false);
        }
        area.Freeze();
        context.DrawGeometry(Frozen(new SolidColorBrush(Color.FromArgb(34, accentColor.R, accentColor.G, accentColor.B))), null, area);

        var curve = new StreamGeometry();
        using (var draw = curve.Open())
        {
            draw.BeginFigure(points[0], isFilled: false, isClosed: false);
            Smooth(draw, points, stroked: true);
        }
        curve.Freeze();
        context.DrawGeometry(null, Frozen(new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round }), curve);

        // Each band: its thumb, its gain above, its frequency below.
        for (var band = 0; band < EqBands.Count; band++)
        {
            var x = XOf(band);
            var y = points[band].Y;
            var held = band == _dragging || (IsKeyboardFocused && band == _focusBand);

            // The track lit from 0 dB to the thumb.
            var from = Math.Min(y, zero);
            var to = Math.Max(y, zero);
            if (to - from > 0.5)
                context.DrawRoundedRectangle(accent, null, new Rect(x - 2.5, from, 5, to - from), 2.5, 2.5);

            // The thumb, as the sliders': a white rounded square with a soft shadow, grown while held,
            // a faint green halo while dragged, a green ring when the keys move it.
            var grow = band == _dragging ? 1.16 : 1.0;
            var halfWidth = ThumbHalfWidth * grow;
            var halfHeight = ThumbHalfHeight * grow;
            var center = new Point(x, y);

            if (IsKeyboardFocused && band == _focusBand && _dragging < 0)
            {
                Thumb(context, accent, center, halfWidth + 4, halfHeight + 4);
                Thumb(context, TryFindResource("Brush.Card") as Brush ?? Brushes.Black, center, halfWidth + 2, halfHeight + 2);
            }
            else if (band == _dragging)
            {
                Thumb(context, Frozen(new SolidColorBrush(Color.FromArgb(46, accentColor.R, accentColor.G, accentColor.B))), center, halfWidth + 3, halfHeight + 3);
            }

            Thumb(context, Frozen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0))), new Point(x, y + 1), halfWidth, halfHeight);
            Thumb(context, IsEnabled ? Brushes.White : MutedBrush ?? Brushes.Gray, center, halfWidth, halfHeight);

            var gain = Text(GainText(gains[band]), held ? accent : Brushes.White, 12, FontWeights.SemiBold);
            context.DrawText(gain, new Point(x - gain.Width / 2, Top - 14 - gain.Height / 2 - 4));

            var frequency = Text(EqBands.Label(EqBands.Frequencies[band]), text, 11, FontWeights.SemiBold);
            context.DrawText(frequency, new Point(x - frequency.Width / 2, Bottom + 10));
        }
    }

    // ===== Mouse and keys =====

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!IsEnabled || BandAt(e.GetPosition(this).X) is not int band)
            return;

        Focus();

        if (e.ClickCount == 2)
        {
            SetGain(band, 0);
            return;
        }

        _dragging = band;
        _focusBand = band;
        CaptureMouse();
        SetGain(band, GainAt(e.GetPosition(this).Y));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging >= 0 && e.LeftButton == MouseButtonState.Pressed)
            SetGain(_dragging, GainAt(e.GetPosition(this).Y));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragging < 0)
            return;

        _dragging = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = -1;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!IsEnabled || BandAt(e.GetPosition(this).X) is not int band)
            return;

        SetGain(band, Current[band] + Math.Sign(e.Delta) * EqBands.Step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEnabled)
            return;

        var gain = Current[_focusBand];

        switch (e.Key)
        {
            case Key.Left:
                _focusBand = Math.Max(0, _focusBand - 1);
                InvalidateVisual();
                break;
            case Key.Right:
                _focusBand = Math.Min(EqBands.Count - 1, _focusBand + 1);
                InvalidateVisual();
                break;
            case Key.Up:
                SetGain(_focusBand, gain + EqBands.Step);
                break;
            case Key.Down:
                SetGain(_focusBand, gain - EqBands.Step);
                break;
            case Key.PageUp:
                SetGain(_focusBand, gain + LargeStep);
                break;
            case Key.PageDown:
                SetGain(_focusBand, gain - LargeStep);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    private int? BandAt(double x) =>
        x < ScaleWidth ? null : Math.Clamp((int)((x - ScaleWidth) / ColumnWidth), 0, EqBands.Count - 1);

    private void SetGain(int band, double gain)
    {
        var gains = Current;
        var snapped = EqBands.Snap(gain);

        if (gains[band] == snapped)
            return;

        gains[band] = snapped;
        Gains = gains;
    }

    // ===== Drawing =====

    // A soft curve through the points (Catmull-Rom as Béziers), kept inside the grid.
    private void Smooth(StreamGeometryContext draw, List<Point> points, bool stroked)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var before = points[Math.Max(0, i - 1)];
            var from = points[i];
            var to = points[i + 1];
            var after = points[Math.Min(points.Count - 1, i + 2)];

            var first = new Point(from.X + (to.X - before.X) / 6, Clamp(from.Y + (to.Y - before.Y) / 6));
            var second = new Point(to.X - (after.X - from.X) / 6, Clamp(to.Y - (after.Y - from.Y) / 6));
            draw.BezierTo(first, second, to, stroked, isSmoothJoin: true);
        }
    }

    private static void Thumb(DrawingContext context, Brush brush, Point center, double halfWidth, double halfHeight)
    {
        var corner = 3 + Math.Max(0, halfHeight - ThumbHalfHeight) * 0.5;
        context.DrawRoundedRectangle(brush, null, new Rect(center.X - halfWidth, center.Y - halfHeight, 2 * halfWidth, 2 * halfHeight), corner, corner);
    }

    private double Clamp(double y) => Math.Clamp(y, Top, Bottom);

    private static string GainText(double gain) =>
        gain switch
        {
            > 0 => "+" + gain.ToString("0.#", CultureInfo.InvariantCulture),
            < 0 => "−" + (-gain).ToString("0.#", CultureInfo.InvariantCulture),
            _ => "0"
        };

    private FormattedText Text(string value, Brush brush, double size, FontWeight weight) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
