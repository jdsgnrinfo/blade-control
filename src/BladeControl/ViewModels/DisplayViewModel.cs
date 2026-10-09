using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BladeControl.ViewModels;

/// <summary>A refresh rate to pick: Auto, or a fixed one.</summary>
internal sealed partial class RefreshChoice(DisplayRefreshMode mode, string name, Action<DisplayRefreshMode> pick) : ObservableObject
{
    private bool _isSelected;

    public DisplayRefreshMode Mode { get; } = mode;

    public string Name { get; } = name;

    /// <summary>The rate in blocky digits over a row of bars; "A" for Auto.</summary>
    public System.Windows.Media.Geometry Icon { get; } = UI.RefreshRateIcon.For(mode.FixedHz);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
                pick(Mode);

            OnPropertyChanged();
        }
    }

    internal void Show(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}

/// <summary>A line of the panel's details: what it is, and its value.</summary>
internal sealed record PanelDetail(string Name, string Value);

/// <summary>
/// The Display page, as in Razer Helper: the internal screen's refresh rate
/// (Auto follows the charger: the fastest plugged in, 60 Hz on battery, and
/// waits while a fullscreen game has the screen), its color profile, and what
/// Windows says about the panel. The saved rate and profile are put back at
/// startup, and the profile again whenever Windows may have reset it; it
/// stays on the screens after the app closes.
/// </summary>
internal sealed partial class DisplayViewModel : ObservableObject, IDisposable
{
    private static readonly ColorProfile[] Profiles = [ColorProfile.Standard, ColorProfile.Warm, ColorProfile.Cool, ColorProfile.Contrast];

    private readonly AppServices _services;
    private readonly Action<string, bool> _report;
    private readonly DisplayService _display = new();
    private readonly ColorProfileService _colors = new(new WindowsGammaRamps());
    private readonly FullscreenGuard _fullscreenGuard = new(new WindowsFullscreenDetector());
    private readonly DispatcherTimer _retryTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _reapplyTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly int _fastHz;
    private Task _rateChange = Task.CompletedTask;
    private DisplayRefreshMode? _selected;

