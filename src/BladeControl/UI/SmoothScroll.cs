using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BladeControl.UI;

/// <summary>
/// A gentler mouse wheel for the page: each notch moves it a short way and
/// the page glides there instead of jumping, so the eye keeps its place.
/// Notches in quick succession add up. With Windows' animation effects off it
/// still moves the shorter step, without the glide.
/// </summary>
public static class SmoothScroll
{
    // How far one notch of the wheel moves the page, in pixels (WPF's own is 48).
    private const double NotchPixels = 32;

    // How much of the way left the page covers each frame: lower is softer.
    private const double Follow = 0.2;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    // Where the page is heading, per scroll viewer.
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(double), typeof(SmoothScroll), new PropertyMetadata(double.NaN));

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.NewValue is not true)
            return;

        viewer.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;

        // A list that scrolls itself, or the equalizer, under the pointer keeps the wheel.
        if (e.OriginalSource is DependencyObject source && InnerScroller(source, viewer) is not null)
            return;

        e.Handled = true;

        var current = viewer.GetValue(TargetProperty) is double target && !double.IsNaN(target) ? target : viewer.VerticalOffset;
        var next = Math.Clamp(current - e.Delta / 120.0 * NotchPixels, 0, viewer.ScrollableHeight);

        if (Motion.Reduced)
        {
            viewer.ScrollToVerticalOffset(next);
            return;
        }

        var wasMoving = !double.IsNaN((double)viewer.GetValue(TargetProperty));
        viewer.SetValue(TargetProperty, next);

        if (wasMoving)
            return;

        void Frame(object? s, EventArgs args)
        {
            var goal = (double)viewer.GetValue(TargetProperty);
            var left = goal - viewer.VerticalOffset;

            if (Math.Abs(left) < 0.5 || !viewer.IsLoaded)
            {
                viewer.ScrollToVerticalOffset(goal);
                viewer.SetValue(TargetProperty, double.NaN);
                CompositionTarget.Rendering -= Frame;
                return;
            }

            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + left * Follow);
        }

        CompositionTarget.Rendering += Frame;
    }

    // Something between the pointer and the page that takes the wheel itself: a list
    // that scrolls, or the equalizer.
    private static DependencyObject? InnerScroller(DependencyObject source, ScrollViewer page)
    {
        for (var element = source; element is not null && element != page; element = Parent(element))
        {
            if (element is ScrollViewer inner && inner.ScrollableHeight > 0)
                return inner;

            // The equalizer uses the wheel to move its bands.
            if (element is EqualizerGraph { IsEnabled: true })
                return element;
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
