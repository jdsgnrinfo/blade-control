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

        if (IsInside(clicked, EditorCard) || FindAncestor<ListBoxItem>(clicked) is not null)
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
