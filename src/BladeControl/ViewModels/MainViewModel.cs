using System.Windows.Threading;
using BladeControl.Core.Localization;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>The pages, in the row of green tabs; Settings opens from the gear in the title bar.</summary>
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

    public DevicePanelViewModel Device { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPerformance), nameof(IsLighting), nameof(IsDisplay), nameof(IsBattery),
        nameof(IsAudio), nameof(IsOptimize), nameof(IsSettings), nameof(PageTitle), nameof(ShowsComingSoon))]
    private AppPage _page = AppPage.Performance;

    /// <summary>The pages not built yet show that they are coming.</summary>
    public bool ShowsComingSoon => Page is not (AppPage.Performance or AppPage.Lighting);

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

    // The Lighting page follows the Fn brightness keys only while it is open.
    partial void OnPageChanged(AppPage value) => Lighting.SetPageOpen(value == AppPage.Lighting);

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _statusIsError;

    /// <summary>Reads the laptop again: Fn keys or another program may have changed its mode.</summary>
    [RelayCommand]
    private Task Refresh() => Page == AppPage.Lighting ? Lighting.RefreshAsync() : Performance.RefreshAsync();

    private void Report(string message, bool isError)
    {
        Status = message;
        StatusIsError = isError;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
