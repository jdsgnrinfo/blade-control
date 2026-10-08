using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BladeControl.UI;

namespace BladeControl.Views;

/// <summary>
/// The tray icon's right-click menu: opens at the pointer (above it, by the
/// taskbar), scales in as the dropdowns do, and goes as soon as anything
/// else is clicked. Escape closes it too.
/// </summary>
public partial class TrayMenuWindow : Window
{
    private readonly Action _open;
    private readonly Action _exit;
    private bool _closing;

    private TrayMenuWindow(Action open, Action exit)
    {
        InitializeComponent();
        _open = open;
        _exit = exit;

        Deactivated += (_, _) => CloseOnce();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                CloseOnce();
        };
    }

    /// <summary>Opens the menu at the pointer.</summary>
    public static void ShowAtPointer(Action open, Action exit)
    {
        var menu = new TrayMenuWindow(open, exit) { Opacity = 0, Left = -10000, Top = -10000 };
        menu.Show();
        menu.PlaceAtPointer();
        menu.Activate();
        menu.PlayIn();
    }

    // Its corner at the pointer, turned to stay on the screen: above the
    // pointer by a bottom taskbar, to its left near the right edge.
    private void PlaceAtPointer()
    {
        GetCursorPos(out var cursor);
        var toDips = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pointer = toDips.Transform(new Point(cursor.X, cursor.Y));
        var area = SystemParameters.WorkArea;

        // The panel sits 8 in from the window's edges, to leave room for its shadow.
        var left = pointer.X - 8;
        var top = pointer.Y - ActualHeight + 8;

        if (left + ActualWidth > area.Right)
            left = pointer.X - ActualWidth + 8;

        if (top < area.Top)
            top = pointer.Y - 8;

        Left = Math.Max(area.Left, left);
        Top = Math.Min(Math.Max(area.Top, top), area.Bottom - ActualHeight);
    }

    private void PlayIn()
    {
        var reduced = Motion.Reduced;
        var length = TimeSpan.FromMilliseconds(reduced ? 90 : 180);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, length));

        if (reduced)
            return;

        var scale = new ScaleTransform(0.96, 0.92);
        Panel.RenderTransformOrigin = new Point(0.5, 1);
        Panel.RenderTransform = scale;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, length) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, length) { EasingFunction = ease });
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        CloseOnce();
        _open();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        CloseOnce();
        _exit();
    }

    private void CloseOnce()
    {
        if (_closing)
            return;

        _closing = true;
        Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}
