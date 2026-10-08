namespace BladeControl.Core.Models;

/// <summary>How a laptop runs both fans flat out, if it can.</summary>
internal enum MaxFanMethod
{
    /// <summary>
    /// The controller's own max fan flag (<c>0x070F</c>), as on the Blade 16
    /// (2023). Only honored in Custom mode; the controller clears it when the
    /// mode leaves Custom.
    /// </summary>
    ControllerFlag,

    /// <summary>
    /// The 2018 to 2020 Blades' way: each fan is switched to manual in the
    /// performance mode command and given a fixed speed (<c>0x0D01</c>).
    /// Checked by ear on a Blade 15 Base (2020): it works in Balanced, Gaming
    /// and Custom, but Silent holds the fans down whatever is asked.
    /// </summary>
    ManualFan,

    /// <summary>The laptop has no max fan speed this app can set.</summary>
    None
}
