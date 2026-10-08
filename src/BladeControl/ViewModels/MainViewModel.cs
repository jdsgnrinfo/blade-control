using System.Windows.Threading;
using BladeControl.Core.Localization;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>The pages, in the row of green tabs; Settings opens from the gear beside refresh.</summary>
internal enum AppPage
{
    Performance,
    Lighting,
    Display,
    Battery,
    Audio,
    Optimize,
    Settings
}

/// <summary>The window: which page is open, and the short message beside the tabs after a change.</summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public MainViewModel(AppServices services)
    {
        Services = services;
        Performance = new PerformanceViewModel(services, Report);
        Lighting = new LightingViewModel(services, Report);
        Display = new DisplayViewModel(services, Report);
        Battery = new BatteryViewModel(services);
        Audio = new AudioViewModel(services, Report);
        Optimize = new OptimizeViewModel(services, Report);
        Settings = new SettingsViewModel(services, Report);
        Device = new DevicePanelViewModel(services);

        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            Status = string.Empty;
        };
    }

    public AppServices Services { get; }

    public PerformanceViewModel Performance { get; }

    public LightingViewModel Lighting { get; }

    public DisplayViewModel Display { get; }

    public BatteryViewModel Battery { get; }

    public AudioViewModel Audio { get; }

    public OptimizeViewModel Optimize { get; }

    public SettingsViewModel Settings { get; }

    public DevicePanelViewModel Device { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPerformance), nameof(IsLighting), nameof(IsDisplay), nameof(IsBattery),
        nameof(IsAudio), nameof(IsOptimize), nameof(IsSettings), nameof(PageTitle), nameof(ShowsComingSoon))]
    private AppPage _page = AppPage.Performance;

    /// <summary>The pages not built yet show that they are coming.</summary>
    public bool ShowsComingSoon => Page is not (AppPage.Performance or AppPage.Lighting or AppPage.Display or AppPage.Battery or AppPage.Audio or AppPage.Optimize or AppPage.Settings);

    /// <summary>The open page's name, for the pages that are still to come.</summary>
    public string PageTitle => L.T(Page switch
    {
        AppPage.Lighting => "Lighting",
        AppPage.Display => "Display",
        AppPage.Battery => "Battery",
        AppPage.Audio => "Audio",
        AppPage.Optimize => "Optimize",
        AppPage.Settings => "Settings",
        _ => "Performance"
    });

    // The radio buttons: setting one to true opens it.
    public bool IsPerformance { get => Page == AppPage.Performance; set { if (value) Page = AppPage.Performance; } }

    public bool IsLighting { get => Page == AppPage.Lighting; set { if (value) Page = AppPage.Lighting; } }

    public bool IsDisplay { get => Page == AppPage.Display; set { if (value) Page = AppPage.Display; } }

    public bool IsBattery { get => Page == AppPage.Battery; set { if (value) Page = AppPage.Battery; } }

    public bool IsAudio { get => Page == AppPage.Audio; set { if (value) Page = AppPage.Audio; } }

    public bool IsOptimize { get => Page == AppPage.Optimize; set { if (value) Page = AppPage.Optimize; } }

    public bool IsSettings { get => Page == AppPage.Settings; set { if (value) Page = AppPage.Settings; } }

    // The Lighting page follows the Fn brightness keys only while it is open;
    // Optimize and Settings read again what may have changed since.
    partial void OnPageChanged(AppPage value) => OpenPage(value);

    /// <summary>
    /// The window closed to the tray, or back from it: while it is hidden nothing
    /// is read once a second; shown again, the open page starts over.
    /// </summary>
    public void SetWindowShown(bool shown)
    {
        if (shown)
        {
            Device.Start();
            OpenPage(Page);
            return;
        }

        Device.Stop();
        Performance.Monitor.Stop();
        Lighting.SetPageOpen(false);
        Battery.SetPageOpen(false);
        Audio.Flush();
    }

    private void OpenPage(AppPage value)
    {
        Lighting.SetPageOpen(value == AppPage.Lighting);
        Battery.SetPageOpen(value == AppPage.Battery);

        if (value != AppPage.Audio)
            Audio.Flush();

        if (value == AppPage.Performance)
            Performance.Monitor.Start();
        else
            Performance.Monitor.Stop();

        if (value == AppPage.Display)
            Display.Refresh();
        else if (value == AppPage.Optimize)
            _ = Optimize.RefreshAsync();
        else if (value == AppPage.Audio)
            Audio.Refresh();
        else if (value == AppPage.Settings)
            Settings.Refresh();
    }

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _statusIsError;

    /// <summary>Reads the laptop again: Fn keys or another program may have changed its mode.</summary>
    [RelayCommand]
    private Task Refresh() => Page switch
    {
        AppPage.Display => RefreshDisplay(),
        AppPage.Lighting => Lighting.RefreshAsync(),
        AppPage.Battery => Battery.RefreshAsync(),
        AppPage.Audio => RefreshAudio(),
        AppPage.Optimize => Optimize.RefreshAsync(),
        _ => Performance.RefreshAsync()
    };

    private Task RefreshDisplay()
    {
        Display.Refresh();
        return Task.CompletedTask;
    }

    private Task RefreshAudio()
    {
        Audio.Refresh();
        return Task.CompletedTask;
    }

    private void Report(string message, bool isError)
    {
        Status = message;
        StatusIsError = isError;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
