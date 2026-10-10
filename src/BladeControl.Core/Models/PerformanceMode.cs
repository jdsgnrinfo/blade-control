namespace BladeControl.Core.Models;

/// <summary>
/// Performance modes Razer Synapse offers on the Blade 16 (2023), plus the
/// 2020 Blades' Gaming, offered in Balanced's place by an experimental
/// setting. The values are the EC's wire
/// bytes. The EC knows a few more (Battery, Hyperboost) that are deliberately
/// left out.
/// </summary>
internal enum PerformanceMode : byte
{
    Balanced = 0,

    // The 2020 Blades' high-performance mode (Synapse called it Gaming).
    // Offered only with the experimental setting, plugged in, in Balanced's place.
    Gaming = 1,

    Custom = 4,
    Silent = 5
}
