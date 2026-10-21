using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>Windows' power mode for the power source in use.</summary>
internal interface IPowerMode
{
    /// <summary>True in Balanced, the mode with no overlay on the plan.</summary>
    bool IsBalanced();

    void SetBalanced();
}

/// <summary>
/// The processor settings of the Performance page (EPP, core parking,
/// frequency scaling, turbo boost), one set for each power source, in steps
/// of 10. Once chosen they are kept in whatever power plan is active, in every
/// mode: <see cref="Recheck"/> writes them back when a plan is switched or
/// installed. Windows' power mode is kept on Balanced too, since Best
/// performance and Best power efficiency replace the plan's processor
/// settings with their own. Unchosen, nothing is touched.
/// </summary>
internal sealed class CpuTuning(
    Func<CpuSetting, IPowerPlanValue> planValue,
    IPowerMode mode,
    Func<bool> isPluggedIn,
    CpuValues? pluggedIn,
    CpuValues? onBattery)
{
    public const int Step = 10;

    public CpuValues PluggedIn { get; private set; } = Normalize(pluggedIn);

    public CpuValues OnBattery { get; private set; } = Normalize(onBattery);

    /// <summary>The lowest a setting goes: a frequency cap of 0 would stop the processor.</summary>
    public static int Minimum(CpuSetting setting) => setting == CpuSetting.FrequencyScaling ? Step : 0;

    /// <summary>The nearest step, 0 (or 10) to 100.</summary>
    public static int Snap(CpuSetting setting, double value) =>
        (int)Math.Clamp(Math.Round(value / Step) * Step, Minimum(setting), 100);

    /// <summary>The Razer Blade plan's values, plugged in and on battery, which a reset goes back to.</summary>
    public static (uint PluggedIn, uint OnBattery) Default(CpuSetting setting) => setting switch
    {
        CpuSetting.Epp => (20, 80),
        CpuSetting.CoreParking => (100, 30),
        CpuSetting.FrequencyScaling => (100, 100),
        _ => (60, 40)
    };

    private static CpuValues Normalize(CpuValues? values)
    {
        var normalized = new CpuValues();

        foreach (var setting in Enum.GetValues<CpuSetting>())
            if (values?.Get(setting) is int chosen)
                normalized = normalized.With(setting, Snap(setting, chosen));

        return normalized;
    }

    public CpuValues For(bool forPluggedIn) => forPluggedIn ? PluggedIn : OnBattery;

    /// <summary>The active plan's value, snapped to the steps, or null when it cannot be read.</summary>
    public int? ReadPlan(CpuSetting setting, bool forPluggedIn)
    {
        try
        {
            var current = planValue(setting).Read();
            return Snap(setting, forPluggedIn ? current.PluggedIn : current.OnBattery);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not read the processor setting {setting}.", exception);
            return null;
        }
    }

    /// <summary>Chooses one setting for one power source and writes it now. False when Windows refused it.</summary>
    public bool Set(CpuSetting setting, bool forPluggedIn, int value)
    {
        var snapped = Snap(setting, value);

        if (forPluggedIn)
            PluggedIn = PluggedIn.With(setting, snapped);
        else
            OnBattery = OnBattery.With(setting, snapped);

        AppLog.Info($"Processor {setting} {(forPluggedIn ? "plugged in" : "on battery")} set to {snapped}.");
        return Recheck();
    }

    /// <summary>Forgets one power source's choices and gives the plan the Razer Blade plan's values for it.</summary>
    public bool Reset(bool forPluggedIn)
    {
        if (forPluggedIn)
            PluggedIn = new CpuValues();
        else
            OnBattery = new CpuValues();

        var done = true;

        foreach (var setting in Enum.GetValues<CpuSetting>())
        {
            try
            {
                var value = planValue(setting);
                var current = value.Read();
                var standard = Default(setting);

                value.Write(
                    current.Scheme,
                    forPluggedIn ? standard.PluggedIn : current.PluggedIn,
                    forPluggedIn ? current.OnBattery : standard.OnBattery);
            }
            catch (Exception exception)
            {
                AppLog.Error($"Could not reset the processor setting {setting}.", exception);
                done = false;
            }
        }

        AppLog.Info($"Processor settings {(forPluggedIn ? "plugged in" : "on battery")} reset.");
        return Recheck() && done;
    }

    /// <summary>Writes the chosen values into the active plan where it has others, and keeps the power mode on Balanced. False when that failed.</summary>
    public bool Recheck()
    {
        var done = true;

        foreach (var setting in Enum.GetValues<CpuSetting>())
        {
            var wantPluggedIn = PluggedIn.Get(setting);
            var wantOnBattery = OnBattery.Get(setting);

            if (wantPluggedIn is null && wantOnBattery is null)
                continue;

            try
            {
                var value = planValue(setting);
                var current = value.Read();
                var pluggedIn = (uint)(wantPluggedIn ?? (int)current.PluggedIn);
                var onBattery = (uint)(wantOnBattery ?? (int)current.OnBattery);

                if (current.PluggedIn != pluggedIn || current.OnBattery != onBattery)
                    value.Write(current.Scheme, pluggedIn, onBattery);
            }
            catch (Exception exception)
            {
                AppLog.Error($"Could not set the processor setting {setting}.", exception);
                done = false;
            }
        }

        // The power mode is per power source; only the one in use can be changed.
        try
        {
            if (For(isPluggedIn()).HasAny() && !mode.IsBalanced())
            {
                mode.SetBalanced();
                AppLog.Info("Windows' power mode set to Balanced, so the processor settings apply.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not set Windows' power mode to Balanced.", exception);
            done = false;
        }

        return done;
    }
}
