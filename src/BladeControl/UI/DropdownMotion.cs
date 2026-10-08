using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BladeControl.UI;

/// <summary>
/// The dropdowns' motion, as Arc UI's dropdown menu: the list scales in from
/// the box and its items fade in one after another; a single highlight glides
/// between items under the pointer (and jumps for the keyboard); the chevron
/// turns; a new choice rises into the box. The Dropdown style switches it on
/// and names the parts it moves: Menu, Highlight, HighlightHost, Chevron and
/// Choice. With Windows' animation effects off only a quick fade is left.
/// </summary>
public static class DropdownMotion
{
    private static readonly Duration Open = TimeSpan.FromMilliseconds(180);
    private static readonly Duration Glide = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(18);
    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Snappy = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };

    private static readonly DependencyPropertyDescriptor Highlighted =
        DependencyPropertyDescriptor.FromProperty(ComboBoxItem.IsHighlightedProperty, typeof(ComboBoxItem));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DropdownMotion), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    // Each item is watched once.
    private static readonly DependencyProperty WatchedProperty = DependencyProperty.RegisterAttached(
        "Watched", typeof(bool), typeof(DropdownMotion), new PropertyMetadata(false));

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ComboBox box || e.NewValue is not true)
            return;

        box.DropDownOpened += (_, _) => Opened(box);
        box.DropDownClosed += (_, _) => Turn(box, 0);
        box.SelectionChanged += (_, _) => Rise(box);
    }

    // ===== Opening =====

    private static void Opened(ComboBox box)
    {
        Turn(box, 180);

        if (Part<FrameworkElement>(box, "Menu") is not { } menu)
            return;

        var reduced = Motion.Reduced;
        menu.Opacity = 0;
        menu.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, reduced ? TimeSpan.FromMilliseconds(90) : Open) { EasingFunction = Out });

        if (!reduced)
        {
            // Scales in from the box above it.
            var scale = new ScaleTransform(0.96, 0.92);
            menu.RenderTransformOrigin = new Point(0.5, 0);
            menu.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Open) { EasingFunction = Snappy });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Open) { EasingFunction = Snappy });
        }

        // The items exist once the list has been laid out.
        menu.Dispatcher.BeginInvoke(() => ItemsIn(box, reduced), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // The items fade in and rise a little, one after another; the highlight starts on the choice.
    private static void ItemsIn(ComboBox box, bool reduced)
    {
        for (var index = 0; index < box.Items.Count; index++)
        {
            if (box.ItemContainerGenerator.ContainerFromIndex(index) is not ComboBoxItem item)
                continue;

            Watch(box, item);

            if (reduced)
                continue;

            var delay = TimeSpan.FromTicks(Stagger.Ticks * Math.Min(index, 12));
            var shift = new TranslateTransform(0, 4);
            item.RenderTransform = shift;
            item.Opacity = 0;
            item.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Glide) { BeginTime = delay, EasingFunction = Out });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, Glide) { BeginTime = delay, EasingFunction = Out });
        }

        var start = box.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, box.SelectedIndex)) as ComboBoxItem;
        MoveHighlight(box, start, jump: true);
    }

    private static void Watch(ComboBox box, ComboBoxItem item)
    {
        if ((bool)item.GetValue(WatchedProperty))
            return;

        item.SetValue(WatchedProperty, true);
        Highlighted.AddValueChanged(item, (_, _) =>
        {
            if (item.IsHighlighted)
                MoveHighlight(box, item, jump: InputManager.Current.MostRecentInputDevice is KeyboardDevice);
        });
    }

    // ===== The highlight =====

    private static void MoveHighlight(ComboBox box, ComboBoxItem? item, bool jump)
    {
        if (Part<FrameworkElement>(box, "Highlight") is not { } highlight ||
            Part<FrameworkElement>(box, "HighlightHost") is not { } host ||
            item is null || !item.IsVisible)
        {
            return;
        }

        var top = item.TranslatePoint(new Point(0, 0), host).Y;

        // The item's own rise is not where it will rest.
        if (item.RenderTransform is TranslateTransform shift)
            top -= shift.Y;

        highlight.Width = item.ActualWidth;
        highlight.Height = item.ActualHeight;

        if (highlight.RenderTransform is not TranslateTransform place)
            highlight.RenderTransform = place = new TranslateTransform();

        var wasShown = highlight.Opacity > 0;
        highlight.Opacity = 1;

        if (jump || !wasShown || Motion.Reduced)
        {
            place.BeginAnimation(TranslateTransform.YProperty, null);
            place.Y = top;
            return;
        }

        place.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(top, Glide) { EasingFunction = Snappy });
    }

    // ===== The box =====

    private static void Turn(ComboBox box, double angle)
    {
        if (Part<FrameworkElement>(box, "Chevron") is not { } chevron)
            return;

        if (chevron.RenderTransform is not RotateTransform turn)
        {
            chevron.RenderTransformOrigin = new Point(0.5, 0.5);
            chevron.RenderTransform = turn = new RotateTransform();
        }

        if (Motion.Reduced)
            turn.Angle = angle;
        else
            turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, Open) { EasingFunction = Out });
    }

    // A new choice rises into the box.
    private static void Rise(ComboBox box)
    {
        if (Motion.Reduced || !box.IsLoaded || Part<FrameworkElement>(box, "Choice") is not { } choice)
            return;

        if (choice.RenderTransform is not TranslateTransform shift)
            choice.RenderTransform = shift = new TranslateTransform();

        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, Open) { EasingFunction = Out });
        choice.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Open) { EasingFunction = Out });
    }

    private static T? Part<T>(ComboBox box, string name) where T : class =>
        box.Template?.FindName(name, box) as T;
}
