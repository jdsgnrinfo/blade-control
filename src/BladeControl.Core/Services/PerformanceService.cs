using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>Reads and changes the EC's performance mode and, in Custom, its CPU and GPU boost levels.</summary>
/// <param name="maxFanMethod">How this laptop runs its fans flat out (see <see cref="MaxFanMethod"/>).</param>
internal sealed class PerformanceService(IRazerTransport transport, MaxFanMethod maxFanMethod = MaxFanMethod.ControllerFlag)
{
    private const byte FanModeAuto = 0x00;
    private const byte FanModeManual = 0x01;

    // The speed the manual fan method asks for, in hundreds of RPM: 10000, far
    // above what these fans can reach (about 5000 on a Blade 15 Base 2020), so
    // the controller drives them at full power, their true maximum, whatever
    // it is. The model does not report real fan speeds, so the ceiling cannot
    // be read; the controller stores any target without clamping it. 7000 was
    // held without complaint on that laptop; 10000 is being tried by the
    // user, at their own request, to hear whether it changes anything. If
    // the fans misbehave, go back to 70.
    private const byte ManualMaxRpmHundreds = 100;
    private const byte CpuCluster = 0x01;
    private const byte GpuCluster = 0x02;
    private const byte MaxFanOn = 0x02;
    private const byte MaxFanOff = 0x00;

    // The EC keeps the mode per zone (CPU and GPU) and expects both written.
    private static readonly byte[] Zones = [0x01, 0x02];

    /// <summary>What the EC holds now. Boost levels are only read in Custom mode.</summary>
    public Task<PerformanceState> ReadStateAsync() => Task.Run(ReadState);

    /// <summary>
    /// Brings the EC to <paramref name="profile"/>, writing only what differs,
    /// and returns the state it ended up in. Fields the profile leaves null
    /// are left alone.
    /// </summary>
    public Task<PerformanceState> ApplyProfileAsync(PowerProfile profile) => Task.Run(() => ApplyProfile(profile));

    internal PerformanceState ApplyProfile(PowerProfile profile)
    {
        var state = ReadState();

        if (profile.Mode is PerformanceMode mode && state.Mode != mode)
        {
            SetMode(mode);
            state = ReadState();
        }

        // Boost levels only exist in Custom mode; the EC ignores them elsewhere.
        if (state.Mode == PerformanceMode.Custom)
        {
            var wrote = false;

            if (profile.Cpu is CpuBoost cpu && state.Cpu != cpu)
            {
                SetBoost(CpuCluster, (byte)cpu);
                wrote = true;
            }

            if (profile.Gpu is GpuBoost gpu && state.Gpu != gpu)
            {
                SetBoost(GpuCluster, (byte)gpu);
                wrote = true;
            }

            if (wrote)
                state = ReadState();
        }

        return state;
    }

    internal PerformanceState ReadState()
    {
        var mode = GetMode();

        // The manual fan method's state is the fan mode the controller reports
        // in every performance mode, not only in Custom.
        if (maxFanMethod == MaxFanMethod.ManualFan && mode != PerformanceMode.Custom)
            return new PerformanceState(mode, null, null, ReadManualFan());

        if (mode != PerformanceMode.Custom)
            return new PerformanceState(mode, null, null);

        var cpu = ReadBoost(CpuCluster);
        var gpu = ReadBoost(GpuCluster);

        return new PerformanceState(
            mode,
            Enum.IsDefined((CpuBoost)cpu) ? (CpuBoost)cpu : null,
            Enum.IsDefined((GpuBoost)gpu) ? (GpuBoost)gpu : null,
            maxFanMethod switch
            {
                MaxFanMethod.ControllerFlag => ReadMaxFan(),
                MaxFanMethod.ManualFan => ReadManualFan(),
                _ => null
            });
    }

    /// <summary>
    /// Turns max fan speed (both fans flat out) on or off. It is a one-off, not
    /// part of a profile, and the EC clears it by itself when the mode leaves
    /// Custom. Returns the state the EC reports afterwards.
    /// </summary>
    public Task<PerformanceState> SetMaxFanAsync(bool enabled) => Task.Run(() => SetMaxFan(enabled));

    internal PerformanceState SetMaxFan(bool enabled)
    {
        switch (maxFanMethod)
        {
            case MaxFanMethod.ControllerFlag:
                SetMaxFanFlag(enabled);
                break;
            case MaxFanMethod.ManualFan:
                SetManualFan(enabled);
                break;
            default:
                throw new InvalidOperationException("This laptop has no max fan speed.");
        }

        var state = ReadState();

        // Report what the EC says rather than what was asked for.
        if (state.MaxFan != enabled)
            AppLog.Error($"The EC accepted max fan speed {(enabled ? "on" : "off")} but reports {state.MaxFan?.ToString() ?? "unknown"}.");

        return state;
    }

