using System.Globalization;

namespace BladeControl.Core.Services;

/// <summary>How the CPU and GPU figures beside the usage rings are written. "--" stands for no reading.</summary>
internal static class HardwareStatsText
{
    public const string NoReading = "--";

    /// <summary>Whole degrees, as elsewhere: "52°C".</summary>
    public static string Celsius(double? celsius) =>
        celsius is { } value ? $"{Round(value)}°C" : NoReading;

    /// <summary>Whole percent: "23%".</summary>
    public static string Percent(double? percent) =>
        percent is { } value ? $"{Round(value)}%" : NoReading;

    /// <summary>A CPU clock, in GHz with two decimals in the reader's culture: "4.52 GHz" or "4,52 GHz".</summary>
    public static string Gigahertz(double? megahertz, CultureInfo culture) =>
        megahertz is { } value ? string.Create(culture, $"{value / 1000:0.00} GHz") : NoReading;

    /// <summary>A GPU clock: "1455 MHz".</summary>
    public static string Megahertz(int? megahertz) =>
        megahertz is { } value ? $"{value} MHz" : NoReading;

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
