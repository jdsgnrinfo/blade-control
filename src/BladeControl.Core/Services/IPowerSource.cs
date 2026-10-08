namespace BladeControl.Core.Services;

/// <summary>
/// Whether the laptop is on AC power. An interface so the profile logic can be
/// driven by a fake "plugged in" / "unplugged" in tests.
/// </summary>
internal interface IPowerSource
{
    /// <summary>True on AC, false on battery, null when Windows cannot tell.</summary>
    bool? IsPluggedIn { get; }

    /// <summary>The battery's charge, 0 to 100, or null when Windows cannot tell.</summary>
    int? BatteryPercent => null;

    /// <summary>
    /// Raised on any Windows power status change, which includes battery
    /// percentage updates, so handlers should compare with the last source.
    /// </summary>
    event EventHandler? PowerSourceChanged;
}
