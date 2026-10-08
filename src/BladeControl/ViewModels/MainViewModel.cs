using System.Windows.Threading;
using BladeControl.Core.Localization;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>The sections along the title bar.</summary>
internal enum AppSection
{
    System,
    Audio,
    Optimize,
    Settings
}

/// <summary>The pages of a section, in the row of green tabs.</summary>
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

/// <summary>
/// The window: which section and page are open, the back and forward history
/// across them, and the short message under the tabs after a change.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private readonly List<AppPage> _history = [AppPage.Performance];
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private int _historyIndex;

    public MainViewModel(AppServices services)
    {
        Services = services;
        Performance = new PerformanceViewModel(services, Report);
        Device = new DevicePanelViewModel(services);

        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            Status = string.Empty;
        };
    }

    public AppServices Services { get; }

    public PerformanceViewModel Performance { get; }

    public DevicePanelViewModel Device { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Section), nameof(IsSystem), nameof(IsAudio), nameof(IsOptimize), nameof(IsSettings),
        nameof(IsPerformance), nameof(IsLighting), nameof(IsDisplay), nameof(IsBattery), nameof(HasSubTabs), nameof(PageTitle))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(ForwardCommand))]
    private AppPage _page = AppPage.Performance;

    public AppSection Section => SectionOf(Page);

    public bool HasSubTabs => Section == AppSection.System;

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
    public bool IsSystem { get => Section == AppSection.System; set { if (value && !IsSystem) Open(AppPage.Performance); } }

    public bool IsAudio { get => Section == AppSection.Audio; set { if (value) Open(AppPage.Audio); } }

    public bool IsOptimize { get => Section == AppSection.Optimize; set { if (value) Open(AppPage.Optimize); } }

    public bool IsSettings { get => Section == AppSection.Settings; set { if (value) Open(AppPage.Settings); } }

    public bool IsPerformance { get => Page == AppPage.Performance; set { if (value) Open(AppPage.Performance); } }

    public bool IsLighting { get => Page == AppPage.Lighting; set { if (value) Open(AppPage.Lighting); } }

    public bool IsDisplay { get => Page == AppPage.Display; set { if (value) Open(AppPage.Display); } }

    public bool IsBattery { get => Page == AppPage.Battery; set { if (value) Open(AppPage.Battery); } }

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _statusIsError;

    public void Open(AppPage page)
    {
        if (page == Page)
            return;

        // A new page drops whatever was ahead of this one, as in a browser.
        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(page);
        _historyIndex = _history.Count - 1;
        Page = page;
    }

    private bool CanGoBack() => _historyIndex > 0;

    private bool CanGoForward() => _historyIndex < _history.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Page = _history[--_historyIndex];

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward() => Page = _history[++_historyIndex];

    /// <summary>Reads the laptop again: Fn keys or another program may have changed its mode.</summary>
    [RelayCommand]
    private Task Refresh() => Performance.RefreshAsync();

    private void Report(string message, bool isError)
    {
        Status = message;
        StatusIsError = isError;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private static AppSection SectionOf(AppPage page) => page switch
    {
        AppPage.Audio => AppSection.Audio,
        AppPage.Optimize => AppSection.Optimize,
        AppPage.Settings => AppSection.Settings,
        _ => AppSection.System
    };
}
