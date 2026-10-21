using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using BladeControl.ViewModels;
using BladeControl.Views;

namespace BladeControl;

public partial class App : Application
{
    // Session-local, so each signed-in user can run their own copy.
    private const string SingleInstanceMutexName = @"Local\BladeControl.SingleInstance";

    // A second copy sets this, so the first shows its window (from the tray) instead.
    private const string ShowWindowEventName = @"Local\BladeControl.ShowWindow";

    private Mutex? _singleInstance;
    private AppServices? _services;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private GlobalShortcuts? _shortcuts;
    private EventWaitHandle? _showWindow;
    private RegisteredWaitHandle? _showWindowWait;
    private bool _quitting;

    // While the window is closed to the tray, what little the app touches is moved out of RAM now and then.
    private readonly DispatcherTimer _trimTimer = new() { Interval = TimeSpan.FromMinutes(10) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The elevated helpers Optimize starts (with Windows' permission) do
        // their one job and exit, before the single-instance check (the normal
        // app is already running) and before any window exists.
        if (RunElevatedCommand(e.Args) is int exitCode)
        {
            Shutdown(exitCode);
            return;
        }

        // After a restart from Settings, let the old copy finish exiting first:
        // it still holds the single-instance lock until it is gone.
        AppRestart.WaitForPreviousCopy(e.Args);

        // Two copies would compete for the laptop's HID command channel and
        // overwrite each other's settings file.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);

        // A screenshot run only looks, so it may run beside the open app.
        if (!isFirstInstance && e.Args is not ["--screenshot", ..])
        {
            AppLog.Info("Another Blade Control is already running for this user; showing it and exiting.");

            if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var showFirst))
            {
                using (showFirst)
                    showFirst.Set();
            }

            Shutdown();
            return;
        }

        AppLog.Info("Blade Control starting.");

