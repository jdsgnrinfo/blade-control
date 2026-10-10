using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>
/// What the Switch style draws: the app's pill-shaped track (dark, green when on)
/// and round knob (grey off, white on), moving as Razer Helper's
/// switch does. Pressing (the mouse or Space) stretches the knob towards the
/// other side, keeping its own edge where it is; letting go sends it across
/// on a spring that lands without overshooting, while the green fades in or
/// out. A change that came without a press (Enter, or the app setting it)
/// gives the knob a brief stretch as it travels instead. With Windows'
/// animation effects off it just changes.
/// </summary>
public sealed class SwitchVisual : FrameworkElement
{
    private const double TrackWidth = 46;
    private const double TrackHeight = 24;
    private const double TrackCorner = TrackHeight / 2;
    private const double Inset = 3;
    private const double KnobSize = 18;
    private const double KnobCorner = KnobSize / 2;
    private const double PressStretch = 5;   // How far the knob widens towards the other side while pressed.
    private const double PulseSeconds = 0.22; // The stretch a change without a press gives: up to 16% wider and back.
    private const double PulseWidth = 0.16;
    private const double FadeSeconds = 0.16;
    private const double PressMemory = 0.25;  // A change this soon after a press already had its stretch.

    // Lands on its end without overshooting the state it reports.
    private static readonly Spring.Feel Glide = Spring.Feel.Of(0.18, 0);

