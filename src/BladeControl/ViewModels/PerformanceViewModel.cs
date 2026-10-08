using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private readonly DispatcherTimer _silentTimer = new() { Interval = TimeSpan.FromSeconds(2) };
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

        _profiles = new Dictionary<bool, PowerProfile>
        {
            [true] = PowerProfileRules.Sanitize(services.Settings.PluggedInProfile ?? new PowerProfile(), pluggedIn: true),
            [false] = PowerProfileRules.Sanitize(services.Settings.OnBatteryProfile ?? PowerProfile.DefaultOnBattery, pluggedIn: false)
        };

        _editingPluggedIn = IsPluggedIn;
        _chargeLimit = BatteryLimitRange.Normalize(services.Settings.BatteryChargeLimit ?? 80);
        _chargeLimitOn = services.Settings.BatteryChargeLimit is int saved && saved < BatteryLimitRange.NoLimit;

        services.PowerSource.PowerSourceChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(OnPowerSourceChanged);

        // In Silent, the boost stays off in whatever power plan is active.
        _silentTimer.Tick += (_, _) => _services.SilentBoost.Recheck();
        _silentTimer.Start();
    }

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

    public bool SilentOffered => IsModeOffered(PerformanceMode.Silent);

    public bool BalancedOffered => IsModeOffered(PerformanceMode.Balanced);

    /// <summary>Custom is plugged-in only, as in Synapse.</summary>
    public bool CustomOffered => IsModeOffered(PerformanceMode.Custom);

    public bool IsCpuLow { get => ShownCpu == CpuBoost.Low; set => PickCpu(value, CpuBoost.Low); }

    public bool IsCpuMedium { get => ShownCpu == CpuBoost.Medium; set => PickCpu(value, CpuBoost.Medium); }

    public bool IsCpuHigh { get => ShownCpu == CpuBoost.High; set => PickCpu(value, CpuBoost.High); }

    public bool IsCpuBoost { get => ShownCpu == CpuBoost.Boost; set => PickCpu(value, CpuBoost.Boost); }

    public bool IsGpuLow { get => ShownGpu == GpuBoost.Low; set => PickGpu(value, GpuBoost.Low); }

    public bool IsGpuMedium { get => ShownGpu == GpuBoost.Medium; set => PickGpu(value, GpuBoost.Medium); }

    public bool IsGpuHigh { get => ShownGpu == GpuBoost.High; set => PickGpu(value, GpuBoost.High); }

    /// <summary>The levels can be changed: Custom on the source the laptop is on now (the EC only takes them in Custom).</summary>
    public bool CanChangeLevels => !_busy && EditingCurrentSource && PowerProfileRules.CanChangeBoost(_state, IsPluggedIn);

    public bool IsBusy => _busy;

    /// <summary>Whether this laptop has a max fan speed at all.</summary>
    public bool HasMaxFan => _services.MaxFanMethod != MaxFanMethod.None;

    public bool MaxFan
    {
        get => _state.MaxFan == true;
        set => _ = SetMaxFanAsync(value);
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
                _ = ChangeProfileAsync(profile => profile with { Mode = mode }, L.T("Could not change the performance mode."));
            else
                RefreshLater();

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

    private void PickCpu(bool chosen, CpuBoost level)
    {
        if (chosen && CanChangeLevels && level != _state.Cpu)
            _ = ChangeProfileAsync(profile => profile with { Cpu = level }, L.T("Could not change the boost level."));
        else
            RefreshLater();
    }

    private void PickGpu(bool chosen, GpuBoost level)
    {
        if (chosen && CanChangeLevels && level != _state.Gpu)
            _ = ChangeProfileAsync(profile => profile with { Gpu = level }, L.T("Could not change the boost level."));
        else
            RefreshLater();
    }

    private async Task SetMaxFanAsync(bool enabled)
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
            _ => { }); // Not part of a profile: a one-off.
    }

    // ===== Talking to the EC =====

    private void OnPowerSourceChanged()
    {
        // Follow the laptop: the page shows the source it is on now.
        _editingPluggedIn = IsPluggedIn;
        OnPropertyChanged(nameof(IsPluggedIn));
        RefreshAll();

        if (AutoSwitchProfiles && IsPluggedIn != _appliedSource)
            _ = ApplyActiveProfileAsync();
        else if (!IsPluggedIn && _state.MaxFan == true)
            _ = SetMaxFanAsync(false); // Max is a plugged-in setting.
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
        var profile = _profiles[source];

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
            quiet: atStartup);
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

            if (AutoSwitchProfiles && IsPluggedIn != _appliedSource)
                await ApplyActiveProfileAsync();
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