    public DisplayViewModel(AppServices services, Action<string, bool> report)
    {
        _services = services;
        _report = report;

        var rates = _display.GetAvailableRefreshRates();
        _fastHz = rates.Count > 0 ? rates[^1] : 120;
        var secondHz = rates.Count >= 2 ? rates[^2] : (int?)null;

        var modes = new List<DisplayRefreshMode> { DisplayRefreshMode.Auto, DisplayRefreshMode.Fixed(DisplayRefreshMode.OnBatteryHz) };

        if (secondHz is int second && second != DisplayRefreshMode.OnBatteryHz)
            modes.Add(DisplayRefreshMode.Fixed(second));

        if (_fastHz != DisplayRefreshMode.OnBatteryHz)
            modes.Add(DisplayRefreshMode.Fixed(_fastHz));

        RefreshChoices = [.. modes.Select(mode => new RefreshChoice(mode, mode.IsAuto ? L.T("Auto") : mode.Label, PickRate))];
        AutoHint = L.F("Auto uses {0} Hz plugged in and {1} Hz on battery, and waits while a fullscreen game has the screen.", _fastHz, DisplayRefreshMode.OnBatteryHz);

        ProfileNames = [.. Profiles.Select(NameOf)];

        _retryTimer.Tick += (_, _) =>
        {
            if (_fullscreenGuard.ReadyToRetry)
                _ = ApplyAutoAsync();
        };

        // Windows may reset the screens' ramps a moment after waking or a change of screen.
        _reapplyTimer.Tick += (_, _) =>
        {
            _reapplyTimer.Stop();
            _colors.Reapply();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        services.PowerSource.PowerSourceChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_selected?.IsAuto == true)
                    _ = ApplyAutoAsync();
            });
    }

    // ===== Refresh rate =====

    public IReadOnlyList<RefreshChoice> RefreshChoices { get; }

    public string AutoHint { get; }

    /// <summary>The internal screen's resolution now, beside the card's title.</summary>
    [ObservableProperty]
    private string _resolution = string.Empty;

    /// <summary>What Auto is waiting for, or why a rate was not taken; empty when all is well.</summary>
    [ObservableProperty]
    private string _rateStatus = string.Empty;

    // ===== Color profile =====

    public IReadOnlyList<string> ProfileNames { get; }

    private int _profileIndex;

    public int ProfileIndex
    {
        get => _profileIndex;
        set
        {
            if (value < 0 || !SetProperty(ref _profileIndex, value))
                return;

            var profile = Profiles[value];

            if (profile == _colors.Current)
                return;

            var applied = _colors.Apply(profile);
            AppLog.Info(applied ? $"Color profile: {profile}." : $"Color profile {profile} was not taken by every screen.");
            ProfileStatus = applied ? string.Empty : L.T("This screen does not take it");
            _services.UpdateSettings(settings => settings with { ColorProfile = ColorProfiles.ToSetting(profile) });
        }
    }

    [ObservableProperty]
    private string _profileStatus = string.Empty;

    // ===== Panel =====

    [ObservableProperty]
    private IReadOnlyList<PanelDetail> _panelDetails = [];

    // ===== External display =====

    /// <summary>A monitor is plugged in besides the laptop's own panel.</summary>
    [ObservableProperty]
    private bool _hasExternalDisplay;

    [RelayCommand]
    private static void OpenDisplaySettings() => ExternalLinks.OpenDisplaySettings();

    private void ShowExternalDisplay() => HasExternalDisplay = DisplayTopology.HasExternalDisplay() == true;

    // ===== Life cycle =====

    /// <summary>At startup: the saved color profile and refresh rate go back on.</summary>
    public void Restore()
    {
        var profile = ColorProfiles.Parse(_services.Settings.ColorProfile);
        _colors.Apply(profile);
        _profileIndex = Array.IndexOf(Profiles, profile);
        OnPropertyChanged(nameof(ProfileIndex));

        var saved = DisplayRefreshMode.Parse(_services.Settings.DisplayMode);

        if (saved is not null && RefreshChoices.Any(choice => choice.Mode == saved))
        {
            Select(saved);

            if (saved.IsAuto)
                _ = ApplyAutoAsync();
        }
        else
        {
            ShowCurrentRate();
        }
    }

    /// <summary>When the page opens: what the screen is at now, and the panel's details.</summary>
    public void Refresh()
    {
        if (_selected is null)
            ShowCurrentRate();

        if (_fullscreenGuard.ReadyToRetry)
            _ = ApplyAutoAsync();

        ShowResolution();
        PanelDetails = ReadPanelDetails();
        ShowExternalDisplay();
    }

    /// <summary>
    /// As the app exits. The color profile stays on the screens: it is the
    /// user's choice, not something the app borrows while it runs.
    /// </summary>
    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _retryTimer.Stop();
        _reapplyTimer.Stop();
    }

    // ===== Picking a rate =====

    private async void PickRate(DisplayRefreshMode mode)
    {
        if (mode.IsAuto)
        {
            Select(mode);
            Save(mode);
            await ApplyAutoAsync();
            return;
        }

        _fullscreenGuard.Cancel();
        _retryTimer.Stop();

        var previous = _selected;
        Select(mode);

        var (applied, message) = await SetRateAsync(mode.FixedHz!.Value);

        if (!applied)
        {
            if (_selected == mode && previous is not null)
                Select(previous);

            RateStatus = message;
            _report(message, true);
            return;
        }

        Save(mode);
        RateStatus = string.Empty;
        ShowResolution();
    }

    private async Task ApplyAutoAsync()
    {
        var pluggedIn = _services.PowerSource.IsPluggedIn;

        if (pluggedIn is null)
        {
            RateStatus = L.T("Auto: power source unavailable.");
            return;
        }

        if (!_fullscreenGuard.CanApplyNow())
        {
            RateStatus = L.T("Auto: waiting for the game");
            _retryTimer.Start();
            return;
        }

        _retryTimer.Stop();
        var target = DisplayRefreshMode.Auto.TargetHz(pluggedIn.Value, _fastHz);

        if (_display.GetInternalDisplayInfo()?.RefreshRateHz != target)
        {
            var (applied, message) = await SetRateAsync(target);

            if (!applied)
            {
                RateStatus = message;
                return;
            }
        }

        RateStatus = string.Empty;
        ShowResolution();
    }

    // One change at a time, in the order asked.
    private Task<(bool Applied, string Message)> SetRateAsync(int hertz)
    {
        var before = _rateChange;
        var change = Task.Run(async () =>
        {
            await before;
            var applied = _display.TrySetInternalRefreshRate(hertz, out var message);
            return (applied, message);
        });

        _rateChange = change;
        return change;
    }

    private void Select(DisplayRefreshMode mode)
    {
        _selected = mode;

        foreach (var choice in RefreshChoices)
            choice.Show(choice.Mode == mode);
    }

    private void Save(DisplayRefreshMode mode) =>
        _services.UpdateSettings(settings => settings with { DisplayMode = mode.Label });

    // With no rate saved, the button of the rate the screen is at now is lit.
    private void ShowCurrentRate()
    {
        var hz = _display.GetInternalDisplayInfo()?.RefreshRateHz;

        if (RefreshChoices.FirstOrDefault(choice => choice.Mode.FixedHz == hz) is { } current)
            Select(current.Mode);

        ShowResolution();
    }

    private void ShowResolution()
    {
        var info = _display.GetInternalDisplayInfo();
        Resolution = info is null ? string.Empty : L.F("Resolution: {0}", DisplayPanelText.Resolution(info.Width, info.Height));
    }

    private void OnDisplayChanged(object? sender, EventArgs e) =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _reapplyTimer.Stop();
            _reapplyTimer.Start();
            ShowResolution();
            ShowExternalDisplay();
        });

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            OnDisplayChanged(sender, e);
    }

    // ===== Texts =====

    private static string NameOf(ColorProfile profile) => profile switch
    {
        ColorProfile.Warm => L.T("Warm"),
        ColorProfile.Cool => L.T("Cool"),
        ColorProfile.Contrast => L.T("Contrast"),
        _ => L.T("Standard")
    };

    private List<PanelDetail> ReadPanelDetails()
    {
        DisplayPanel? panel;

        try
        {
            panel = DisplayPanelReader.Read();
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the panel's details.", exception);
            panel = null;
        }

        var culture = CultureInfo.CurrentCulture;
        var details = new List<PanelDetail>();

        if (panel?.Edid is { } edid)
        {
            var maker = DisplayPanelText.MakerName(edid.MakerCode);
            details.Add(new PanelDetail(L.T("Panel"), edid.Model is { } model ? $"{maker} {model}" : maker));

            if (edid.DiagonalInches is { } inches)
                details.Add(new PanelDetail(L.T("Size"), DisplayPanelText.Diagonal(inches, culture)));

            if (edid is { NativeWidth: { } width, NativeHeight: { } height })
                details.Add(new PanelDetail(L.T("Resolution"), DisplayPanelText.Resolution(width, height)));
        }

        details.Add(new PanelDetail(L.T("Max refresh"), $"{_fastHz} Hz"));

        if (panel?.BitsPerColor is { } bits)
            details.Add(new PanelDetail(L.T("Color depth"), L.F("{0} bits", bits)));

        if (panel?.HdrSupported is { } hdr)
            details.Add(new PanelDetail("HDR", hdr ? (panel.HdrOn == true ? L.T("On") : L.T("Supported")) : L.T("Not supported")));

        return details;
    }
}
