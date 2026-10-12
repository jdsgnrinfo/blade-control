namespace BladeControl.Core.Models;

/// <summary>
/// Performance modes Razer Synapse offers on the Blade 16 (2023), plus the
/// 2020 Blades' Gaming. The values are the EC's wire
/// bytes. The EC knows a few more (Battery, Hyperboost) that are deliberately
/// left out.
/// </summary>
internal enum PerformanceMode : byte
{
    Balanced = 0,

    // The 2020 Blades' high-performance mode, as Synapse named it: a slightly
    // higher sustained CPU power limit than Balanced. Plugged in only, like Custom.
    Gaming = 1,

    Custom = 4,
    Silent = 5
}
