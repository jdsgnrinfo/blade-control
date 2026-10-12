using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;

namespace BladeControl.Services;

/// <summary>
/// Everything the window talks to, made once at startup: one EC connection
/// shared by every service that talks to the hardware, the power source, the
/// laptop that was found and the saved settings.
/// </summary>
internal sealed class AppServices : IDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly RazerHidTransport _transport = new();
    private readonly PowerSourceService _powerSource = new();

    public AppServices()
    {
        Settings = _settingsService.Load();
        Model = new DeviceSupportService().TryGetPresentModel(out var model) ? model : null;
        AppLog.Info(Model is null ? "No supported Razer Blade was found." : $"Found {Model.Name}.");

        MaxFanMethod = Model?.MaxFan ?? MaxFanMethod.ControllerFlag;
        Performance = new PerformanceService(_transport, MaxFanMethod);
        FanTelemetry = new FanTelemetryService(_transport);
        EcTemperatures = new EcTemperatureService(_transport);
        FirmwareVersion = new FirmwareVersionService(_transport);
        Charger = new ChargerService(_transport);
        ChargeLimit = new BatteryChargeLimitService(_transport);
        Lighting = new LightingService(_transport, Model?.HasKeyboardColor == true);

        // The keyboard goes dark with the screen (and when idle or low, if chosen) and comes back with it.
        KeyboardLightsOff = new KeyboardScreenOffService(Lighting);

        // Silent keeps the CPU at its base frequency (unless turned off in
        // Settings, Experimental): on the Blade 15
        // Base (2020) the EC's Silent alone runs hotter than Balanced, and it is
        // the boost being off that quiets it. Balanced and Custom give it back.
        SilentBoost = new SilentPlanOverride(
            PowerPlanValue.BoostMode(),
            0, // Boost off.
            "CPU boost",
            Settings.CpuBoostBeforeSilent,
            saved => UpdateSettings(settings => settings with { CpuBoostBeforeSilent = saved }));

        if (!Settings.SilentTurnsBoostOff)
            SilentBoost.SetEnabled(false);

        // With a monitor connected and the charger in, closing the lid can leave the laptop running.
        LidClose = new LidCloseOverride(
            PowerPlanValue.LidAction(),
            Settings.LidActionBeforeKeepRunning,
            saved => UpdateSettings(settings => settings with { LidActionBeforeKeepRunning = saved }));

        StartupRegistration = new RunKeyStartupRegistration(Environment.ProcessPath ?? string.Empty);
        RazerSoftware = new RazerSoftwareManager(
            new RazerServiceManager(new WindowsServiceControl()),
            new RazerLoginEntryManager(new RunKeyLoginEntries()),
            new WindowsRazerApps());
        FactoryReset = new FactoryReset(_settingsService, StartupRegistration, Performance, ChargeLimit);
    }

    public AppSettings Settings { get; private set; }

    /// <summary>The laptop found at startup, or null when none was.</summary>
    public RazerLaptopModel? Model { get; }

    public MaxFanMethod MaxFanMethod { get; }

    public IPowerSource PowerSource => _powerSource;

    public PerformanceService Performance { get; }

    public FanTelemetryService FanTelemetry { get; }

    public EcTemperatureService EcTemperatures { get; }

    public IGpuTemperatureSource GpuTemperature { get; } = new D3dkmtGpuTemperature();

    /// <summary>The controller's firmware version, for the laptop panel.</summary>
    public FirmwareVersionService FirmwareVersion { get; }

    /// <summary>Which charger is in, for the laptop panel.</summary>
    public ChargerService Charger { get; }

    public BatteryChargeLimitService ChargeLimit { get; }

    public LightingService Lighting { get; }

    public KeyboardScreenOffService KeyboardLightsOff { get; }

    public SilentPlanOverride SilentBoost { get; }

    public LidCloseOverride LidClose { get; }

    /// <summary>Starting with Windows, through the Run key.</summary>
    public IStartupRegistration StartupRegistration { get; }

    /// <summary>Razer's own services, startup entries and programs, for Optimize.</summary>
    public RazerSoftwareManager RazerSoftware { get; }

    public FactoryReset FactoryReset { get; }

    /// <summary>Changes the settings and writes them to disk straight away.</summary>
    public void UpdateSettings(Func<AppSettings, AppSettings> edit)
    {
        Settings = edit(Settings);
        _settingsService.Save(Settings);
    }

    public void Dispose()
    {
        _powerSource.Dispose();
        _transport.Dispose();
    }
}
