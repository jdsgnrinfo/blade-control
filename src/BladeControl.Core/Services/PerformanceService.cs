using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;

namespace BladeControl.Core.Services;

/// <summary>Reads and changes the EC's performance mode and, in Custom, its CPU and GPU boost levels.</summary>
/// <param name="maxFanMethod">How this laptop runs its fans flat out (see <see cref="MaxFanMethod"/>).</param>
/// <param name="mayHaveBatteryModes">
/// A 2025 or later Blade (see <see cref="RazerLaptopModel.MayHaveBatteryModes"/>):
/// on battery, Silent is sent as Battery Saver and Balanced as Battery Balanced,
/// once the firmware is found to keep them.
/// </param>
/// <param name="onBattery">Whether the laptop is on battery now; the battery modes are only sent then.</param>
internal sealed class PerformanceService(
    IRazerTransport transport,
    MaxFanMethod maxFanMethod = MaxFanMethod.ControllerFlag,
    bool mayHaveBatteryModes = false,
    Func<bool>? onBattery = null)
{
    private const byte FanModeAuto = 0x00;
    private const byte FanModeManual = 0x01;

    // The battery modes of the newer firmware, from OpenBlade's captures of
    // Synapse on the Blade 16 (2025 and 2026). They stand in for Silent and
    // Balanced on battery and are shown as those.
    internal const byte BatterySaverWire = 0x03;
    internal const byte BatteryBalancedWire = 0x06;

    // Null until a battery mode is first set; then whether the firmware kept it.
    private bool? _hasBatteryModes;

    // The mode byte the EC reported last, which may be a battery mode.
    private byte? _lastWireMode;

    /// <summary>
    /// The mode sent depends on the power source, so a mode kept across a
    /// plug or unplug is sent again: the laptop may have one and not have
    /// found out yet whether its firmware keeps the battery modes.
    /// </summary>
    public bool FollowsPowerSource => mayHaveBatteryModes && _hasBatteryModes != false;

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

        // Compared by the byte the EC holds, so Balanced plugged in and Battery
        // Balanced on battery count as different and are switched between.
        if (profile.Mode is PerformanceMode mode && _lastWireMode != WireFor(mode))
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

        // The byte the EC holds, so a battery mode is rewritten as itself.
        var wire = _lastWireMode ?? (byte)mode;

        foreach (var zone in Zones)
        {
            transport.SendAndConfirm(
                RazerCommands.SetPerformanceMode,
                [0x01, zone, wire, enabled ? FanModeManual : FanModeAuto],
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

            _lastWireMode = cpuZone;
            return Decode(cpuZone);
        }

        _lastWireMode = null;
        AppLog.Error("The EC reported different performance modes for its two zones.");
        return null;
    }

    /// <summary>The mode a byte from the EC stands for: the battery modes show as Silent and Balanced.</summary>
    internal static PerformanceMode? Decode(byte wire) => wire switch
    {
        BatterySaverWire => PerformanceMode.Silent,
        BatteryBalancedWire => PerformanceMode.Balanced,
        _ => Enum.IsDefined((PerformanceMode)wire) ? (PerformanceMode)wire : null
    };

    // The byte to send for a mode: on battery, on a laptop whose firmware may
    // have them, Silent and Balanced become the battery modes.
    private byte WireFor(PerformanceMode mode) =>
        mayHaveBatteryModes && _hasBatteryModes != false && onBattery?.Invoke() == true
            ? mode switch
            {
                PerformanceMode.Silent => BatterySaverWire,
                PerformanceMode.Balanced => BatteryBalancedWire,
                _ => (byte)mode
            }
            : (byte)mode;

    private byte ReadZoneMode(byte zone)
    {
        var response = transport.Send(RazerCommands.GetPerformanceMode, [0x00, zone, 0x00, 0x00]);

        if (RazerHidPacket.GetArgument(response, 1) != zone)
            throw new InvalidOperationException("The performance-mode response used an unexpected zone.");

        return RazerHidPacket.GetArgument(response, 2);
    }

    private void SetMode(PerformanceMode mode)
    {
        var wire = WireFor(mode);

        if (wire == (byte)mode)
        {
            WriteMode(wire, mode.ToString());
            return;
        }

        // A battery mode. The first one sent is read back: firmware without
        // them refuses it or keeps another mode, and from then on gets the
        // plain modes, as before.
        var name = wire == BatterySaverWire ? "Battery Saver" : "Battery Balanced";

        try
        {
            WriteMode(wire, name);
        }
        catch (Exception exception) when (exception is InvalidOperationException or RazerCommandNotSupportedException)
        {
            Settle(false, name);
            WriteMode((byte)mode, mode.ToString());
            return;
        }

        if (_hasBatteryModes is not null)
            return;

        var kept = ReadZoneMode(Zones[0]) == wire && ReadZoneMode(Zones[1]) == wire;
        Settle(kept, name);

        if (!kept)
            WriteMode((byte)mode, mode.ToString());
    }

    private void Settle(bool kept, string name)
    {
        _hasBatteryModes = kept;
        AppLog.Info(kept
            ? $"The firmware kept {name}: on battery, Silent and Balanced are sent as Battery Saver and Battery Balanced. BIOS {BiosInfo.Describe()}."
            : $"The firmware did not keep {name}: Silent and Balanced are sent as they are. BIOS {BiosInfo.Describe()}.");
    }

    private void WriteMode(byte wire, string name)
    {
        foreach (var zone in Zones)
        {
            transport.SendAndConfirm(
                RazerCommands.SetPerformanceMode,
                [0x01, zone, wire, FanModeAuto],
                $"performance mode {name} for zone {zone}");
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
