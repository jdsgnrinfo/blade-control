using BladeControl.Core.Models;

namespace BladeControl.Core.Hardware;

/// <summary>One Razer laptop this app can look for, by its USB product id.</summary>
/// <param name="Verified">
/// Someone has confirmed the commands in <see cref="RazerCommands"/> actually
/// work on this model. An unverified entry is still tried the same way: a
/// command the firmware does not implement fails cleanly with
/// <see cref="RazerCommandNotSupportedException"/> instead of doing something
/// unexpected.
/// </param>
/// <param name="HasChargeLimit">
/// The controller really applies <see cref="RazerCommands.SetBatteryChargeLimit"/>.
/// Some older firmware echoes every class 0x07 command as if it took it and
/// then does nothing, so this cannot be detected from the reply.
/// </param>
/// <param name="MaxFan">
/// How this model runs its fans flat out. The controller flag
/// (<see cref="RazerCommands.SetMaxFan"/>) has the same class 0x07 caveat as
/// <paramref name="HasChargeLimit"/>: where it is only echoed, the older
/// manual fan method may work instead.
/// </param>
/// <param name="HasKeyboardColor">
/// A static keyboard color of the user's choice, through the standard matrix
/// effect (<see cref="RazerCommands.SetStandardEffect"/>). Checked on the model
/// by eye: white sent this way lit the keyboard white.
/// </param>
/// <param name="HasFanSpeeds">
/// The controller reports real fan speeds (<see cref="RazerCommands.GetActualFanRpm"/>).
/// Where it does not, the fan speed line is left out from the start rather
/// than removed on the first reading.
/// </param>
/// <param name="HasWaveEffect">
/// The keyboard can show a wave. A single-zone keyboard (the whole board one
/// light) cannot, whichever command is sent, so Wave is not offered there.
/// </param>
/// <param name="MayHaveBatteryModes">
/// The 2025 and later Blades, whose newer BIOS and firmware give the battery
/// modes of their own: Battery Saver and Battery Balanced. On battery, that
/// firmware runs Silent and Balanced alike; Synapse offers only the battery
/// modes there. Whether this laptop's firmware has them is checked the first
/// time one is set (see <see cref="Services.PerformanceService"/>).
/// </param>
/// <param name="PerformanceWire">
/// The byte of this model's high-performance mode, where it is known to have
/// one: 1 on the 2020 Blades (Synapse's Gaming), 2 on the 2023 Blades
/// (Performance). Null leaves the Performance mode out. Plugged in only.
/// </param>
internal sealed record RazerLaptopModel(
    int ProductId,
    string Name,
    bool Verified,
    bool HasChargeLimit = true,
    MaxFanMethod MaxFan = MaxFanMethod.ControllerFlag,
    bool HasKeyboardColor = false,
    bool HasFanSpeeds = true,
    bool HasWaveEffect = true,
    bool MayHaveBatteryModes = false,
    byte? PerformanceWire = null);