        // The files an update replaced, kept as ".old" while the previous copy ran.
        if (System.IO.Path.GetDirectoryName(Environment.ProcessPath) is { } appFolder)
            _ = Task.Run(() => UpdateInstaller.CleanUp(appFolder));
        // Every error nobody caught goes to the log; the user hears of it once.
        var errors = new UnhandledExceptionHandler(AppLog.Error, message => UI.Dialogs.Tell(message, "Blade Control"), AppLog.LogFilePath);
        DispatcherUnhandledException += (_, args) =>
        {
            errors.OnRecoverableException(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                errors.OnFatalException(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            errors.OnUnobservedTaskException(args.Exception);
            args.SetObserved();
        };

        // How long each part of starting takes, for the log: .NET's own start
        // before this code runs, the services, the pages and the first drawing.
        var beforeApp = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
        var startup = System.Diagnostics.Stopwatch.StartNew();

        _services = new AppServices();
        var servicesMs = startup.ElapsedMilliseconds;

        // The language has to be known before any text is made.
        L.Current = L.Resolve(_services.Settings.Language, CultureInfo.CurrentUICulture);

        _viewModel = new MainViewModel(_services);
        var pagesMs = startup.ElapsedMilliseconds - servicesMs;

        // A screenshot run leaves the size and the tray to the open app.
        var screenshot = e.Args is ["--screenshot", ..];

        if (!screenshot)
            KeepInTray();

        // Started at sign-in ("--tray"): it waits in the tray, and the window opens from there.
        var inTray = !screenshot && e.Args.Contains(RunKeyStartupRegistration.TrayArgument);
        var window = inTray ? null : OpenWindow(keepPlacement: !screenshot);
        var windowMs = startup.ElapsedMilliseconds - servicesMs - pagesMs;

        void LogStartup(string end) => AppLog.Info(
            $"Started: .NET {(long)beforeApp.TotalMilliseconds} ms, services {servicesMs} ms, pages {pagesMs} ms, " +
            $"window {windowMs} ms, {end} {(long)(beforeApp.TotalMilliseconds + startup.ElapsedMilliseconds)} ms after launch.");

        if (window is null)
            LogStartup("in the tray");
        else
            window.ContentRendered += (_, _) => LogStartup("drawn");

        _ = _viewModel.Performance.StartAsync();
        _viewModel.Device.Start();
        _viewModel.Performance.Monitor.Start(); // The first page is Performance.

        if (inTray)
        {
            _viewModel.SetWindowShown(false);
            ReleaseMemoryLater();
        }

        if (!screenshot)
            RefreshStartupEntry();
        // A screenshot run leaves the screen's rate and colors to the open app.
        if (e.Args is not ["--screenshot", ..])
        {
            _viewModel.Display.Restore();

            // Battery mode on battery, or what a previous run left put back.
            _ = _services.BatteryMode.StartAsync();

            // Then the apps with a profile are watched for, on top of what was just put back.
            _viewModel.Apps.Start();
        }

        // "--screenshot file.png [page] [width] [custom] [scrolled]": draws the window into a picture a few
        // seconds after it opens, then closes. For the README and for checking
        // the look without touching the screen.
        if (e.Args is ["--screenshot", var path, ..])
        {
            if (e.Args.Length > 2 && Enum.TryParse<AppPage>(e.Args[2], ignoreCase: true, out var page))
                _viewModel.Page = page;

            // An optional width, to see how the page holds up in a narrow window.
            if (e.Args.Length > 3 && double.TryParse(e.Args[3], out var width))
                window!.Width = Math.Max(window.MinWidth, width);

            // "custom" after the width shows Custom with its levels, without
            // sending anything to the laptop.
            var previewCustom = e.Args.Skip(4).Contains("custom");

            // "scrolled" scrolls the page to its end first, to see the sticky panel.
            var scrolled = e.Args.Skip(4).Contains("scrolled");

            ScreenshotThenClose(window!, path, previewCustom ? _viewModel.Performance : null, scrolled);
        }
    }

    // Closing the window leaves the app running in the tray; only Exit (the tray
    // menu) or a restart ends it. A second copy started asks this one to show its window.
    private void KeepInTray()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SessionEnding += (_, _) => _quitting = true;

        _tray = new TrayIcon();
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _tray.MenuRequested += (_, _) => TrayMenuWindow.ShowAtPointer(ShowMainWindow, Quit, TrayModes());
        _viewModel!.Performance.ModeChanged += (_, mode) => Dispatcher.BeginInvoke(() => _tray?.ShowMode(mode));

        // A newer version found on its own: a Windows notice, which opens Settings.
        _viewModel.Settings.Updates.Found += (_, version) =>
            _tray?.ShowNotice(L.F("Blade Control {0} is available", version), L.T("Open Settings to update."));
        _tray.NoticeClicked += (_, _) =>
        {
            ShowMainWindow();
            _viewModel.Page = AppPage.Settings;
        };
        _viewModel.Settings.Updates.Start();

        _showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        _showWindowWait = ThreadPool.RegisterWaitForSingleObject(_showWindow, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _trimTimer.Tick += (_, _) => BackgroundMemory.Trim();

        StartShortcuts();
    }

    // The modes in the tray menu: picked the way the mode shortcuts pick them,
    // with the same notice, for the power source the laptop is on now.
    private IReadOnlyList<TrayMenuMode> TrayModes()
    {
        var performance = _viewModel!.Performance;

        TrayMenuMode Mode(PerformanceMode mode, string name) =>
            new(name, performance.CurrentMode == mode, performance.CanPick(mode), () => _ = SwitchModeFromShortcutAsync(mode));

        var hasPerformance = performance.HasPerformanceMode;

        return
        [
            Mode(PerformanceMode.Silent, L.T("Silent")),
            Mode(PerformanceMode.Balanced, L.T("Balanced")),
            .. hasPerformance ? new[] { Mode(PerformanceMode.Performance, L.T("Performance")) } : [],
            Mode(PerformanceMode.Custom, L.T("Custom"))
        ];
    }

    // Fn+Del shows or hides the window; Ctrl+Shift+F1/F2/F3 switch mode, unless
    // turned off in Settings. A key another program owns is logged and said under
    // the switch, and the others still work.
    private void StartShortcuts()
    {
        _shortcuts = new GlobalShortcuts();
        _shortcuts.WindowRequested += (_, _) => ToggleWindowFromShortcut();
        _shortcuts.ModeRequested += async (_, mode) => await SwitchModeFromShortcutAsync(mode);

        if (_shortcuts.TryRegisterWindow())
            AppLog.Info($"Shortcut {GlobalShortcuts.WindowShortcutText} is on.");
        else
            AppLog.Error($"Shortcut {GlobalShortcuts.WindowShortcutText} could not be registered; another program uses it.");

        if (_services!.Settings.ProfileShortcuts)
            StartModeShortcuts();

        _viewModel!.Settings.ProfileShortcutsChanged += (_, enabled) =>
        {
            if (enabled)
            {
                StartModeShortcuts();
                return;
            }

            _shortcuts.UnregisterModes();
            _viewModel.Settings.ProfileShortcutsHint = L.T("Ctrl+Shift+F1, F2 and F3: Silent, Balanced and Custom.");
            AppLog.Info("Performance mode shortcuts are off.");
        };
    }

    private void StartModeShortcuts()
    {
        var taken = _shortcuts!.TryRegisterModes();

        if (taken.Count == 0)
        {
            AppLog.Info("Performance mode shortcuts are on.");
            return;
        }

        AppLog.Error($"Shortcuts {string.Join(", ", taken)} could not be registered; another program uses them.");
        _viewModel!.Settings.ProfileShortcutsHint = L.F("{0} shortcut is used by another program.", string.Join(", ", taken));
    }

    // Fn+Del, maybe in a game: the window to the front, or back to the tray if it already is.
    private void ToggleWindowFromShortcut()
    {
        if (MainWindow is { IsActive: true } shown)
        {
            AppLog.Info($"{GlobalShortcuts.WindowShortcutText} pressed: back to the tray.");
            shown.Close();
            return;
        }

        ShowMainWindow();

        // Above a game for this showing, even with Always on top off.
        if (MainWindow is { } window)
        {
            window.Topmost = true;
            window.Topmost = _services!.Settings.AlwaysOnTop;
            window.Activate();
        }

        AppLog.Info($"{GlobalShortcuts.WindowShortcutText} pressed: showing the window.");
    }

    // A mode shortcut, maybe in a game: switch as the button would, then say how it
    // went at the top right without taking the focus.
    private async Task SwitchModeFromShortcutAsync(PerformanceMode mode)
    {
        var (applied, status) = await _viewModel!.Performance.SelectFromShortcutAsync(mode);
        AppLog.Info($"Mode shortcut for {mode}: {status}");
        ModeToastWindow.ShowNotice(mode, status, applied);
    }

    // The window is made each time it opens and let go when it closes to the
    // tray: hidden, its pages, pictures and drawing surfaces would only hold memory.
    private MainWindow OpenWindow(bool keepPlacement)
    {
        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Topmost = _services!.Settings.AlwaysOnTop;
        RestorePlacement(window, _services.Settings.Window);

        if (keepPlacement)
        {
            window.Closing += (_, _) => _services.UpdateSettings(settings => settings with { Window = PlacementOf(window) });
            window.Closed += (_, _) => OnWindowClosed(window);
            window.StateChanged += (_, _) => OnWindowStateChanged(window);
        }

        _windowMinimized = false;
        window.Show();
        return window;
    }

    private bool _windowMinimized;

    // Minimized counts as in the tray: nothing on screen to read sensors or
    // draw for, so the reading stops and, a moment later, the RAM it used goes
    // back to Windows. Restored, the open page starts over. Between normal and
    // maximized nothing changes.
    private void OnWindowStateChanged(MainWindow window)
    {
        var minimized = window.WindowState == WindowState.Minimized;

        if (minimized == _windowMinimized)
            return;

        _windowMinimized = minimized;
        _viewModel!.SetWindowShown(!minimized);

        if (minimized)
        {
            _ = Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2));

                if (_windowMinimized && MainWindow == window)
                    BackgroundMemory.Trim();
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    // Closed to the tray: nothing is read each second any more, and once the
    // window has been let go the memory it held goes back to Windows.
    private void OnWindowClosed(MainWindow window)
    {
        if (_quitting)
            return;

        if (MainWindow == window)
            MainWindow = null;

        window.DataContext = null;
        _viewModel!.SetWindowShown(false);
        ReleaseMemoryLater();
    }

    // A moment after the window has gone (or at a start in the tray), once the app
    // is idle, the memory goes back to Windows, and a trim keeps it low from then on.
    private void ReleaseMemoryLater() =>
        _ = Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));

            if (MainWindow is null)
            {
                BackgroundMemory.Release();
                _trimTimer.Start();
            }
        }, DispatcherPriority.ApplicationIdle);

    // An entry made by an older version (or for an exe since moved) is written again,
    // so sign-in starts this exe, in the tray.
    private void RefreshStartupEntry()
    {
        try
        {
            if (_services!.StartupRegistration.IsEnabled)
                _services.StartupRegistration.SetEnabled(true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            AppLog.Error("Could not refresh the start-at-login entry.", exception);
        }
    }

    /// <summary>Brings the window back from the tray, or to the front.</summary>
    private void ShowMainWindow()
    {
        if (MainWindow is not { } window)
        {
            _trimTimer.Stop();
            OpenWindow(keepPlacement: true).Activate();
            _viewModel?.SetWindowShown(true);
            return;
        }

        window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }

    /// <summary>Ends the app: Exit in the tray menu, or a restart. The window closes for good and the tray icon goes.</summary>
    public static void Quit() => QuitAsync(restarting: false);

    /// <summary>Ends the app for a new copy that starts now: battery mode stays as it is, for the new copy to carry on.</summary>
    public static void QuitForRestart() => QuitAsync(restarting: true);

    private static async void QuitAsync(bool restarting)
    {
        if (Current is not App app || app._quitting)
            return;

        app._quitting = true;
        app.MainWindow?.Close();

        // An app profile still on is taken off first, so the laptop is left as the user set it.
        if (app._viewModel is { } viewModel)
        {
            try
            {
                await viewModel.Apps.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not put back what an app profile changed before exiting.", exception);
            }
        }

        // Without the app nothing would put the screen, keyboard and Windows' effects back on plugging in.
        if (!restarting && app._services is { } services)
        {
            app._viewModel?.Display.PutFixedRateBack();

            try
            {
                await services.BatteryMode.RestoreAsync("Blade Control is exiting").WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not put back what battery mode changed before exiting.", exception);
            }
        }

        app.Shutdown();
    }

    // The window where it was left, as big as it was, if that is still on a screen
    // (a monitor may have been unplugged since); otherwise centred at its first size.
    private static void RestorePlacement(Window window, WindowPlacement? placement)
    {
        if (placement is not { } saved)
            return;

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(saved.Left, saved.Top, saved.Width, saved.Height);
        var visible = Rect.Intersect(screen, bounds);

        if (visible.IsEmpty || visible.Width < 120 || visible.Height < 80)
            return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = saved.Left;
        window.Top = saved.Top;
        window.Width = Math.Clamp(saved.Width, window.MinWidth, screen.Width);
        window.Height = Math.Clamp(saved.Height, window.MinHeight, screen.Height);

        if (saved.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    // Maximized, the size before it was maximized is kept, so it comes back to that.
    private static WindowPlacement PlacementOf(Window window)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        return new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized);
    }

    private static int? RunElevatedCommand(string[] args) =>
        RazerServiceCommand.TryRun(args, new WindowsServiceControl())
        ?? MemoryCacheCommand.TryRun(args, WindowsMemoryCache.Purge)
        ?? HibernationCommand.TryRun(args, WindowsHibernation.SetEnabled)
        ?? WindowsCachesCommand.TryRun(args, WindowsCaches.CleanElevated)
        ?? DedicatedGpuCommand.TryRun(args, () => DedicatedGpuDevice.Find()?.InstanceId, DedicatedGpuDevice.SetEnabled);

    private static async void ScreenshotThenClose(MainWindow window, string path, PerformanceViewModel? previewCustom, bool scrolled)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));

        previewCustom?.Preview(new Core.Models.PerformanceState(
            Core.Models.PerformanceMode.Custom, Core.Models.CpuBoost.High, Core.Models.GpuBoost.Medium, MaxFan: false));
        if (scrolled)
            window.PageScroll.ScrollToEnd();

        await Task.Delay(TimeSpan.FromSeconds(1));

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
        _viewModel?.Lighting.Shutdown();
        _viewModel?.Optimize.Dispose();
        _viewModel?.Display.Dispose();
        _viewModel?.Battery.Dispose();
        _viewModel?.Performance.Monitor.Dispose();
        _viewModel?.Device.Stop();
        _showWindowWait?.Unregister(null);
        _showWindow?.Dispose();
        _tray?.Dispose();
        _shortcuts?.Dispose();
        _services?.Dispose();
        _singleInstance?.Dispose();
        AppLog.Info("Blade Control exiting.");
        base.OnExit(e);
    }
}
