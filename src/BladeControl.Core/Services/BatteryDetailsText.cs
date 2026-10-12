using System.Globalization;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;

namespace BladeControl.Core.Services;

/// <summary>
/// Turns the battery driver's raw figures into the text of the Battery
/// details window's header and cards. Pure, so all of it can be tested
/// without a battery.
/// </summary>
internal static class BatteryDetailsText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The charge as a percentage of a full charge, as the taskbar shows it; null without both figures.</summary>
    public static int? ChargePercent(BatteryDetails battery) =>
        battery is { RemainingMilliwattHours: { } now, FullChargeMilliwattHours: { } full } && full > 0
            ? Math.Clamp((int)Math.Round(100.0 * now / full), 0, 100)
            : null;

    /// <summary>The power on one line, as the tray icon shows it on hover: "Using 12.4 W". Null without a matching rate.</summary>
    public static string? PowerLine(BatteryDetails? battery) =>
        battery is not null && PowerParts(battery) is var (watts, direction) ? $"{direction} {watts}" : null;

    /// <summary>The Power card: the watts, and whether they are going in or out. Null without a matching rate.</summary>
    public static (string Value, string Caption)? PowerParts(BatteryDetails battery) => battery.RateMilliwatts switch
    {
        > 0 and var rate when battery.Charging => (Watts(rate), L.T("Charging")),
        < 0 and var rate when battery.Discharging => (Watts(-rate), L.T("Using")),
        _ => null
    };

    /// <summary>The Time card: how long, and to what ("left" or "to full"). Null when there is nothing to count down.</summary>
    public static (string Value, string Caption)? TimeParts(BatteryDetails battery)
    {
        if (battery.Discharging)
        {
            var seconds = battery.EstimatedSecondsLeft ??
                (battery is { RemainingMilliwattHours: { } left, RateMilliwatts: < 0 and var rate }
                    ? (int)(left * 3600.0 / -rate)
                    : null);

            return seconds is { } s ? (Duration(s), L.T("left")) : null;
        }

        return battery is { Charging: true, RemainingMilliwattHours: { } now, FullChargeMilliwattHours: { } full, RateMilliwatts: > 0 and var charging } && full > now
            ? (Duration((int)((full - now) * 3600.0 / charging)), L.T("to full"))
            : null;
    }

    /// <summary>The Charge card: the energy stored now, and out of how much. Null without a reading.</summary>
    public static (string Value, string? Caption)? ChargeParts(BatteryDetails battery) =>
        battery.RemainingMilliwattHours is { } now
            ? (WattHours(now), battery.FullChargeMilliwattHours is { } full and > 0 ? L.F("of {0}", WattHours(full)) : null)
            : null;

    /// <summary>The Health card's caption: what a full charge holds now against the capacity when new. Null without both.</summary>
    public static string? HealthCaption(BatteryDetails battery) =>
        battery is { FullChargeMilliwattHours: { } full, DesignMilliwattHours: { } design }
            ? L.F("{0} of {1} (factory)", WattHours(full), WattHours(design))
            : null;

    /// <summary>The compact Health card's note: what a full charge holds now and held when new, "57.3 / 65.0 Wh". Null without both.</summary>
    public static string? HealthShort(BatteryDetails battery) =>
        battery is { FullChargeMilliwattHours: { } full, DesignMilliwattHours: { } design }
            ? $"{(full / 1000.0).ToString("0.0", Invariant)} / {WattHours(design)}"
            : null;

    /// <summary>The energy stored now, "57.3 Wh". Null without a reading.</summary>
    public static string? Stored(BatteryDetails battery) =>
        battery.RemainingMilliwattHours is { } now ? WattHours(now) : null;

    /// <summary>The header's short status: "Charging", "On battery" or "Plugged in".</summary>
    public static string HeaderStatus(BatteryDetails battery) =>
        L.T(Status(battery) == "Plugged in, not charging" ? "Plugged in" : Status(battery));

    /// <summary>The voltage as shown, for example "16.55 V". Null without a reading.</summary>
    public static string? Voltage(BatteryDetails battery) =>
        battery.VoltageMillivolts is { } millivolts
            ? $"{(millivolts / 1000.0).ToString("0.00", Invariant)} V"
            : null;

    /// <summary>Full-charge capacity as a percentage of the capacity when new, or null without both.</summary>
    public static int? HealthPercent(BatteryDetails battery) =>
        battery is { FullChargeMilliwattHours: { } full, DesignMilliwattHours: { } design } && design > 0
            ? (int)Math.Round(100.0 * full / design)
            : null;

    internal static string Status(BatteryDetails battery) => battery switch
    {
        { Charging: true } => "Charging",
        { Discharging: true } => "On battery",
        { PluggedIn: true } => "Plugged in, not charging",
        _ => "On battery"
    };

    internal static string? Identity(BatteryDetails battery)
    {
        var name = string.Join(" ", new[] { battery.Manufacturer, battery.Name }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var chemistry = ChemistryName(battery.Chemistry);

        return (name.Length, chemistry) switch
        {
            (0, null) => null,
            (0, _) => chemistry,
            (_, null) => name,
            _ => $"{name} · {chemistry}"
        };
    }

    // The driver gives four letters, sometimes cut short ("Li-I" for "Li-Ion").
    internal static string? ChemistryName(string? code) => ChemistryNameInEnglish(code) is { } name ? L.T(name) : null;

    private static string? ChemistryNameInEnglish(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "LION" or "LI-I" or "LI-ION" or "LIION" => "Lithium-ion",
        "LIP" or "LIPO" or "LI-P" => "Lithium polymer",
        "NIMH" => "Nickel-metal hydride",
        "NICD" => "Nickel-cadmium",
        "PBAC" => "Lead-acid",
        _ => code
    };

    internal static string Duration(int seconds)
    {
        var minutes = Math.Max(1, (int)Math.Round(seconds / 60.0));
        var hours = minutes / 60;

        return hours > 0 ? $"{hours} h {minutes % 60} min" : $"{minutes} min";
    }

    private static string Watts(int milliwatts) =>
        $"{(milliwatts / 1000.0).ToString("0.0", Invariant)} W";

    private static string WattHours(int milliwattHours) =>
        $"{(milliwattHours / 1000.0).ToString("0.0", Invariant)} Wh";
}
