using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BladeControl.Core.Localization;

namespace BladeControl.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => ShowWindowState();
        SourceInitialized += (_, _) => RoundCorners();
        ShowWindowState();
        FitToScreen();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;

        // Maximized, Windows pushes a borderless window's edges past the screen
        // by the resize border; the margin keeps the content on screen.
        Root.Margin = maximized ? new Thickness(7) : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? L.T("Restore") : L.T("Maximize");
    }

    // A screen smaller than the design size gets a window that fits it.
    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 40);
        Height = Math.Min(Height, area.Height - 40);
    }

    // Windows 11 rounds a window's corners only when asked, for a window that draws its own frame.
    private void RoundCorners()
    {
        const int cornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
        var round = 2; // DWMWCP_ROUND
        var handle = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(handle, cornerPreference, ref round, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