    private void SetMaxFanFlag(bool enabled)
    {
        // The EC rejects this command outside Custom, so it is never sent from
        // any other mode, whatever the caller believes the mode is.
        if (GetMode() != PerformanceMode.Custom)
            throw new InvalidOperationException("Max fan speed can only be changed in Custom mode.");

        transport.SendAndConfirm(
            RazerCommands.SetMaxFan,
            [enabled ? MaxFanOn : MaxFanOff],
            $"max fan speed {(enabled ? "on" : "off")}");
    }

    // Each fan is switched to manual (or back to automatic) by rewriting the
    // current mode with the fan byte set, then given its fixed speed. The mode
    // itself is left as it is.
    private void SetManualFan(bool enabled)
    {
        // Silent holds the fans down whatever is asked, so Max would only look
        // on there. Turning it off is allowed in any known mode.
        if (GetMode() is not PerformanceMode mode || (enabled && mode == PerformanceMode.Silent))
            throw new InvalidOperationException("Max fan speed is not available in this mode.");

        foreach (var zone in Zones)
        {
            transport.SendAndConfirm(
                RazerCommands.SetPerformanceMode,
                [0x01, zone, (byte)mode, enabled ? FanModeManual : FanModeAuto],
                $"fan mode {(enabled ? "manual" : "automatic")} for zone {zone}");

            if (enabled)
            {
                transport.SendAndConfirm(
                    RazerCommands.SetFanRpm,
                    [0x01, zone, ManualMaxRpmHundreds],
                    $"fan speed for zone {zone}");
            }
        }
    }

    // Both fans manual means Max is on, both automatic that it is off.
    private bool? ReadManualFan()
    {
        var cpu = ReadZoneFanMode(Zones[0]);
        var gpu = ReadZoneFanMode(Zones[1]);

        return cpu == gpu
            ? cpu switch
            {
                FanModeManual => true,
                FanModeAuto => false,
                _ => null
            }
            : null;
    }

    private byte ReadZoneFanMode(byte zone) =>
        RazerHidPacket.GetArgument(transport.Send(RazerCommands.GetPerformanceMode, [0x00, zone, 0x00, 0x00]), 3);

    // 2 turns it on and 0 off (checked on a Blade 16). Only meaningful in
    // Custom mode, which is the only place this is called from.
    private bool? ReadMaxFan() =>
        RazerHidPacket.GetArgument(transport.Send(RazerCommands.GetMaxFan, [0x00]), 0) switch
        {
            MaxFanOn => true,
            MaxFanOff => false,
            _ => null
        };

    private PerformanceMode? GetMode()
    {
        // The zones are separate HID round trips, so a mode change landing
        // between them (Fn+P, Synapse) makes them disagree for a moment.
        // Re-read once before treating a disagreement as real.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var cpuZone = ReadZoneMode(Zones[0]);
            var gpuZone = ReadZoneMode(Zones[1]);

            if (cpuZone != gpuZone)
                continue;

            return Enum.IsDefined((PerformanceMode)cpuZone)
                ? (PerformanceMode)cpuZone
                : null;
        }

        AppLog.Error("The EC reported different performance modes for its two zones.");
        return null;
    }

    private byte ReadZoneMode(byte zone)
    {
        var response = transport.Send(RazerCommands.GetPerformanceMode, [0x00, zone, 0x00, 0x00]);

        if (RazerHidPacket.GetArgument(response, 1) != zone)
            throw new InvalidOperationException("The performance-mode response used an unexpected zone.");

        return RazerHidPacket.GetArgument(response, 2);
    }

    private void SetMode(PerformanceMode mode)
    {
        foreach (var zone in Zones)
        {
            transport.SendAndConfirm(
                RazerCommands.SetPerformanceMode,
                [0x01, zone, (byte)mode, FanModeAuto],
                $"performance mode {mode} for zone {zone}");
        }
    }

    private byte ReadBoost(byte cluster)
    {
        var response = transport.Send(RazerCommands.GetBoost, [0x00, cluster, 0x00]);

        if (RazerHidPacket.GetArgument(response, 1) != cluster)
            throw new InvalidOperationException("The boost response used an unexpected cluster.");

        return RazerHidPacket.GetArgument(response, 2);
    }

    private void SetBoost(byte cluster, byte level)
    {
        // Callers already know the mode, but a stale read must never turn into
        // a boost write outside Custom, where the EC drops or misapplies it.
        if (GetMode() != PerformanceMode.Custom)
            throw new InvalidOperationException("Boost levels can only be changed in Custom mode.");

        transport.SendAndConfirm(
            RazerCommands.SetBoost,
            [0x01, cluster, level],
            $"boost level {level} for cluster {cluster}");
    }
}
