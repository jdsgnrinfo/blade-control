using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BladeControl.UI;

/// <summary>
/// A page coming into view fades in and rises a few pixels, in a fifth of a
/// second: enough to see the change, too short to wait for. Its cards follow
/// one another by a few milliseconds. With Windows' animation effects off it
/// just appears.
/// </summary>
public static class Reveal
{
    private static readonly Duration Length = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(40);
    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static readonly DependencyProperty OnShowProperty = DependencyProperty.RegisterAttached(
        "OnShow", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnOnShowChanged));

    public static bool GetOnShow(DependencyObject element) => (bool)element.GetValue(OnShowProperty);

    public static void SetOnShow(DependencyObject element, bool value) => element.SetValue(OnShowProperty, value);

    private static void OnOnShowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement page || e.NewValue is not true)
            return;

        page.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && page.IsLoaded && !Motion.Reduced)
                Play(page);
        };
    }

    private static void Play(FrameworkElement page)
    {
        // The page's columns (or the page itself, without any) each rise in turn.
        var parts = Columns(page);

        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var delay = TimeSpan.FromTicks(Stagger.Ticks * index);

            if (part.RenderTransform is not TranslateTransform shift || shift.IsFrozen)
                part.RenderTransform = shift = new TranslateTransform();

            part.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Length) { BeginTime = delay, EasingFunction = Out, FillBehavior = FillBehavior.Stop });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, Length) { BeginTime = delay, EasingFunction = Out, FillBehavior = FillBehavior.Stop });
        }
    }

    // The children of the page's AdaptiveColumns, if it has one.
    private static List<UIElement> Columns(FrameworkElement page)
    {
        if (page is System.Windows.Controls.UserControl { Content: AdaptiveColumns columns })
            return [.. columns.Children.OfType<UIElement>()];

        return [page];
    }
}
