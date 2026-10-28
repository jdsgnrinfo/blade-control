using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>
/// The Performance page: the mode for each power source, Custom's CPU and GPU
/// levels, max fan speed and the charge limit. The rules are Razer Helper's
/// (see <see cref="PowerProfileRules"/>): each power source keeps its own
/// profile; the one the laptop is on now is applied as it is picked, the other
/// is only saved and applied when the charger is plugged in or pulled out.
/// </summary>
internal sealed partial class PerformanceViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string, bool> _report;
    private readonly Dictionary<bool, PowerProfile> _profiles;
    private readonly HashSet<PerformanceMode> _unsupportedModes = [];
    // Every two seconds plugged in, every ten on battery.
    private readonly PacedTimer _silentTimer;
    private PerformanceState _state = PerformanceState.Unknown;
    private bool? _appliedSource;
    private bool _busy;
    private bool _reapplyRequested;
    private bool _editingPluggedIn;

    /// <param name="report">Shows a message under the page tabs; true marks it as an error.</param>
    public PerformanceViewModel(AppServices services, Action<string, bool> report)
    {
        _services = services;
        _report = report;
        _silentTimer = new PacedTimer(services.PowerSource, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        Monitor = new SystemMonitorViewModel(services);

        _profiles = new Dictionary<bool, PowerProfile>
        {
            [true] = PowerProfileRules.Sanitize(services.Settings.PluggedInProfile ?? new PowerProfile(), pluggedIn: true),
            [false] = PowerProfileRules.Sanitize(services.Settings.OnBatteryProfile ?? PowerProfile.DefaultOnBattery, pluggedIn: false)
        };

        _editingPluggedIn = IsPluggedIn;

        // A laptop with no known high-performance mode never gets Performance sent.
        if (!HasPerformanceMode)
            _unsupportedModes.Add(PerformanceMode.Performance);
        _chargeLimit = BatteryLimitRange.Normalize(services.Settings.BatteryChargeLimit ?? 80);
        _chargeLimitOn = services.Settings.BatteryChargeLimit is int saved && saved < BatteryLimitRange.NoLimit;

        services.PowerSource.PowerSourceChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(OnPowerSourceChanged);

        // In Silent, the boost stays off in whatever power plan is active; with
        // the lid option on and a monitor connected, so does the lid action.
        _silentTimer.Tick += (_, _) =>
        {
            _services.SilentBoost.Recheck();
            _services.LidClose.Recheck();
            _services.CpuTuning.Recheck();

            // The plan was switched (the laptop panel, Windows): the card follows.
            if (_services.CpuTuning.IsOnRazerBladePlan() != _onRazerBladePlan)
                RefreshCpu();
        };
        _silentTimer.Start();

        _services.CpuTuning.Recheck();
        RefreshCpu();
    }

    /// <summary>The system monitor, under Battery health.</summary>
    public SystemMonitorViewModel Monitor { get; }

    // ===== What the page shows =====

    public bool IsPluggedIn => PowerProfileRules.TreatAsPluggedIn(_services.PowerSource.IsPluggedIn);

    /// <summary>Which power source's profile the page shows: the "Plugged In | On Battery" switch.</summary>
    public bool EditingPluggedIn
    {
        get => _editingPluggedIn;
        set
        {
            if (SetProperty(ref _editingPluggedIn, value))
                RefreshAll();
        }
    }

    public bool EditingOnBattery
    {
        get => !_editingPluggedIn;
        set => EditingPluggedIn = !value;
    }

    /// <summary>The page shows the source the laptop is on now, so the EC's own state.</summary>
    private bool EditingCurrentSource => _editingPluggedIn == IsPluggedIn;

    private PerformanceMode? ShownMode => EditingCurrentSource
        ? _state.Mode ?? _profiles[_editingPluggedIn].Mode
        : _profiles[_editingPluggedIn].Mode;

    private CpuBoost? ShownCpu => EditingCurrentSource && _state.Cpu is not null ? _state.Cpu : _profiles[_editingPluggedIn].Cpu;

    private GpuBoost? ShownGpu => EditingCurrentSource && _state.Gpu is not null ? _state.Gpu : _profiles[_editingPluggedIn].Gpu;

    public bool IsSilent { get => ShownMode == PerformanceMode.Silent; set => Pick(value, PerformanceMode.Silent); }

    public bool IsBalanced { get => ShownMode == PerformanceMode.Balanced; set => Pick(value, PerformanceMode.Balanced); }

    public bool IsCustom { get => ShownMode == PerformanceMode.Custom; set => Pick(value, PerformanceMode.Custom); }

    public bool IsPerformance { get => ShownMode == PerformanceMode.Performance; set => Pick(value, PerformanceMode.Performance); }

    /// <summary>Only the models known to have a high-performance mode show it (see RazerLaptopModel.PerformanceWire).</summary>
    public bool HasPerformanceMode => _services.Model?.PerformanceWire is not null;

    /// <summary>Plugged-in only, like Custom.</summary>
    public bool PerformanceOffered => HasPerformanceMode && IsModeOffered(PerformanceMode.Performance);

    /// <summary>Three mode tiles, or four with Performance.</summary>
    public int ModeColumns => HasPerformanceMode ? 4 : 3;

    /// <summary>The page's width from which its cards go side by side: four tiles need about 130 px each.</summary>
    public double ColumnsMinimumWidth => HasPerformanceMode ? 1200 : 940;

    public bool SilentOffered => IsModeOffered(PerformanceMode.Silent);

    public bool BalancedOffered => IsModeOffered(PerformanceMode.Balanced);

    /// <summary>Custom is plugged-in only, as in Synapse.</summary>
    public bool CustomOffered => IsModeOffered(PerformanceMode.Custom);

    private static readonly CpuBoost[] CpuLevels = [CpuBoost.Low, CpuBoost.Medium, CpuBoost.High, CpuBoost.Boost];
    private static readonly GpuBoost[] GpuLevels = [GpuBoost.Low, GpuBoost.Medium, GpuBoost.High];

    public IReadOnlyList<string> CpuOptions { get; } = [L.T("Low"), L.T("Medium"), L.T("High"), L.T("Boost")];

    public IReadOnlyList<string> GpuOptions { get; } = [L.T("Low"), L.T("Medium"), L.T("High")];

    /// <summary>The CPU level in its dropdown; High until one is known.</summary>
    public int CpuIndex
    {
        get => Math.Max(0, Array.IndexOf(CpuLevels, ShownCpu ?? CpuBoost.High));
        set => PickCpu(value >= 0, CpuLevels[Math.Clamp(value, 0, CpuLevels.Length - 1)]);
    }

    /// <summary>The GPU level in its dropdown; High until one is known.</summary>
    public int GpuIndex
    {
        get => Math.Max(0, Array.IndexOf(GpuLevels, ShownGpu ?? GpuBoost.High));
        set => PickGpu(value >= 0, GpuLevels[Math.Clamp(value, 0, GpuLevels.Length - 1)]);
    }

    /// <summary>The levels can be changed: Custom on the source the laptop is on now (the EC only takes them in Custom).</summary>
    public bool CanChangeLevels => !_busy && EditingCurrentSource && PowerProfileRules.CanChangeBoost(_state, IsPluggedIn);

    public bool IsBusy => _busy;

    /// <summary>Whether this laptop has a max fan speed at all.</summary>
    public bool HasMaxFan => _services.MaxFanMethod != MaxFanMethod.None;

    public bool MaxFan
    {
        get => _state.MaxFan == true;
        set
        {
            if (value != MaxFan)
                TakeOverFromApp();

            _ = SetMaxFanAsync(value);
        }
    }

    public bool CanUseMaxFan =>
        !_busy && EditingCurrentSource &&
        (MaxFan || PowerProfileRules.CanUseMaxFan(_state, IsPluggedIn, _services.MaxFanMethod));

    public string MaxFanHint => _services.MaxFanMethod == MaxFanMethod.ManualFan
        ? L.T("Both fans at full speed for better cooling. Plugged in only, in any mode but Silent.")
        : L.T("Both fans at full speed for better cooling. Plugged in only, in Custom mode.");

    public bool AutoSwitchProfiles
    {
        get => _services.Settings.AutoSwitchProfiles;
        set
        {
            if (value == AutoSwitchProfiles)
                return;

            _services.UpdateSettings(settings => settings with { AutoSwitchProfiles = value });
            OnPropertyChanged();

            if (value && IsPluggedIn != _appliedSource)
                _ = ApplyActiveProfileAsync();
        }
    }

    // ===== The Processor card: EPP, core parking, frequency scaling and turbo boost, for each power source, in every mode =====

    private bool _cpuEditingPluggedIn = true;
    private readonly Dictionary<CpuSetting, double> _cpuValues = Enum.GetValues<CpuSetting>().ToDictionary(setting => setting, _ => 0.0);
    private bool _hasCpuSettings;
    private bool _onRazerBladePlan;

    /// <summary>Which power source the sliders show: the card's "Plugged In | On Battery" switch.</summary>
    public bool CpuEditingPluggedIn
    {
        get => _cpuEditingPluggedIn;
        set
        {
            if (SetProperty(ref _cpuEditingPluggedIn, value))
                RefreshCpu();
        }
    }

    public bool CpuEditingOnBattery
    {
        get => !_cpuEditingPluggedIn;
        set => CpuEditingPluggedIn = !value;
    }

    /// <summary>
    /// The sliders work only on the Razer Blade power plan and when its settings
    /// can be read: a plan the user made or tuned is left as it is.
    /// </summary>
    public bool HasCpuSettings => _hasCpuSettings && _onRazerBladePlan;

    /// <summary>Another power plan is active: the card says the settings wait for the Razer Blade plan.</summary>
    public bool ShowCpuPlanNote => !_onRazerBladePlan;

    public double EppValue { get => _cpuValues[CpuSetting.Epp]; set => SetCpu(CpuSetting.Epp, value); }

    public double CoreParkingValue { get => _cpuValues[CpuSetting.CoreParking]; set => SetCpu(CpuSetting.CoreParking, value); }

    public double FrequencyScalingValue { get => _cpuValues[CpuSetting.FrequencyScaling]; set => SetCpu(CpuSetting.FrequencyScaling, value); }

    public double TurboBoostValue { get => _cpuValues[CpuSetting.TurboBoost]; set => SetCpu(CpuSetting.TurboBoost, value); }

    /// <summary>
    /// Turbo boost is the mode's to decide in Silent while Silent turns the
    /// boost off (Settings, Experimental); otherwise its slider shows.
    /// </summary>
    public bool ShowTurboBoost =>
        !(_services.Settings.SilentTurnsBoostOff && ModeFor(_cpuEditingPluggedIn) == PerformanceMode.Silent);

    private PerformanceMode? ModeFor(bool pluggedIn) => pluggedIn == IsPluggedIn
        ? _state.Mode ?? _profiles[pluggedIn].Mode
        : _profiles[pluggedIn].Mode;

    private void SetCpu(CpuSetting setting, double value)
    {
        if (_cpuValues[setting] == value)
            return;

        _cpuValues[setting] = value;
        OnPropertyChanged(PropertyFor(setting));
    }

    private static string PropertyFor(CpuSetting setting) => setting switch
    {
        CpuSetting.Epp => nameof(EppValue),
        CpuSetting.CoreParking => nameof(CoreParkingValue),
        CpuSetting.FrequencyScaling => nameof(FrequencyScalingValue),
        _ => nameof(TurboBoostValue)
    };

    /// <summary>Reads the active plan again: each slider shows the chosen value, or the plan's own until one is chosen.</summary>
    public void RefreshCpu()
    {
        var chosen = _services.CpuTuning.For(_cpuEditingPluggedIn);
        _hasCpuSettings = false;
        _onRazerBladePlan = _services.CpuTuning.IsOnRazerBladePlan();

        foreach (var setting in Enum.GetValues<CpuSetting>())
        {
            var plan = _services.CpuTuning.ReadPlan(setting, _cpuEditingPluggedIn);
            _hasCpuSettings |= plan is not null;
            SetCpu(setting, chosen.Get(setting) ?? plan ?? CpuTuning.Minimum(setting));
        }

        OnPropertyChanged(nameof(HasCpuSettings));
        OnPropertyChanged(nameof(ShowCpuPlanNote));
        OnPropertyChanged(nameof(ShowTurboBoost));
    }

    /// <summary>A slider was let go: keeps its value for that power source and writes it.</summary>
    public void CommitCpu(CpuSetting setting)
    {
        var value = CpuTuning.Snap(setting, _cpuValues[setting]);
        var forPluggedIn = _cpuEditingPluggedIn;

        if (_services.CpuTuning.Set(setting, forPluggedIn, value))
        {
            SaveCpu();
            _report(L.F(forPluggedIn ? "{0} plugged in: {1}%." : "{0} on battery: {1}%.", CpuName(setting), value), false);
        }
        else
            _report(L.T("Windows did not accept the processor setting."), true);

        RefreshCpu();
    }

    /// <summary>Forgets this power source's processor settings and gives the plan the Razer Blade values.</summary>
    [RelayCommand]
    private void ResetCpu()
    {
        var forPluggedIn = _cpuEditingPluggedIn;

        if (_services.CpuTuning.Reset(forPluggedIn))
            _report(forPluggedIn ? L.T("Processor settings plugged in reset.") : L.T("Processor settings on battery reset."), false);
        else
            _report(L.T("Windows did not accept the processor setting."), true);

        SaveCpu();
        RefreshCpu();
    }

    private void SaveCpu()
    {
        var tuning = _services.CpuTuning;
        _services.UpdateSettings(settings => settings with
        {
            CpuPluggedIn = tuning.PluggedIn.HasAny() ? tuning.PluggedIn : null,
            CpuOnBattery = tuning.OnBattery.HasAny() ? tuning.OnBattery : null
        });
    }

    private static string CpuName(CpuSetting setting) => setting switch
    {
        CpuSetting.Epp => L.T("EPP"),
        CpuSetting.CoreParking => L.T("Core parking"),
        CpuSetting.FrequencyScaling => L.T("Frequency scaling"),
        _ => L.T("Turbo boost")
    };


    /// <summary>
    /// Battery mode: on battery the laptop goes to Silent, whatever the battery
    /// profile says, and the rest (screen, keyboard, Windows' effects, Energy
    /// saver, the GPU offer) follows through <see cref="BatteryModeChanged"/>.
    /// Plugging in applies the plugged-in profile again.
    /// </summary>
    public bool BatteryMode
    {
        get => _services.Settings.BatteryMode;
        set
        {
            if (value == BatteryMode)
                return;

            _services.UpdateSettings(settings => settings with { BatteryMode = value });
            AppLog.Info($"Battery mode turned {(value ? "on" : "off")}.");
            OnPropertyChanged();
            BatteryModeChanged?.Invoke(this, value);

            // On battery now: Silent at once, or the battery profile back.
            if (!IsPluggedIn && _appOverride is null)
                _ = ApplyActiveProfileAsync();
        }
    }

    /// <summary>Battery mode was turned on or off.</summary>
    public event EventHandler<bool>? BatteryModeChanged;

    // ===== Battery health =====

    /// <summary>The controller really applies a charge limit (some older firmware only pretends to).</summary>
    public bool HasChargeLimit => _services.Model?.HasChargeLimit == true;

    private bool _chargeLimitOn;

    public bool ChargeLimitOn
    {
        get => _chargeLimitOn;
        set
        {
            if (SetProperty(ref _chargeLimitOn, value))
                _ = ApplyChargeLimitAsync();
        }
    }

    private int _chargeLimit;

    /// <summary>The limit on the slider, 60 to 99; it is sent when the slider is let go (see <see cref="CommitChargeLimit"/>).</summary>
    public int ChargeLimit
    {
        get => _chargeLimit;
        set => SetProperty(ref _chargeLimit, Math.Clamp(value, BatteryLimitRange.Minimum, BatteryLimitRange.Maximum - 1));
    }

    public int ChargeLimitMinimum => BatteryLimitRange.Minimum;

    public int ChargeLimitMaximum => BatteryLimitRange.Maximum - 1;

    public void CommitChargeLimit()
    {
        if (_chargeLimitOn)
            _ = ApplyChargeLimitAsync();
    }

    // ===== Startup, refresh and exit =====

    /// <summary>Applies the profile for the current power source: at startup.</summary>
    public async Task StartAsync()
    {
        await ApplyActiveProfileAsync(atStartup: true);

        // Put back the charge limit the user chose: the EC forgets it when it loses power.
        if (HasChargeLimit && _services.Settings.BatteryChargeLimit is int)
            await ApplyChargeLimitAsync(quiet: true);
    }

    /// <summary>Shows what the EC is actually in (Fn keys or another program may have changed it).</summary>
    public async Task RefreshAsync()
    {
        if (_busy)
            return;

        RefreshCpu();

        SetBusy(true);

        try
        {
            ShowState(await _services.Performance.ReadStateAsync());
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the performance mode.", exception);
            _report(L.T("Could not read the performance mode."), true);
        }

        SetBusy(false);
    }

    /// <summary>
    /// Called as the app exits. The controller keeps the fans flat out after
    /// the app has gone, so Max is put back on automatic here.
    /// </summary>
    public void TurnOffMaxFanBeforeExit()
    {
        _silentTimer.Stop();

        if (_state.MaxFan != true)
            return;

        try
        {
            _services.Performance.SetMaxFan(false);
            AppLog.Info("Max fan speed turned off as Blade Control exits.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not turn off max fan speed on exit.", exception);
        }
    }

    // ===== Picking =====

    private bool IsModeOffered(PerformanceMode mode) =>
        !_unsupportedModes.Contains(mode) && PowerProfileRules.IsModeAllowed(mode, _editingPluggedIn);

    private void Pick(bool chosen, PerformanceMode mode)
    {
        if (!chosen || _busy || !IsModeOffered(mode))
        {
            RefreshLater();
            return;
        }

        if (EditingCurrentSource)
        {
            if (mode != _state.Mode)
            {
                TakeOverFromApp();
                _ = ChangeProfileAsync(profile => profile with { Mode = mode }, L.T("Could not change the performance mode."));
            }
            else
            {
                RefreshLater();
            }

            return;
        }

        // The other power source: only saved, applied when the laptop switches to it.
        if (_profiles[_editingPluggedIn].Mode == mode)
            return;

        var edited = PowerProfileRules.Sanitize(_profiles[_editingPluggedIn] with { Mode = mode }, _editingPluggedIn);
        Remember(_editingPluggedIn, edited);
        _report(_editingPluggedIn
            ? L.T("Saved. It is used when the charger is plugged in.")
            : L.T("Saved. It is used when the laptop runs on battery."), false);
        RefreshAll();
    }

    /// <summary>
    /// A mode shortcut (Ctrl+Shift+F1/F2/F3), maybe pressed in a game: the
    /// mode for the power source the laptop is on now, as its button would,
    /// and how it went, for the notice.
    /// </summary>
    /// <summary>The mode the laptop is in now, for the tray menu's check mark.</summary>
    public PerformanceMode? CurrentMode => _state.Mode;

    /// <summary>Whether a mode can be picked now: this laptop has it, and the power source allows it (Custom only plugged in).</summary>
    public bool CanPick(PerformanceMode mode) =>
        !_unsupportedModes.Contains(mode) && PowerProfileRules.IsModeAllowed(mode, IsPluggedIn);

    public async Task<(bool Applied, string Status)> SelectFromShortcutAsync(PerformanceMode mode)
    {
        if (_unsupportedModes.Contains(mode))
            return (false, L.T("Not supported on this laptop"));

        if (!PowerProfileRules.IsModeAllowed(mode, IsPluggedIn))
            return (false, L.T("Needs to be plugged in"));

        if (_state.Mode == mode)
            return (true, L.T("Active"));

        if (_busy)
            return (false, L.T("Busy, try again in a moment"));

        // The page follows the laptop's own power source, which is the one changed.
        TakeOverFromApp();
        EditingPluggedIn = IsPluggedIn;
        await ChangeProfileAsync(profile => profile with { Mode = mode }, L.T("Could not change the performance mode."));

        if (_state.Mode == mode)
            return (true, L.T("Active"));

        return (false, _unsupportedModes.Contains(mode)
            ? L.T("Not supported on this laptop")
            : L.T("Could not change the performance mode."));
    }

    private void PickCpu(bool chosen, CpuBoost level)
    {
        if (chosen && CanChangeLevels && level != _state.Cpu)
        {
            TakeOverFromApp();
            _ = ChangeProfileAsync(profile => profile with { Cpu = level }, L.T("Could not change the boost level."));
        }
        else
            RefreshLater();
    }

    private void PickGpu(bool chosen, GpuBoost level)
    {
        if (chosen && CanChangeLevels && level != _state.Gpu)
        {
            TakeOverFromApp();
            _ = ChangeProfileAsync(profile => profile with { Gpu = level }, L.T("Could not change the boost level."));
        }
        else
            RefreshLater();
    }

    private async Task SetMaxFanAsync(bool enabled, bool quiet = false)
    {
        if (_busy || enabled == MaxFan ||
            (enabled && !PowerProfileRules.CanUseMaxFan(_state, IsPluggedIn, _services.MaxFanMethod)))
        {
            RefreshLater();
            return;
        }

        await RunAsync(
            () => _services.Performance.SetMaxFanAsync(enabled),
            L.T("Could not change max fan speed."),
            _ => { }, // Not part of a profile: a one-off.
            quiet);
    }

    // ===== App profiles =====

    // What the profile of the app running now asks for, on top of the power
    // source's profile; null when no app profile holds the mode. Never saved:
    // the power sources' own profiles stay as they were.
    private (PowerProfile? Profile, bool? MaxFan)? _appOverride;
    private bool _appTurnedMaxFanOn;

    /// <summary>
    /// The mode was changed by hand (a button, a shortcut) while an app's
    /// profile held it: the app's profile lets go, and nothing is put back
    /// when the app closes.
    /// </summary>
    public event EventHandler? AppOverrideTakenOver;

    /// <summary>
    /// Puts an app profile's mode and fans on, without saving them, or with
    /// both null takes them off again and puts back the power source's own
    /// profile. Returns whether it all applied, and how it went for the notice.
    /// A mode the power source does not allow (Custom on battery) is refused
    /// and the power source's own profile stays.
    /// </summary>
    public async Task<(bool Applied, string Status)> SetAppOverrideAsync(PowerProfile? profile, bool? maxFan)
    {
        await WaitUntilIdleAsync();

        var had = _appOverride is not null;
        _appOverride = profile is null && maxFan is null ? null : (profile, maxFan);

        if (_appOverride is null)
        {
            if (!had)
                return (true, L.T("Active"));

            await ApplyActiveProfileAsync();

            if (_appTurnedMaxFanOn && _state.MaxFan == true)
                await SetMaxFanAsync(false, quiet: true);

            _appTurnedMaxFanOn = false;
            return (true, L.T("Active"));
        }

        var source = IsPluggedIn;
        var (effective, refusal) = EffectiveProfile(source);

        await RunAsync(
            () => _services.Performance.ApplyProfileAsync(effective),
            L.T("Could not apply the power profile."),
            _ => _appliedSource = source,
            quiet: true);

        if (profile?.Mode is { } wanted && refusal is null && _state.Mode != wanted)
            refusal = L.T("Could not change the performance mode.");

        refusal ??= await ApplyAppFanAsync();
        return (refusal is null, refusal ?? L.T("Active"));
    }

    // The power source's profile, with the app's mode on it when the source allows it.
    private (PowerProfile Profile, string? Refusal) EffectiveProfile(bool pluggedIn)
    {
        var own = _profiles[pluggedIn];

        // Battery mode puts Silent in place of the battery profile's mode.
        if (!pluggedIn && BatteryMode && !_unsupportedModes.Contains(PerformanceMode.Silent))
            own = PowerProfileRules.Sanitize(own with { Mode = PerformanceMode.Silent }, pluggedIn: false);

        if (_appOverride?.Profile is not { Mode: { } mode } wanted)
            return (own, null);

        if (_unsupportedModes.Contains(mode))
            return (own, L.T("Not supported on this laptop"));

        if (!PowerProfileRules.IsModeAllowed(mode, pluggedIn))
            return (own, L.T("Needs to be plugged in"));

        return (wanted, null);
    }

    // The app's fan choice, when the mode allows it; returns why not otherwise.
    private async Task<string?> ApplyAppFanAsync()
    {
        if (_appOverride?.MaxFan is not bool fan || _state.MaxFan == fan || !HasMaxFan)
            return null;

        if (fan && !PowerProfileRules.CanUseMaxFan(_state, IsPluggedIn, _services.MaxFanMethod))
            return IsPluggedIn ? L.T("Max fan speed is not available in this mode") : L.T("Needs to be plugged in");

        await SetMaxFanAsync(fan, quiet: true);

        if (fan && _state.MaxFan == true)
            _appTurnedMaxFanOn = true;

        return _state.MaxFan == fan ? null : L.T("Could not change max fan speed.");
    }

    private void TakeOverFromApp()
    {
        if (_appOverride is null)
            return;

        _appOverride = null;
        _appTurnedMaxFanOn = false;
        AppOverrideTakenOver?.Invoke(this, EventArgs.Empty);
    }

    // Another change to the EC may be under way; an app profile waits for it, a few seconds at most.
    private async Task WaitUntilIdleAsync()
    {
        for (var tries = 0; _busy && tries < 50; tries++)
            await Task.Delay(100);
    }

    // ===== Talking to the EC =====

    private void OnPowerSourceChanged()
    {
        // Follow the laptop: the page shows the source it is on now.
        _editingPluggedIn = IsPluggedIn;
        OnPropertyChanged(nameof(IsPluggedIn));
        RefreshAll();

        if ((AutoSwitchProfiles || BatteryMode) && IsPluggedIn != _appliedSource)
            _ = ApplyActiveProfileAsync();
        else if (!IsPluggedIn && _state.MaxFan == true)
            _ = SetMaxFanAsync(false); // Max is a plugged-in setting.
        else if (_services.Performance.FollowsPowerSource && _state.Mode is PerformanceMode.Silent or PerformanceMode.Balanced)
            _ = KeepModeAsync(_state.Mode.Value);
    }

    // The same mode, sent again for the new power source: on a laptop with
    // battery modes, Silent and Balanced are sent differently on battery.
    private Task KeepModeAsync(PerformanceMode mode)
    {
        if (_busy)
        {
            _reapplyRequested = true;
            return Task.CompletedTask;
        }

        return RunAsync(
            () => _services.Performance.ApplyProfileAsync(new PowerProfile(mode)),
            L.T("Could not apply the power profile."),
            _ => _appliedSource = IsPluggedIn,
            quiet: true);
    }

    /// <param name="atStartup">Also turn off a Max left on from before the app started: it is never part of a profile.</param>
    private Task ApplyActiveProfileAsync(bool atStartup = false)
    {
        if (_busy)
        {
            // Something else is talking to the EC; picked up when it is done.
            _reapplyRequested = true;
            return Task.CompletedTask;
        }

        var source = IsPluggedIn;
        var (profile, _) = EffectiveProfile(source);

        return RunAsync(
            async () =>
            {
                var state = await _services.Performance.ApplyProfileAsync(profile);

                return (atStartup || !source) && state.MaxFan == true
                    ? await _services.Performance.SetMaxFanAsync(false)
                    : state;
            },
            L.T("Could not apply the power profile."),
            _ => _appliedSource = source,
            quiet: atStartup || _appOverride is not null);
    }

    private Task ChangeProfileAsync(Func<PowerProfile, PowerProfile> edit, string failureMessage)
    {
        var source = IsPluggedIn;
        var edited = edit(_profiles[source]);

        return RunAsync(
            () => _services.Performance.ApplyProfileAsync(edited),
            failureMessage,
            state =>
            {
                // Asked for a mode and the firmware settled on another: this laptop does not have it.
                if (edited.Mode is PerformanceMode asked && state.Mode is not null && state.Mode != asked)
                {
                    _unsupportedModes.Add(asked);
                    AppLog.Error($"The EC was asked for {asked} but stayed in {state.Mode}; {asked} is marked as not supported.");
                    _report(L.T("This laptop does not support that mode."), true);
                }

                Remember(source, PowerProfileRules.Remember(edited, state));
                _appliedSource = source;
            });
    }

    // Runs an EC operation off the UI thread, then shows the state it ended in.
    // On failure the page goes back to what the EC last confirmed.
    private async Task RunAsync(
        Func<Task<PerformanceState>> operation,
        string failureMessage,
        Action<PerformanceState> onSuccess,
        bool quiet = false)
    {
        SetBusy(true);

        try
        {
            var state = await Task.Run(operation);
            ShowState(state);
            AppLog.Info($"Performance state is now {state.Mode} (CPU {state.Cpu}, GPU {state.Gpu}, max fan {state.MaxFan?.ToString() ?? "n/a"}).");

            if (!quiet)
                _report(L.T("Performance profile applied."), false);

            onSuccess(state);
        }
        catch (Exception exception)
        {
            AppLog.Error(failureMessage, exception);
            _report(failureMessage, true);
            ShowState(_state);
        }

        SetBusy(false);

        // A plug or unplug arrived while busy.
        if (_reapplyRequested)
        {
            _reapplyRequested = false;

            if ((AutoSwitchProfiles || BatteryMode) && IsPluggedIn != _appliedSource)
                await ApplyActiveProfileAsync();
            else if (_services.Performance.FollowsPowerSource && _state.Mode is PerformanceMode.Silent or PerformanceMode.Balanced)
                await KeepModeAsync(_state.Mode.Value);
        }
    }

    private void Remember(bool pluggedIn, PowerProfile profile)
    {
        _profiles[pluggedIn] = profile;
        _services.UpdateSettings(settings => pluggedIn
            ? settings with { PluggedInProfile = profile }
            : settings with { OnBatteryProfile = profile });
    }

    private void ShowState(PerformanceState state)
    {
        _state = state;
        _services.SilentBoost.OnModeChanged(state.Mode);
        ModeChanged?.Invoke(this, state.Mode);
        RefreshAll();
    }

    /// <summary>
    /// Shows a state without sending anything to the laptop or touching the
    /// power plan: for screenshots of a mode the laptop is not in.
    /// </summary>
    public void Preview(PerformanceState state)
    {
        _state = state;
        RefreshAll();
    }

    /// <summary>Raised with the mode the laptop is in after every confirmed change.</summary>
    public event EventHandler<PerformanceMode?>? ModeChanged;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshAll();
    }

    // Every shown value depends on the state, the source and the busy flag: all are raised together.
    private void RefreshAll() => OnPropertyChanged(string.Empty);

    // From inside a binding's setter WPF ignores a change to that same value, so a
    // refused click is undone once the setter has returned.
    private void RefreshLater() =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshAll, DispatcherPriority.Background);

    private async Task ApplyChargeLimitAsync(bool quiet = false)
    {
        var limit = _chargeLimitOn ? _chargeLimit : BatteryLimitRange.NoLimit;

        try
        {
            await _services.ChargeLimit.SetChargeLimitAsync(limit);
            _services.UpdateSettings(settings => settings with { BatteryChargeLimit = limit });

            if (!quiet)
            {
                _report(limit == BatteryLimitRange.NoLimit
                    ? L.T("The battery charges to 100% again.")
                    : L.F("The battery now stops charging at {0}%.", limit), false);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not set the battery charge limit.", exception);
            _report(L.T("Could not set the battery charge limit."), true);
        }
    }
}
