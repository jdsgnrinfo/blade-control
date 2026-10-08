using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BladeControl.UI;

/// <summary>
/// The app's slider, as Razer Helper's: a round-ended track filling in green,
/// a dot at each step when there are few, and a white rounded-square thumb
/// with a soft shadow. It moves on springs: the thumb follows the pointer exactly
/// and grows a little while held; a click on the track sends it over from
/// where it was; a release with speed carries it at most a step further
/// before it settles; past either end it gives like rubber; a key at an end
/// makes it strain and spring back. A green bubble springs up over the thumb
/// with the value while it moves, and goes a moment after. With Windows'
/// animation effects off it moves without any of that.
/// </summary>
/// <remarks>
/// <see cref="RangeBase.Value"/> changes on every step while dragging;
/// <see cref="Committed"/> fires once when the user lets go (or a key that
/// moved it is released), which is the moment to act on the value. The
/// bubble is drawn above the control, over whatever is there.
/// </remarks>
public sealed class SpringSlider : RangeBase
{
    private const double ThumbRadius = 9;   // Half the thumb's width; it is 18 by 22, as before.
    private const double ThumbHalfHeight = 11;
    private const double ThumbCorner = 3;
    private const double TrackHeight = 6;
    private const double Inset = 16;       // Room at each end for the grown thumb and its stretch.
    private const double Stretch = 5;      // How far past an end the thumb can be pulled.
    private const double Push = 150;       // A key's push at an end.
    private const double CarryStep = 12;   // A release carries a step further only when steps are this far apart.
    private const int MostDots = 20;
    private const double Lifted = 1.16;
    private const int BubbleLinger = 700;
    private const double PillHeight = 26;
    private const double PointerSize = 5;
    private const double PillPadding = 10;

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(SpringSlider), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(SpringSlider), new PropertyMetadata("{0}"));

    private readonly Spring _position = new(0, 0.1);
    private readonly Spring _catchUp = new(0, 0.1);
    private readonly Spring _lift = new(1, 0.002);
    private readonly Spring _bubble = new(0, 0.002);
    private readonly List<(double Time, double Position)> _trail = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _bubbleLinger = new() { Interval = TimeSpan.FromMilliseconds(BubbleLinger) };

    private bool _dragging;
    private bool _hovered;
    private bool _keyFocus;
    private bool _keyMovedValue;
    private bool _changing;
    private bool _placed;
    private bool _framing;
    private double _pointer;
    private double _grab;
    private double _lastFrame;

    static SpringSlider()
    {
        FocusableProperty.OverrideMetadata(typeof(SpringSlider), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(SpringSlider), new FrameworkPropertyMetadata(null));
        IsEnabledProperty.OverrideMetadata(typeof(SpringSlider), new UIPropertyMetadata(true, (sender, _) => ((SpringSlider)sender).OnAvailabilityChanged()));
        MaximumProperty.OverrideMetadata(typeof(SpringSlider), new FrameworkPropertyMetadata(100.0));
    }

    public SpringSlider()
    {
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;

        _bubbleLinger.Tick += (_, _) =>
        {
            _bubbleLinger.Stop();

            if (!_dragging)
                HideBubble();
        };

        SizeChanged += (_, _) => Settle();
        Unloaded += (_, _) => StopFrames();
    }

    /// <summary>The steps the value snaps to.</summary>
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>How the bubble writes the value: "{0}%", "{0}".</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    /// <summary>Once the user lets go, or a key that moved it is released.</summary>
    public event EventHandler? Committed;

    protected override Size MeasureOverride(Size constraint) => new(0, 28);

    // ===== Value =====

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);

        if (_changing)
            return;

        // Set from outside (a refresh, a preset): the thumb glides there, unless the user holds it.
        if (_dragging || _keyMovedValue)
            return;

        if (!IsLoaded || !IsVisible)
        {
            Settle();
            return;
        }

        _position.Jump(ThumbPosition);
        _position.To(XAt(Value), Spring.Snappy);
        StartFrames();
    }

    // Takes the step nearest the value, leaving the thumb where it is; true if it changed.
    private bool ChangeValue(double value)
    {
        var snapped = Snap(value);

        if (snapped == Value)
            return false;

        _changing = true;
        Value = snapped;
        _changing = false;
        InvalidateVisual();
        return true;
    }

    // ===== Mouse =====

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var x = e.GetPosition(this).X;
        var current = ThumbPosition; // Read before the drag starts; from then on it follows the pointer.

        Focus();
        CaptureMouse();
        _keyFocus = false;
        _dragging = true;
        _trail.Clear();
        _trail.Add((Now, x));

        // Grabbing the thumb keeps it where it is under the pointer; a click on the track springs it over from where it was.
        if (Math.Abs(x - ShownX(current)) <= ThumbRadius + 2)
        {
            _grab = current - x;
            _catchUp.Jump(0);
        }
        else
        {
            _grab = 0;
            _catchUp.Jump(current - x);
            _catchUp.To(0, Spring.Snappy);
        }

        _position.Jump(current);
        _pointer = x + _grab;
        _lift.To(Lifted, Spring.Snappy);
        FollowPointer();
        ShowBubble();
        StartFrames();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var x = e.GetPosition(this).X;

        if (!_dragging)
        {
            var hovered = IsEnabled && Math.Abs(x - ShownX(ThumbPosition)) <= ThumbRadius + 4;

            if (hovered != _hovered)
            {
                _hovered = hovered;
                InvalidateVisual();
            }

            return;
        }

        var now = Now;
        _trail.Add((now, x));
        _trail.RemoveAll(point => now - point.Time > 80 && _trail.Count > 2);
        _pointer = x + _grab;
        FollowPointer();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hovered)
        {
            _hovered = false;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragging)
            Release();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        if (_dragging)
            Release();
    }

    private void Release()
    {
        var from = ThumbPosition;
        var velocity = SliderPhysics.ReleaseVelocity(_trail, Now) + _catchUp.Velocity;
        var stepWidth = TrackWidth * Step / Math.Max(Step, Maximum - Minimum);

        // On a coarse grid a quick release carries the thumb one step further, never more.
        var carry = stepWidth >= CarryStep && !Motion.Reduced ? Math.Clamp(SliderPhysics.Project(velocity), -stepWidth, stepWidth) : 0;

        _dragging = false;

        if (IsMouseCaptured)
            ReleaseMouseCapture();

        _catchUp.Jump(0);
        _position.Jump(from);
        ChangeValue(ValueAt(Math.Clamp(from, XAt(Minimum), XAt(Maximum)) + carry));
        _position.To(XAt(Value), Spring.Snappy, velocity);
        _lift.To(1, Spring.Snappy);
        StartFrames();
        LetBubbleGo();
        Committed?.Invoke(this, EventArgs.Empty);
    }

    // ===== Keys =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEnabled)
            return;

        var largeStep = Math.Max(Step, Snap(Minimum + (Maximum - Minimum) / 10) - Minimum);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? largeStep : Step;

        var (target, push) = e.Key switch
        {
            Key.Left or Key.Down => (Value - step, -1),
            Key.Right or Key.Up => (Value + step, 1),
            Key.PageDown => (Value - largeStep, -1),
            Key.PageUp => (Value + largeStep, 1),
            Key.Home => (Minimum, 0),
            Key.End => (Maximum, 0),
            _ => ((double?)null, 0)
        };

        if (target is null)
            return;

        e.Handled = true;
        _keyFocus = true;

        if (Snap(target.Value) == Value && push != 0)
        {
            // At an end the thumb strains towards the key and springs home, so the key still answers.
            _position.To(XAt(Value), Spring.Morph, push * Push);
        }
        else
        {
            var before = Value;
            _position.Jump(ThumbPosition);
            ChangeValue(target.Value);
            _position.To(XAt(Value), Spring.Snappy);
            _keyMovedValue |= Value != before;
        }

        StartFrames();
        ShowBubble();
        LetBubbleGo(BubbleLinger * 2);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        // Only a key that actually moved the slider is worth acting on; Tab and friends are not.
        if (!_keyMovedValue)
            return;

        _keyMovedValue = false;
        Committed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _keyFocus = false;
        InvalidateVisual();
    }

    private void OnAvailabilityChanged()
    {
        _dragging = false;
        _hovered = false;
        Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
        HideBubble();
        Settle();
    }

    // ===== Drawing =====

    protected override void OnRender(DrawingContext context)
    {
        if (ActualWidth <= Inset * 2)
            return;

        var accent = Resource("Brush.Accent", Brushes.LimeGreen);
        var track = Resource("Brush.Track", Brushes.DimGray);
        var live = IsEnabled;

        // The whole height takes clicks, not just the track.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var thumbX = ShownX(ThumbPosition);
        var start = XAt(Minimum);
        var end = XAt(Maximum);
        var y = TrackY;

        // The track, round-ended, then the filled part up to the thumb.
        Pill(context, track, new Rect(start - TrackHeight / 2, y - TrackHeight / 2, end - start + TrackHeight, TrackHeight));
        Pill(context, live ? accent : DisabledFill, new Rect(start - TrackHeight / 2, y - TrackHeight / 2, Math.Max(0, thumbX - start + TrackHeight / 2), TrackHeight));

        // A dot at each step, when they are few enough to tell apart.
        if (Step > 0 && (Maximum - Minimum) / Step <= MostDots)
        {
            for (var value = Minimum; value <= Maximum + Step / 2; value += Step)
            {
                var x = XAt(value);
                var filled = live && x < thumbX - 4;
                context.DrawEllipse(filled ? FilledDot : Dot, null, new Point(x, y), 2, 2);
            }
        }

        // The thumb: a soft halo while pointed at, a green ring after a key, a shadow, and the white rounded square.
        var halfWidth = ThumbRadius * _lift.Position;
        var halfHeight = ThumbHalfHeight * _lift.Position;
        var center = new Point(thumbX, y);

        if (_keyFocus && IsKeyboardFocused)
        {
            Thumb(context, accent, center, halfWidth + 4, halfHeight + 4);
            Thumb(context, Resource("Brush.Card", Brushes.Black) is SolidColorBrush card ? card : CardFallback, center, halfWidth + 2, halfHeight + 2);
        }
        else if (live && (_hovered || _dragging))
        {
            Thumb(context, Halo, center, halfWidth + 3, halfHeight + 3);
        }

        Thumb(context, Shadow, new Point(center.X, center.Y + 1), halfWidth, halfHeight);
        Thumb(context, live ? Brushes.White : DisabledThumb, center, halfWidth, halfHeight);

        DrawBubble(context, accent, thumbX, y - halfHeight - 4);
    }

    // A green pill with the value in dark text and a small point down to the thumb, springing up as it appears.
    private void DrawBubble(DrawingContext context, Brush accent, double x, double tipY)
    {
        var appear = _bubble.Position;
        var opacity = Math.Clamp(appear * 2.2, 0, 1);

        if (opacity <= 0)
            return;

        var text = new FormattedText(
            string.Format(CultureInfo.CurrentCulture, Format ?? "{0}", Value),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            14, Resource("Brush.OnAccent", Brushes.Black), VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var width = text.WidthIncludingTrailingWhitespace + 2 * PillPadding;

        // The pill stays over the slider; its point stays on the thumb.
        var shift = Math.Clamp(x - width / 2, 0, Math.Max(0, ActualWidth - width)) - (x - width / 2);
        var scale = 0.6 + 0.4 * appear;

        context.PushOpacity(opacity);
        context.PushTransform(new TranslateTransform(x, tipY + (1 - Math.Min(appear, 1)) * 6));
        context.PushTransform(new ScaleTransform(scale, scale));

        var pill = new Rect(-width / 2 + shift, -PointerSize - PillHeight, width, PillHeight);
        context.DrawRoundedRectangle(accent, null, pill, PillHeight / 2, PillHeight / 2);

        var point = new StreamGeometry();
        using (var draw = point.Open())
        {
            draw.BeginFigure(new Point(-PointerSize, -PointerSize - 1), isFilled: true, isClosed: true);
            draw.LineTo(new Point(PointerSize, -PointerSize - 1), false, false);
            draw.LineTo(new Point(0, 0), false, false);
        }
        point.Freeze();
        context.DrawGeometry(accent, null, point);

        context.DrawText(text, new Point(pill.X + (pill.Width - text.Width) / 2, pill.Y + (pill.Height - text.Height) / 2));

        context.Pop();
        context.Pop();
        context.Pop();
    }

    // The thumb's shape at a size: a rounded square, its corners growing with its rings.
    private static void Thumb(DrawingContext context, Brush brush, Point center, double halfWidth, double halfHeight)
    {
        var corner = ThumbCorner + (halfHeight - ThumbHalfHeight > 0 ? (halfHeight - ThumbHalfHeight) * 0.5 : 0);
        context.DrawRoundedRectangle(brush, null, new Rect(center.X - halfWidth, center.Y - halfHeight, 2 * halfWidth, 2 * halfHeight), corner, corner);
    }

    private static void Pill(DrawingContext context, Brush brush, Rect bounds)
    {
        if (bounds.Width <= 0)
            return;

        context.DrawRoundedRectangle(brush, null, bounds, bounds.Height / 2, bounds.Height / 2);
    }

    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static readonly Brush DisabledFill = Frozen(new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)));
    private static readonly Brush DisabledThumb = Frozen(new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xB4)));
    private static readonly Brush Dot = Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)));
    private static readonly Brush FilledDot = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x5C, 0x12)));
    private static readonly Brush Halo = Frozen(new SolidColorBrush(Color.FromArgb(46, 0x44, 0xD6, 0x2C)));
    private static readonly Brush Shadow = Frozen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)));
    private static readonly Brush CardFallback = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)));

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    // ===== Where things are =====

    private double Now => _clock.Elapsed.TotalMilliseconds;

    private double TrackY => ActualHeight / 2;

    private double TrackWidth => Math.Max(1, ActualWidth - Inset * 2);

    private double XAt(double value) =>
        Inset + (Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0) * TrackWidth;

    private double ValueAt(double x) =>
        Snap(Minimum + Math.Clamp((x - Inset) / TrackWidth, 0, 1) * (Maximum - Minimum));

    private double Snap(double value)
    {
        var clamped = Math.Clamp(value, Minimum, Maximum);
        return Step > 0 ? Minimum + Math.Round((clamped - Minimum) / Step, MidpointRounding.AwayFromZero) * Step : clamped;
    }

    // Where the thumb is now, before any stretch past the ends.
    private double ThumbPosition
    {
        get
        {
            if (_dragging)
                return _pointer + _catchUp.Position;

            if (!_placed)
            {
                _position.Jump(XAt(Value));
                _placed = true;
            }

            return _position.Position;
        }
    }

    // Past an end, the thumb gives less and less.
    private double ShownX(double x)
    {
        var start = XAt(Minimum);
        var end = XAt(Maximum);

        return x < start ? start + SliderPhysics.Rubber(x - start, Stretch)
            : x > end ? end + SliderPhysics.Rubber(x - end, Stretch)
            : x;
    }

    // While dragging, the value takes the step nearest the thumb.
    private void FollowPointer()
    {
        ChangeValue(ValueAt(Math.Clamp(ThumbPosition, XAt(Minimum), XAt(Maximum))));
        InvalidateVisual();
    }

    private void Settle()
    {
        _position.Jump(XAt(Value));
        _catchUp.Jump(0);
        _lift.Jump(1);
        _placed = true;
        InvalidateVisual();
    }

    // ===== The bubble =====

    private void ShowBubble()
    {
        _bubbleLinger.Stop();
        _bubble.To(1, Spring.Snappy);
        StartFrames();
    }

    private void HideBubble()
    {
        _bubble.To(0, Spring.Snappy);
        StartFrames();
    }

    // The bubble stays a moment after the thumb stops, then goes.
    private void LetBubbleGo(int milliseconds = BubbleLinger)
    {
        _bubbleLinger.Stop();
        _bubbleLinger.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _bubbleLinger.Start();
    }

    // ===== Frames =====

    private void StartFrames()
    {
        _lastFrame = _clock.Elapsed.TotalSeconds;

        if (!_framing)
        {
            _framing = true;
            CompositionTarget.Rendering += OnFrame;
        }

        InvalidateVisual();
    }

    private void StopFrames()
    {
        if (!_framing)
            return;

        _framing = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var seconds = Math.Min(0.032, now - _lastFrame);
        _lastFrame = now;

        var moving = _position.Step(seconds) | _catchUp.Step(seconds) | _lift.Step(seconds) | _bubble.Step(seconds);

        if (_dragging)
            FollowPointer();

        InvalidateVisual();

        if (!moving && !_dragging)
            StopFrames();
    }
}
