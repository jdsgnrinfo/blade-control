namespace BladeControl.Core.Models;

/// <summary>
/// Performance modes Razer Synapse offers on the Blade 16 (2023), plus the
/// 2020 Blades' Gaming, which the laptop may still report but the app no
/// longer offers. The values are the EC's wire
/// bytes. The EC knows a few more (Battery, Hyperboost) that are deliberately
/// left out.
/// </summary>
internal enum PerformanceMode : byte
{
    Balanced = 0,

    // The 2020 Blades' high-performance mode (Synapse called it Gaming).
    // Not offered any more: a saved Gaming profile is applied as Custom.
    Gaming = 1,

    Custom = 4,
    Silent = 5
}