    // The press stretch, as quick as the travel.
    private static readonly Spring.Feel Stretchy = Spring.Feel.Of(0.18, 0.15);

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool?), typeof(SwitchVisual), new PropertyMetadata(false, (sender, _) => ((SwitchVisual)sender).OnIsOnChanged()));

    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed), typeof(bool), typeof(SwitchVisual), new PropertyMetadata(false, (sender, _) => ((SwitchVisual)sender).OnIsPressedChanged()));

    public static readonly DependencyProperty IsHoveredProperty = DependencyProperty.Register(
        nameof(IsHovered), typeof(bool), typeof(SwitchVisual), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HasKeyboardFocusProperty = DependencyProperty.Register(
        nameof(HasKeyboardFocus), typeof(bool), typeof(SwitchVisual), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    // 0 is fully off, 1 fully on, in between while the knob travels.
    private readonly Spring _travel = new(0, 0.001);
    // Pixels the pressed knob has widened by.
    private readonly Spring _stretch = new(0, 0.05);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _framing;
    private double _lastFrame;
    private double _releasedAt = double.NegativeInfinity;
    private double _pulseAt = double.NegativeInfinity;
    private double _fadeAt = double.NegativeInfinity;
    private double _fadeFrom;
    private double _green;

    public SwitchVisual()
    {
        Unloaded += (_, _) => StopFrames();
    }

    /// <summary>The switch's IsChecked.</summary>
    public bool? IsOn { get => (bool?)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }

    /// <summary>Held down by the mouse or Space.</summary>
    public bool IsPressed { get => (bool)GetValue(IsPressedProperty); set => SetValue(IsPressedProperty, value); }

    public bool IsHovered { get => (bool)GetValue(IsHoveredProperty); set => SetValue(IsHoveredProperty, value); }

    public bool HasKeyboardFocus { get => (bool)GetValue(HasKeyboardFocusProperty); set => SetValue(HasKeyboardFocusProperty, value); }

    private double Now => _clock.Elapsed.TotalSeconds;

    private bool On => IsOn == true;

    protected override Size MeasureOverride(Size availableSize) => new(TrackWidth, TrackHeight);

    private void OnIsOnChanged()
    {
        var target = On ? 1 : 0;

        // Before it is on screen (a saved setting being loaded) there is nothing to watch.
        if (!IsLoaded || !IsVisible || Motion.Reduced)
        {
            _travel.Jump(target);
            _green = target;
            _fadeAt = double.NegativeInfinity;
            InvalidateVisual();
            return;
        }

        _travel.To(target, Glide);
        _fadeFrom = _green;
        _fadeAt = Now;

        // A press already stretched the knob; anything else gets the travelling stretch.
        if (Now - _releasedAt > PressMemory)
            _pulseAt = Now;

        StartFrames();
    }

    private void OnIsPressedChanged()
    {
        // Marked as the press ends, which is before the switch toggles.
        if (!IsPressed)
            _releasedAt = Now;

        if (!IsEnabled || Motion.Reduced)
        {
            _stretch.Jump(0);
            InvalidateVisual();
            return;
        }

        _stretch.To(IsPressed ? PressStretch : 0, Stretchy);
        StartFrames();
    }

    protected override void OnRender(DrawingContext context)
    {
        var track = new Rect((ActualWidth - TrackWidth) / 2, (ActualHeight - TrackHeight) / 2, TrackWidth, TrackHeight);
        var accent = ColorOf("Color.Accent", Colors.LimeGreen);
        var off = ColorOf("Color.Track", System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3A));

        // Only keyboard focus (Tab) shows a ring: green, with a gap, as on the sliders.
        if (HasKeyboardFocus && InputManager.Current.MostRecentInputDevice is KeyboardDevice)
        {
            var ring = Inflate(track, 3);
            context.DrawRoundedRectangle(new SolidColorBrush(accent), null, ring, TrackCorner + 3, TrackCorner + 3);
            context.DrawRoundedRectangle(TryFindResource("Brush.Card") as Brush ?? Brushes.Black, null, Inflate(track, 1.5), TrackCorner + 1.5, TrackCorner + 1.5);
        }

        // The off fill, a little lighter under the pointer, with the green faded in over it.
        var green = Math.Clamp(_green, 0, 1);
        var offFill = IsHovered ? Lighter(off, 13) : off;
        context.DrawRoundedRectangle(new SolidColorBrush(Blend(offFill, accent, green)), null, track, TrackCorner, TrackCorner);

        // The knob: pressed, it widens towards the other side and keeps its own
        // edge; on its way it can stretch a little about its middle.
        var travel = TrackWidth - 2 * Inset - KnobSize;
        var stretch = _stretch.Position;
        var position = Math.Clamp(_travel.Position, 0, 1);
        var width = KnobSize + stretch;
        var left = track.Left + Inset + position * (travel - stretch);
        var pulse = Pulse();

        if (pulse > 0)
        {
            var grown = width * (1 + pulse);
            left -= (grown - width) / 2;
            width = grown;
        }

        var knob = new Rect(left, track.Top + Inset, width, KnobSize);
        var knobColor = Blend(ColorOf("Color.TextMuted", System.Windows.Media.Color.FromRgb(0x8C, 0x8C, 0x8C)), Colors.White, green);

        if (green > 0)
            context.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(70 * green), 0, 0, 0)), null,
                new Rect(knob.X, knob.Y + 1, knob.Width, knob.Height), KnobCorner, KnobCorner);

        context.DrawRoundedRectangle(new SolidColorBrush(knobColor), null, knob, KnobCorner, KnobCorner);
    }

    // The travelling stretch: out quickly, then back.
    private double Pulse()
    {
        var t = (Now - _pulseAt) / PulseSeconds;

        if (t < 0 || t >= 1)
            return 0;

        return t < 0.4
            ? PulseWidth * (1 - Math.Pow(1 - t / 0.4, 2))
            : PulseWidth * (1 - Ease((t - 0.4) / 0.6));
    }

    private void StartFrames()
    {
        if (!_framing)
        {
            _framing = true;
            _lastFrame = Now;
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
        var now = Now;
        var seconds = Math.Min(0.032, now - _lastFrame);
        _lastFrame = now;

        var moving = _travel.Step(seconds) | _stretch.Step(seconds);

        // The green crossfades on its own clock, easing in and out.
        var fade = (now - _fadeAt) / FadeSeconds;
        var target = On ? 1.0 : 0.0;
        _green = fade >= 1 ? target : _fadeFrom + (target - _fadeFrom) * Ease(fade);

        InvalidateVisual();

        if (!moving && fade >= 1 && now - _pulseAt >= PulseSeconds)
            StopFrames();
    }

    private Color ColorOf(string key, Color fallback) => TryFindResource(key) is Color color ? color : fallback;

    private static Rect Inflate(Rect rect, double by) => new(rect.X - by, rect.Y - by, rect.Width + 2 * by, rect.Height + 2 * by);

    private static double Ease(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private static Color Lighter(Color color, byte by) =>
        System.Windows.Media.Color.FromRgb((byte)Math.Min(255, color.R + by), (byte)Math.Min(255, color.G + by), (byte)Math.Min(255, color.B + by));

    private static Color Blend(Color from, Color to, double amount) => System.Windows.Media.Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
