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
using BladeControl.UI;
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

/// <summary>A color profile to pick, as a tile with its icon (see ProfileIconTemplate).</summary>
internal sealed partial class ProfileChoice(ColorProfile profile, string name, Action<ColorProfile> pick) : ObservableObject
{
    private bool _isSelected;

    public ColorProfile Profile { get; } = profile;

    public string Name { get; } = name;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
                pick(Profile);

            OnPropertyChanged();
        }
    }

    internal void Show(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}

/// <summary>
/// The Display page, as in Razer Helper: the internal screen's refresh rate
/// (Auto follows the charger: the fastest plugged in, 60 Hz on battery, and
/// waits while a fullscreen game has the screen), its color profile, whether
/// an external display is connected, and the dedicated GPU turned off or on.
/// The saved rate and profile are put back at
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

    private readonly Func<Task<DgpuFreeUpOutcome>> _closeGpuApps;

    /// <param name="closeGpuApps">Optimize's "free up the dedicated GPU": lists the apps on it, asks, and closes them.</param>
    public DisplayViewModel(AppServices services, Action<string, bool> report, Func<Task<DgpuFreeUpOutcome>> closeGpuApps)
    {
        _services = services;
        _report = report;
        _closeGpuApps = closeGpuApps;

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

        ProfileChoices = [.. Profiles.Select(profile => new ProfileChoice(profile, NameOf(profile), PickProfile))];
        ShowProfile(_colors.Current);

        _retryTimer.Tick += (_, _) =>
        {
            if (_fullscreenGuard.ReadyToRetry)
                _ = ApplyRateTargetAsync();
        };

        // Windows may reset the screens' ramps a moment after waking or a change of screen.
        _reapplyTimer.Tick += (_, _) =>
        {
            _reapplyTimer.Stop();
            _colors.Reapply();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _gpuActivityTimer = new PacedTimer(services.PowerSource, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
        _gpuActivityTimer.Tick += (_, _) => _ = ShowGpuActivityAsync();
        services.PowerSource.PowerSourceChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_rateOverride is null && _selected?.IsAuto == true)
                    _ = ApplyAutoAsync();
                else if (_services.Settings.BatteryMode)
                    OnBatteryModeChanged(); // A fixed rate gives way to 60 Hz on battery, and comes back.
            });

        // A monitor may already be connected, or gone since a lid action was left changed.
        ShowExternalDisplay();
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

    public IReadOnlyList<ProfileChoice> ProfileChoices { get; }

    /// <summary>The chosen profile's name and what it does, under the tiles.</summary>
    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _profileDescription = string.Empty;

    private void PickProfile(ColorProfile profile)
    {
        // Picked by hand: an app's profile lets go of the colors.
        _colorOverride = null;
        ShowProfile(profile);

        if (profile == _colors.Current)
            return;

        var applied = _colors.Apply(profile);
        AppLog.Info(applied ? $"Color profile: {profile}." : $"Color profile {profile} was not taken by every screen.");
        ProfileStatus = applied ? string.Empty : L.T("This screen does not take it");
        _services.UpdateSettings(settings => settings with { ColorProfile = ColorProfiles.ToSetting(profile) });

        // Also in Windows when that option is on, so the colors stay without the app.
        if (_services.WindowsColors.Enabled)
            _ = Task.Run(() => _services.WindowsColors.Apply(profile));
    }

    /// <summary>
    /// The option to keep the color profile in Windows was turned on or off:
    /// the profile goes in, or comes out, and the app sets its colors again,
    /// since Windows resets the screen's ramp as a profile comes or goes.
    /// </summary>
    public async Task OnColorProfileInWindowsChangedAsync(bool on)
    {
        _services.WindowsColors.Enabled = on;
        var saved = ColorProfiles.Parse(_services.Settings.ColorProfile);
        await Task.Run(() => _services.WindowsColors.Apply(saved));
        await ReapplyColorsLaterAsync();
    }

    private async Task ReapplyColorsLaterAsync()
    {
        await Task.Delay(700);
        _colors.Reapply();
    }

    // ===== App profiles =====

    // The rate and colors an app's profile holds while it runs; null when none
    // does. Neither is saved: the choices on this page stay as they were.
    private int? _rateOverride;
    private ColorProfile? _colorOverride;

    /// <summary>The fixed rates offered, for the Apps page.</summary>
    public IReadOnlyList<int> FixedRates => [.. RefreshChoices.Select(choice => choice.Mode.FixedHz).OfType<int>()];

    /// <summary>
    /// Puts an app's rate and colors on the screen, or, with null, the ones
    /// chosen on this page back. A rate or profile picked by hand meanwhile
    /// has let go of the app's, so it is not touched. False when the screen
    /// did not take it.
    /// </summary>
    public async Task<bool> SetAppOverrideAsync(int? hertz, ColorProfile? colors)
    {
        var applied = true;

        if (colors is { } wanted)
        {
            _colorOverride = wanted;
            applied &= _colors.Apply(wanted);
            ShowProfile(wanted);
        }
        else if (_colorOverride is not null)
        {
            _colorOverride = null;
            var saved = ColorProfiles.Parse(_services.Settings.ColorProfile);
            _colors.Apply(saved);
            ShowProfile(saved);
        }

        if (hertz is not null || _rateOverride is not null)
        {
            _rateOverride = hertz;
            applied &= await ApplyRateTargetAsync();
        }

        return applied;
    }

    /// <summary>
    /// Battery mode was turned on or off. With a fixed rate picked, it puts the
    /// screen at 60 Hz on battery, as Auto would, and the picked rate back when
    /// plugged in; Auto already does both.
    /// </summary>
    public void OnBatteryModeChanged()
    {
        if (_rateOverride is null && _selected is { IsAuto: false })
            _ = ApplyRateTargetAsync();
    }

    /// <summary>As the app exits for good: the fixed rate battery mode replaced with 60 Hz goes back, as nothing would put it back later.</summary>
    public void PutFixedRateBack()
    {
        if (_rateOverride is not null || _selected is not { IsAuto: false, FixedHz: int hz } || BatteryModeRate(hz) == hz)
            return;

        if (_display.GetInternalDisplayInfo()?.RefreshRateHz != hz && _display.TrySetInternalRefreshRate(hz, out var message) is var applied)
            AppLog.Info(applied ? $"Refresh rate back to {hz} Hz as Blade Control exits." : $"Refresh rate {hz} Hz not taken on exit: {message}");
    }

    // A fixed rate, or 60 Hz in its place while battery mode is on and the laptop on battery.
    private int? BatteryModeRate(int? fixedHz) =>
        fixedHz is not null && _services.Settings.BatteryMode && _services.PowerSource.IsPluggedIn == false
            ? DisplayRefreshMode.OnBatteryHz
            : fixedHz;

    // The rate the screen should be at: an app's, else Auto's, else the fixed one picked.
    private async Task<bool> ApplyRateTargetAsync()
    {
        if (_rateOverride is null && _selected?.IsAuto == true)
        {
            await ApplyAutoAsync();
            return RateStatus.Length == 0;
        }

        if ((_rateOverride ?? BatteryModeRate(_selected?.FixedHz)) is not int target)
            return true;

        // Not under a game that already has the screen: tried again when it lets go.
        if (!_fullscreenGuard.CanApplyNow())
        {
            _retryTimer.Start();
            return false;
        }

        if (_display.GetInternalDisplayInfo()?.RefreshRateHz == target)
            return true;

        var (applied, message) = await SetRateAsync(target);
        AppLog.Info(applied ? $"Refresh rate {target} Hz{(_rateOverride is null ? string.Empty : " for an app profile")}." : $"Refresh rate {target} Hz not taken: {message}");
        ShowResolution();
        return applied;
    }

    private void ShowProfile(ColorProfile profile)
    {
        foreach (var choice in ProfileChoices)
            choice.Show(choice.Profile == profile);

        ProfileName = NameOf(profile);
        ProfileDescription = DescriptionOf(profile);
    }

    [ObservableProperty]
    private string _profileStatus = string.Empty;

    // ===== External display =====

    /// <summary>A monitor is plugged in besides the laptop's own panel.</summary>
    [ObservableProperty]
    private bool _hasExternalDisplay;

    [RelayCommand]
    private static void OpenDisplaySettings() => ExternalLinks.OpenDisplaySettings();

    private void ShowExternalDisplay()
    {
        HasExternalDisplay = DisplayTopology.HasExternalDisplay() == true;
        ExternalDisplayDark = HasExternalDisplay && _integratedOnly;
        _services.LidClose.Update(KeepRunningWithLidClosed, HasExternalDisplay);
    }

    /// <summary>
    /// With a monitor connected and the charger in, closing the lid leaves the laptop
    /// running; on battery it still sleeps. The lid action is given back when the
    /// monitor goes, this is turned off or the app exits.
    /// </summary>
    public bool KeepRunningWithLidClosed
    {
        get => _services.Settings.KeepRunningWithLidClosed;
        set
        {
            if (value == KeepRunningWithLidClosed)
                return;

            _services.UpdateSettings(settings => settings with { KeepRunningWithLidClosed = value });
            OnPropertyChanged();
            _services.LidClose.Update(value, HasExternalDisplay);
        }
    }

    /// <summary>A monitor is connected while the dedicated GPU, which drives the video ports, is off: it shows nothing.</summary>
    [ObservableProperty]
    private bool _externalDisplayDark;

    // ===== Integrated graphics only =====

    /// <summary>This laptop has a dedicated (NVIDIA) GPU Windows can turn off.</summary>
    [ObservableProperty]
    private bool _hasDedicatedGpu;

    /// <summary>What the switch does and its warnings are shown: hidden until the link under them is clicked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GraphicsInfoLink))]
    private bool _showGraphicsInfo;

    public string GraphicsInfoLink => ShowGraphicsInfo ? L.T("Less information") : L.T("More information");

    [RelayCommand]
    private void ToggleGraphicsInfo() => ShowGraphicsInfo = !ShowGraphicsInfo;

    /// <summary>The switch is busy asking, closing apps or waiting for Windows.</summary>
    [ObservableProperty]
    private bool _integratedOnlyChanging;

    /// <summary>The line under the switch: which card, its state, or what went wrong.</summary>
    [ObservableProperty]
    private string _integratedOnlyHint = string.Empty;

    private bool _integratedOnly;
    private bool _showingIntegratedOnly;

    /// <summary>
    /// On while the dedicated GPU is turned off in Windows, so everything runs
    /// on the integrated one. Read from the device itself, so it is right after
    /// a restart too. Changing it asks Windows for administrator permission.
    /// </summary>
    public bool IntegratedOnly
    {
        get => _integratedOnly;
        set
        {
            if (_showingIntegratedOnly || IntegratedOnlyChanging || value == _integratedOnly)
                return;

            _integratedOnly = value;
            OnPropertyChanged();
            _ = SetIntegratedOnlyAsync(value);
        }
    }

    private void ShowIntegratedOnly()
    {
        var gpu = DedicatedGpuDevice.Find();
        HasDedicatedGpu = gpu is not null;

        _showingIntegratedOnly = true;
        _integratedOnly = gpu is { Enabled: false };
        OnPropertyChanged(nameof(IntegratedOnly));
        _showingIntegratedOnly = false;
        ExternalDisplayDark = HasExternalDisplay && _integratedOnly;

        IntegratedOnlyHint = gpu is null
            ? L.T("This laptop has no dedicated GPU to turn off.")
            : gpu.Enabled
                ? L.F("{0} is on: Windows uses it when an app needs it.", gpu.Name)
                : L.F("{0} is off: everything runs on the integrated GPU.", gpu.Name);
    }

    // ===== What the dedicated GPU is doing =====

    // Every three seconds plugged in, every ten on battery.
    private readonly PacedTimer _gpuActivityTimer;
    private bool _readingGpuActivity;
    private bool _gpuActivityProblemLogged;

    /// <summary>The live lines under the switch are shown: the GPU is on and was read.</summary>
    [ObservableProperty]
    private bool _showGpuActivity;

    /// <summary>The GPU is in D0 now, so it is using power; asleep otherwise.</summary>
    [ObservableProperty]
    private bool _gpuAwake;

    /// <summary>"Awake now" or "Asleep now"; empty when Windows will not say.</summary>
    [ObservableProperty]
    private string _gpuState = string.Empty;

    /// <summary>The apps using it, by name, or that none is.</summary>
    [ObservableProperty]
    private string _gpuUsers = string.Empty;

    /// <summary>Some of those apps may be asked to close (none of Windows, and no external display).</summary>
    [ObservableProperty]
    private bool _canCloseGpuApps;

    /// <summary>
    /// While the page is open, whether the dedicated GPU is awake and which apps
    /// use it are read every few seconds. Both are only read from Windows, so
    /// looking never wakes the card.
    /// </summary>
    public void SetPageOpen(bool open)
    {
        if (!open)
        {
            _gpuActivityTimer.Stop();
            return;
        }

        _gpuActivityTimer.Start();
        _ = ShowGpuActivityAsync();
    }

    private async Task ShowGpuActivityAsync()
    {
        if (_readingGpuActivity)
            return;

        _readingGpuActivity = true;

        try
        {
            var neverClose = _services.Settings.NeverCloseApps;
            var (gpu, scan) = await Task.Run(() =>
            {
                var found = DedicatedGpuDevice.Find();
                return (found, found is { Enabled: true } ? DgpuScanner.Scan(neverClose) : null);
            });

            ShowGpuActivity = gpu is { Enabled: true } && scan is { GpuFound: true };
            GpuAwake = gpu?.Awake == true;
            GpuState = DgpuText.StateLine(gpu?.Awake);
            GpuUsers = scan is null ? string.Empty : DgpuText.UsersLine(scan.Apps);
            CanCloseGpuApps = scan is not null && scan.Closable.Count > 0;
        }
        catch (Exception exception)
        {
            // Read every few seconds: said once in the log, not every time.
            if (!_gpuActivityProblemLogged)
                AppLog.Error("Could not read what the dedicated GPU is doing.", exception);

            _gpuActivityProblemLogged = true;
            ShowGpuActivity = false;
            CanCloseGpuApps = false;
        }
        finally
        {
            _readingGpuActivity = false;
        }
    }

    /// <summary>Optimize's "free up the dedicated GPU": lists the apps, asks, and closes them.</summary>
    [RelayCommand]
    private async Task CloseGpuAppsAsync()
    {
        var outcome = await _closeGpuApps();

        if (DgpuText.DescribeOutcome(outcome) is { } message)
            _report(message, outcome == DgpuFreeUpOutcome.Failed);

        await ShowGpuActivityAsync();
    }

    // Declining, a refusal or a problem leaves the card as it was and puts the switch back.
    private async Task SetIntegratedOnlyAsync(bool integratedOnly)
    {
        IntegratedOnlyChanging = true;
        string? problem = null;

        try
        {
            problem = integratedOnly ? await TurnDedicatedGpuOffAsync() : await RunDedicatedGpuCommandAsync(enabled: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not change the dedicated GPU.", exception);
            problem = L.T("Windows did not make the change.");
        }

        IntegratedOnlyChanging = false;
        ShowIntegratedOnly();
        _ = ShowGpuActivityAsync();

        if (problem is not null)
            IntegratedOnlyHint = problem;
    }

    // Returns what stopped it, or null once the card is off.
    private async Task<string?> TurnDedicatedGpuOffAsync()
    {
        // The video ports are wired to the dedicated GPU: a monitor on them would go dark.
        if (DisplayTopology.HasExternalDisplay() != false)
            return L.T("Disconnect the external display first: its port runs on the dedicated GPU.");

        var answer = Dialogs.Ask(
            L.T("Turn off the dedicated GPU?\r\n\r\nEverything will run on the integrated GPU, which saves battery, but:\r\n  - the HDMI and USB-C video ports will not show a picture\r\n  - games and 3D apps will run much slower\r\n  - apps using the dedicated GPU will be asked to close first\r\n\r\nIt stays off, even after restarting, until you turn this switch off. Windows will ask for administrator permission."),
            L.T("Integrated graphics only"));

        if (answer != System.Windows.MessageBoxResult.Yes)
            return null;

        IntegratedOnlyHint = L.T("Closing the apps on the dedicated GPU...");
        var outcome = await _closeGpuApps();

        switch (outcome)
        {
            case DgpuFreeUpOutcome.Declined:
                return L.T("Kept on: the apps using it were not closed.");
            case DgpuFreeUpOutcome.ExternalDisplay:
                return L.T("Disconnect the external display first: its port runs on the dedicated GPU.");
            case DgpuFreeUpOutcome.Busy or DgpuFreeUpOutcome.Failed:
                return L.T("Kept on: the apps using it could not be checked.");
            case DgpuFreeUpOutcome.Closed:
                // Give them a moment to let go of the card.
                await Task.Delay(TimeSpan.FromSeconds(3));
                break;
        }

        return await RunDedicatedGpuCommandAsync(enabled: false);
    }

    private async Task<string?> RunDedicatedGpuCommandAsync(bool enabled)
    {
        IntegratedOnlyHint = L.T("Asking Windows (asks for permission)...");
        var result = await ElevatedRunner.RunAsync(DedicatedGpuCommand.Arguments(enabled));

        if (result.UserDeclined)
            return L.T("Kept as it was: administrator permission was not given.");

        switch (result.ExitCode)
        {
            case DedicatedGpuCommand.Success:
                AppLog.Info($"Dedicated GPU turned {(enabled ? "on" : "off")}.");
                _report(enabled ? L.T("The dedicated GPU is on again.") : L.T("The dedicated GPU is off."), false);
                return null;
            case DedicatedGpuCommand.RestartNeeded:
                _report(L.T("Restart Windows to finish the change."), false);
                return L.T("Restart Windows to finish the change.");
            default:
                return L.T("Windows did not make the change.");
        }
    }

    // ===== Life cycle =====

    /// <summary>At startup: the saved color profile and refresh rate go back on.</summary>
    public void Restore()
    {
        var profile = ColorProfiles.Parse(_services.Settings.ColorProfile);

        // Windows first, before the app reads the screen's ramp as "how it was": with the option
        // on, the profile goes in; off, one installed by 0.1.5 comes out and the screen is as it was.
        var hadOneInWindows = _services.Settings.WindowsColorProfile is not null;
        _services.WindowsColors.Apply(profile);
        _colors.Apply(profile);
        ShowProfile(profile);

        // Windows resets the screen's ramp a moment after a profile comes out: the colors go back on after it.
        if (hadOneInWindows && !_services.WindowsColors.Enabled)
            _ = ReapplyColorsLaterAsync();

        var saved = DisplayRefreshMode.Parse(_services.Settings.DisplayMode);

        if (saved is not null && RefreshChoices.Any(choice => choice.Mode == saved))
        {
            Select(saved);

            if (saved.IsAuto)
                _ = ApplyAutoAsync();
            else if (_services.Settings.BatteryMode)
                OnBatteryModeChanged(); // 60 Hz on battery, or the picked rate back after a crash left 60.
        }
        else
        {
            ShowCurrentRate();
        }
    }

    /// <summary>When the page opens: what the screen is at now, any external display, and the dedicated GPU.</summary>
    public void Refresh()
    {
        if (_selected is null)
            ShowCurrentRate();

        if (_fullscreenGuard.ReadyToRetry)
            _ = ApplyRateTargetAsync();

        ShowResolution();
        ShowExternalDisplay();
        ShowIntegratedOnly();
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
        _gpuActivityTimer.Stop();

        // Without the app watching for the monitor to go, the lid gets its own action back.
        _services.LidClose.Restore();
    }

    // ===== Picking a rate =====

    private async void PickRate(DisplayRefreshMode mode)
    {
        // Picked by hand: an app's profile lets go of the rate.
        _rateOverride = null;

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
        ColorProfile.Warm => L.T("Night light"),
        ColorProfile.Cool => L.T("Neutral"),
        ColorProfile.Contrast => L.T("Contrast"),
        _ => L.T("Standard")
    };

    private static string DescriptionOf(ColorProfile profile) => profile switch
    {
        ColorProfile.Warm => L.T("Less blue, easier on the eyes at night."),
        ColorProfile.Cool => L.T("A more balanced white, recommended for photo editing."),
        ColorProfile.Contrast => L.T("Deeper darks and brighter lights."),
        _ => L.T("The colors as the panel shows them.")
    };
}
