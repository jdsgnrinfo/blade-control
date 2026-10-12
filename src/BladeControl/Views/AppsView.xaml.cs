using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BladeControl.ViewModels;

namespace BladeControl.Views;

public partial class AppsView : UserControl
{
    private Window? _window;

    public AppsView()
    {
        InitializeComponent();

        // A click anywhere in the window outside the app's settings closes them, as a close would.
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);

            if (_window is not null)
                _window.PreviewMouseLeftButtonDown += Window_Click;
        };
        Unloaded += (_, _) =>
        {
            if (_window is not null)
                _window.PreviewMouseLeftButtonDown -= Window_Click;

            _window = null;
        };

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AppsViewModel before)
                before.PropertyChanged -= ViewModel_PropertyChanged;

            if (e.NewValue is AppsViewModel after)
                after.PropertyChanged += ViewModel_PropertyChanged;
        };
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppsViewModel.Selected) && sender is AppsViewModel { Selected: not null })
            Dispatcher.BeginInvoke(ShowEditor, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // An app was picked: in a window narrow enough that the settings sit under the list,
    // the page glides down to them (or up, if they are above), so its settings are in view.
    private void ShowEditor()
    {
        if (!IsVisible || FindAncestor<ScrollViewer>(this) is not { } page || !EditorCard.IsVisible)
            return;

        var top = EditorCard.TransformToAncestor(page).Transform(new Point(0, 0)).Y;
        var bottom = top + EditorCard.ActualHeight;
        const double margin = 16;

        // Already in view (or its top is, when it is taller than the window).
        if (top >= 0 && (bottom <= page.ViewportHeight || top <= page.ViewportHeight / 3))
            return;

        UI.SmoothScroll.GlideTo(page, page.VerticalOffset + top - margin);
    }

    // A click on the app already picked closes its settings again.
    private void AppItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { IsSelected: true } && DataContext is AppsViewModel apps)
        {
            apps.Selected = null;
            e.Handled = true;
        }
    }

    // Outside the settings: closed. On another app in the list: that one opens instead.
    private void Window_Click(object sender, MouseButtonEventArgs e)
    {
        if (!IsVisible || DataContext is not AppsViewModel { Selected: not null } apps || e.OriginalSource is not DependencyObject clicked)
            return;

        if (IsInside(clicked, EditorCard) || FindAncestor<ListBoxItem>(clicked) is not null || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(clicked) is not null)
            return;

        apps.Selected = null;
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = Parent(current))
        {
            if (current == container)
                return true;
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = element; current is not null; current = Parent(current))
        {
            if (current is T found)
                return found;
        }

        return null;
    }

    // Text runs and other content elements are not visuals: their logical parent leads back to one.
    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}
