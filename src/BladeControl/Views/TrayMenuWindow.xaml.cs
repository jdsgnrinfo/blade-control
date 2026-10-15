using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BladeControl.UI;

namespace BladeControl.Views;

/// <summary>A performance mode in the tray menu: its name, whether it is on now, whether it can be picked, and what picking it does.</summary>
internal sealed record TrayMenuMode(string Name, bool Current, bool CanPick, Action Pick);

/// <summary>
/// The tray icon's right-click menu: the performance modes, with a check on
/// the one in use, then Open and Exit. It opens at the pointer (above it, by
/// the taskbar), scales in as the dropdowns do, and goes as soon as anything
/// else is clicked. Escape closes it too.
/// </summary>
public partial class TrayMenuWindow : Window
{
    private readonly Action _open;
    private readonly Action _exit;
    private bool _closing;

    private TrayMenuWindow(Action open, Action exit, IReadOnlyList<TrayMenuMode> modes)
    {
        InitializeComponent();
        _open = open;
        _exit = exit;
        AddModes(modes);

        Deactivated += (_, _) => CloseOnce();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                CloseOnce();
        };
    }

    /// <summary>Opens the menu at the pointer.</summary>
    internal static void ShowAtPointer(Action open, Action exit, IReadOnlyList<TrayMenuMode> modes)
    {
        var menu = new TrayMenuWindow(open, exit, modes) { Opacity = 0, Left = -10000, Top = -10000 };
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

    // Boxicons' check (bx-check), drawn on a 24 grid.
    private static readonly Geometry Check = Geometry.Parse(
        "M10,15.586 L6.707,12.293 L5.293,13.707 L10,18.414 L19.707,8.707 L18.293,7.293 Z");

    // The modes at the top, each with a green check when it is the one in use,
    // then a line before Open and Exit.
    private void AddModes(IReadOnlyList<TrayMenuMode> modes)
    {
        if (modes.Count == 0)
            return;

        var style = (Style)FindResource("TrayItem");
        var accent = (Brush)FindResource("Brush.Accent");

        for (var index = 0; index < modes.Count; index++)
        {
            var mode = modes[index];
            var row = new System.Windows.Controls.DockPanel();
            var check = new System.Windows.Shapes.Path
            {
                Data = Check,
                Fill = accent,
                Stretch = Stretch.Uniform,
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = mode.Current ? Visibility.Visible : Visibility.Hidden,
                Margin = new Thickness(-18, 0, 8, 0)
            };
            System.Windows.Controls.DockPanel.SetDock(check, System.Windows.Controls.Dock.Left);
            row.Children.Add(check);
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = mode.Name });

            var item = new System.Windows.Controls.Button { Style = style, Content = row, IsEnabled = mode.CanPick };
            item.Click += (_, _) =>
            {
                CloseOnce();
                mode.Pick();
            };
            Items.Children.Insert(index, item);
        }

        Items.Children.Insert(modes.Count, new System.Windows.Controls.Border
        {
            Height = 1,
            Margin = new Thickness(8, 3, 8, 3),
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E))
        });
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
