namespace BladeControl.Core.Models;

/// <summary>
/// Performance modes Razer Synapse offers on the Blade 16 (2023), plus the
/// 2020 Blades' Gaming. The values are the EC's wire bytes. Performance is
/// the high-performance mode of the models known to have one (see
/// <see cref="Hardware.RazerLaptopModel.PerformanceWire"/>): sent as 2, or as
/// 1 (Gaming) on the 2020 Blades, and both read back as Performance. The 2025
/// and later firmware's battery modes (Battery Saver 3, Battery Balanced 6)
/// are sent and read as Silent and Balanced on battery (see
/// <see cref="Services.PerformanceService"/>); Hyperboost is left out.
/// </summary>
internal enum PerformanceMode : byte
{
    Balanced = 0,

    // The 2020 Blades' high-performance mode (Synapse called it Gaming).
    // Read as Performance; a saved Gaming profile is applied as Performance.
    Gaming = 1,

    Performance = 2,
    Custom = 4,
    Silent = 5
}
