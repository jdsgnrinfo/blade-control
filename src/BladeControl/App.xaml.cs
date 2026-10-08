using System.Globalization;
using System.Windows;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Services;
using BladeControl.ViewModels;
using BladeControl.Views;

namespace BladeControl;

public partial class App : Application
{
    // Session-local, so each signed-in user can run their own copy.
    private const string SingleInstanceMutexName = @"Local\BladeControl.SingleInstance";

    private Mutex? _singleInstance;
    private AppServices? _services;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two copies would compete for the laptop's HID command channel and
        // overwrite each other's settings file.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            AppLog.Info("Another Blade Control is already running for this user; exiting.");
            Shutdown();
            return;
        }

        AppLog.Info("Blade Control starting.");
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Unhandled error on the interface thread.", args.Exception);
            args.Handled = true;
        };

        _services = new AppServices();

        // The language has to be known before any text is made.
        L.Current = L.Resolve(_services.Settings.Language, CultureInfo.CurrentUICulture);

        _viewModel = new MainViewModel(_services);
        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();

        _ = _viewModel.Performance.StartAsync();
        _viewModel.Device.Start();

        // "--screenshot file.png [page]": draws the window into a picture a few
        // seconds after it opens, then closes. For the README and for checking
        // the look without touching the screen.
        if (e.Args is ["--screenshot", var path, ..])
        {
            if (e.Args.Length > 2 && Enum.TryParse<AppPage>(e.Args[2], ignoreCase: true, out var page))
                _viewModel.Open(page);

            ScreenshotThenClose(window, path);
        }
    }

    private static async void ScreenshotThenClose(Window window, string path)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var content = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX),
            (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            System.Windows.Media.PixelFormats.Pbgra32);

        // The window's own background is not part of its content: drawn first.
        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(visual);
        bitmap.Render(content);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using (var file = System.IO.File.Create(path))
            encoder.Save(file);

        window.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Performance.TurnOffMaxFanBeforeExit();
        _viewModel?.Device.Stop();
        _services?.Dispose();
        _singleInstance?.Dispose();
        AppLog.Info("Blade Control exiting.");
        base.OnExit(e);
    }
}
