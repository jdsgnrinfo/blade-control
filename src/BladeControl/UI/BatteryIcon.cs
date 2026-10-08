using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BladeControl.UI;

/// <summary>
/// The battery as an icon: a rounded body with its tip at the right, filled
/// from the left as far as the charge goes. White on battery, yellow at 20%
/// and under, red at 10% and under; green with a bolt while plugged in. The
/// fill eases to each new reading, and while it charges a soft light runs
/// across it.
/// </summary>
public sealed class BatteryIcon : FrameworkElement
{
    private const double LowBelow = 0.2;
    private const double CriticalBelow = 0.1;

    private static readonly Color Empty = Color.FromRgb(0x4A, 0x4A, 0x4A);
    private static readonly Color Normal = Colors.White;
    private static readonly Color Low = Color.FromRgb(0xD9, 0xA4, 0x00);
    private static readonly Color Critical = Color.FromRgb(0xD9, 0x26, 0x26);
    private static readonly Color Bolt = Color.FromRgb(0x14, 0x14, 0x14);

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(BatteryIcon), new PropertyMetadata(0.0, OnFractionChanged));

    public static readonly DependencyProperty IsPluggedInProperty = DependencyProperty.Register(
        nameof(IsPluggedIn), typeof(bool), typeof(BatteryIcon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnPluggedInChanged));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(BatteryIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    // The fill as drawn, easing to Fraction.
    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(BatteryIcon), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // Where the charging light is, 0 to 1 across the fill.
    private static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
        "Glow", typeof(double), typeof(BatteryIcon), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public BatteryIcon()
    {
        IsVisibleChanged += (_, _) => UpdateGlow();
    }

    /// <summary>The charge, 0 to 1.</summary>
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    public bool IsPluggedIn { get => (bool)GetValue(IsPluggedInProperty); set => SetValue(IsPluggedInProperty, value); }

    /// <summary>The fill while plugged in.</summary>
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(38, 18);

    private static void OnFractionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var icon = (BatteryIcon)sender;
        var target = Math.Clamp((double)e.NewValue, 0, 1);
        icon.UpdateGlow();

        if (Motion.Reduced || !icon.IsLoaded)
        {
            icon.BeginAnimation(ShownProperty, null);
            icon.SetValue(ShownProperty, target);
            return;
        }

        icon.BeginAnimation(ShownProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void OnPluggedInChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((BatteryIcon)sender).UpdateGlow();

    // The light runs only while plugged in, on screen, and not yet full.
    private void UpdateGlow()
    {
        if (IsPluggedIn && IsVisible && Fraction < 1 && !Motion.Reduced)
        {
            BeginAnimation(GlowProperty, new DoubleAnimation(-0.3, 1.3, TimeSpan.FromSeconds(1.6))
            {
                BeginTime = TimeSpan.FromSeconds(0.4),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            return;
        }

        BeginAnimation(GlowProperty, null);
        SetValue(GlowProperty, -1.0);
    }

    protected override void OnRender(DrawingContext context)
    {
        var height = ActualHeight;
        var tipWidth = Math.Max(2, height * 0.16);
        var gap = Math.Max(1.5, height * 0.1);
        var bodyWidth = ActualWidth - tipWidth - gap;

        if (bodyWidth <= 0 || height <= 0)
            return;

        var radius = height * 0.24;
        var body = new Rect(0, 0, bodyWidth, height);
        var shown = Math.Clamp((double)GetValue(ShownProperty), 0, 1);
        var fill = FillColor(shown);

        // The tip: a short rounded bar at the right, the empty color.
        var tip = new Rect(bodyWidth + gap, height * 0.28, tipWidth, height * 0.44);
        context.DrawRoundedRectangle(Frozen(new SolidColorBrush(Empty)), null, tip, tipWidth / 2, tipWidth / 2);

        // The body, then the charge clipped to its rounded shape.
        var shape = new RectangleGeometry(body, radius, radius);
        shape.Freeze();
        context.DrawGeometry(Frozen(new SolidColorBrush(Empty)), null, shape);

        context.PushClip(shape);
        var charged = new Rect(0, 0, bodyWidth * shown, height);
        context.DrawRectangle(Fill(fill), null, charged);

        // While charging, a soft light moves across the charge.
        var glow = (double)GetValue(GlowProperty);
        if (glow > -0.5 && charged.Width > 0)
        {
            var center = glow * charged.Width;
            var light = new LinearGradientBrush
            {
                StartPoint = new Point(center - charged.Width * 0.35, 0),
                EndPoint = new Point(center + charged.Width * 0.35, 0),
                MappingMode = BrushMappingMode.Absolute,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }
            };
            light.Freeze();
            context.DrawRectangle(light, null, charged);
        }

        context.Pop();

        // The bolt, in the middle, while plugged in.
        if (IsPluggedIn && TryFindResource("Icon.Bolt") is Geometry bolt)
        {
            var size = height * 0.85;
            var scale = size / 24;
            context.PushTransform(new TranslateTransform(bodyWidth / 2 - size / 2, (height - size) / 2));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawGeometry(Frozen(new SolidColorBrush(Bolt)), null, bolt);
            context.Pop();
            context.Pop();
        }
    }

    private Color FillColor(double charge)
    {
        if (IsPluggedIn)
            return Accent is SolidColorBrush accent ? accent.Color : Color.FromRgb(0x44, 0xD6, 0x2C);

        return charge <= CriticalBelow ? Critical : charge <= LowBelow ? Low : Normal;
    }

    private static SolidColorBrush Fill(Color color) => Frozen(new SolidColorBrush(color));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
