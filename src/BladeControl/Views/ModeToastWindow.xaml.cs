using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.UI;

namespace BladeControl.Views;

/// <summary>
/// The small notice at the top right of the screen after a performance
/// shortcut, drawn like a game HUD, as in Razer Helper: a strip in Chroma's
/// colors down the left edge, the top right corner cut on a slant, the mode's
/// icon in green, then "PERFORMANCE MODE" small and spaced, the mode's name
/// large in capitals, and how it went ("Active", "Needs to be plugged in"). A
/// refused mode turns the strip amber and the icon grey. It never takes the
/// focus, so a game in front keeps its keyboard, stays above other windows,
/// and fades out by itself. Another shortcut while it shows just updates it.
/// </summary>
public partial class ModeToastWindow : Window
{
    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(300);
    private static readonly Brush Refused = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)));
    private const double ScreenMargin = 16;

    private static ModeToastWindow? _current;

    private readonly Brush _chroma;

    private ModeToastWindow()
    {
        InitializeComponent();
        _chroma = Edge.Fill;
        SourceInitialized += (_, _) => KeepFromFocus();
    }

    /// <summary>Shows (or updates) the notice at the top right of the screen the pointer is on.</summary>
    /// <param name="applied">Whether the mode is now on: green, and amber and grey when it was refused.</param>
    internal static void ShowNotice(PerformanceMode mode, string status, bool applied) =>
        Current().Fill(L.T("Performance mode"), L.T(mode.ToString()), status, applied, mode, null);

    /// <summary>
    /// Shows (or updates) the notice for an app's profile: "APP PROFILE", the
    /// app's name and what it changed, with the app's icon when it has one.
    /// </summary>
    internal static void ShowAppNotice(string appName, ImageSource? icon, PerformanceMode? mode, string status, bool applied) =>
        Current().Fill(L.T("App profile"), appName, status, applied, mode ?? PerformanceMode.Balanced, icon);

    private static ModeToastWindow Current()
    {
        var app = Application.Current;
        var mainWindow = app.MainWindow;

        _current ??= new ModeToastWindow();

        // The first window made becomes the app's main window when there is none: not this one.
        if (app.MainWindow == _current)
            app.MainWindow = mainWindow;

        return _current;
    }

    private void Fill(string label, string name, string status, bool applied, PerformanceMode mode, ImageSource? icon)
    {
        var culture = CultureInfo.CurrentCulture;
        var accent = (Brush)FindResource("Brush.Accent");

        Edge.Fill = applied ? _chroma : Refused;
        ModeIcon.Data = (Geometry)FindResource(mode switch
        {
            PerformanceMode.Silent => "Icon.Silent",
            PerformanceMode.Gaming => "Icon.Gaming",
            PerformanceMode.Custom => "Icon.Custom",
            _ => "Icon.Balanced"
        });
        ModeIcon.Fill = applied ? accent : (Brush)FindResource("Brush.TextMuted");
        ModeIcon.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        AppIcon.Source = icon;
        AppIcon.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        AppIcon.Opacity = applied ? 1 : 0.5;
        Label.Foreground = applied ? accent : Refused;
        Label.ItemsSource = label.ToUpper(culture);

        // A long app name gets smaller letters so more of it fits.
        var upper = name.ToUpper(culture);
        ModeName.FontSize = upper.Length > 16 ? 16 : 21;
        ModeName.ItemsSource = upper;
        Status.Text = status;
        ShowAtTopRight();
    }

    private void ShowAtTopRight()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (!IsVisible)
        {
            Left = -10000;
            Show();
        }

        // The working area of the screen the pointer is on, in this window's units.
        GetCursorPos(out var cursor);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        var toDips = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topRight = toDips.Transform(new Point(area.Right, area.Top));

        Left = topRight.X - Width - ScreenMargin;
        Top = topRight.Y + ScreenMargin;

        // Held for a moment, then faded out and hidden; nothing fades with Windows' animations off.
        var fade = new DoubleAnimation(1, 0, Motion.Reduced ? TimeSpan.Zero : Fade) { BeginTime = Shown };
        fade.Completed += (_, _) =>
        {
            if (Opacity == 0)
                Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    // A tool window that is never activated: no taskbar button, and the game keeps the keyboard.
    private void KeepFromFocus()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExToolWindow | WsExNoActivate | WsExTopmost);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
