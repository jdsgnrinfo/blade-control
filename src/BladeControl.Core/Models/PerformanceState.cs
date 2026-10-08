namespace BladeControl.Core.Models;

/// <summary>
/// What the EC reports right now. Cpu and Gpu only exist in Custom mode, so
/// they are null in every other mode. MaxFan follows the laptop's
/// <see cref="MaxFanMethod"/>: with the controller flag it is only read in
/// Custom (its read-back is meaningless elsewhere); with the manual fan method
/// it is read in every mode. A null Mode means unreadable or a mode this app
/// does not offer.
/// </summary>
internal sealed record PerformanceState(
    PerformanceMode? Mode,
    CpuBoost? Cpu,
    GpuBoost? Gpu,
    bool? MaxFan = null)
{
    public static PerformanceState Unknown { get; } = new(null, null, null);
}
