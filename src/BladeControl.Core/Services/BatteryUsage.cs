using System.Globalization;
using System.Xml.Linq;

namespace BladeControl.Core.Services;

/// <summary>What the laptop was doing during a stretch of Windows' battery record.</summary>
internal enum UsageState
{
    /// <summary>On and in use, the screen lit.</summary>
    Active,

    /// <summary>Modern standby: asleep with the screen off but still connected.</summary>
    ScreenOff,

    /// <summary>Asleep (S3) or hibernating.</summary>
    Asleep,

    /// <summary>Anything else, such as the moment the report was made.</summary>
    Other
}

/// <summary>
/// One stretch of Windows' battery record: when it began (local time), how
/// long it lasted, whether the charger was in, what the laptop was doing,
/// and the battery's energy at its start, in mWh.
/// </summary>
internal sealed record UsageEntry(DateTime Start, TimeSpan Duration, bool OnAc, UsageState State, int ChargeMilliwattHours);

/// <summary>A point of the charge line: X across the 24 hours and the charge, both 0 to 1.</summary>
internal sealed record ChargePoint(double X, double Fraction);

/// <summary>A stretch with the charger in, as fractions of the chart's width, 0 to 1.</summary>
internal sealed record ChargingSpan(double From, double To, bool StartsInside);

/// <summary>The last 24 hours, as the Battery page draws them.</summary>
internal sealed record BatteryDay(
    DateTime Start,
    IReadOnlyList<ChargePoint> Line,
    IReadOnlyList<ChargingSpan> Charging,
    TimeSpan ScreenOn,
    TimeSpan ScreenOff,
    TimeSpan Asleep);

/// <summary>
/// Reads the "Recent usage" part of Windows' battery report (what
/// <c>powercfg /batteryreport /xml</c> writes, the same record Settings'
/// battery usage comes from) and turns it into the last 24 hours. Pure, so it
/// is tested without a battery.
/// </summary>
internal static class BatteryUsage
{
    public const int HoursShown = 24;

    // Two stretches closer than this with the charger in are one charge.
    private static readonly TimeSpan JoinGap = TimeSpan.FromMinutes(2);

    /// <summary>The report's usage entries, oldest first; empty when the XML has none or cannot be read.</summary>
    public static IReadOnlyList<UsageEntry> Parse(string xml)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var entries = new List<UsageEntry>();

        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "UsageEntry"))
        {
            if (!DateTime.TryParse((string?)element.Attribute("LocalTimestamp"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !long.TryParse((string?)element.Attribute("Duration"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                || !int.TryParse((string?)element.Attribute("ChargeCapacity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var charge))
            {
                continue;
            }

            entries.Add(new UsageEntry(
                start,
                TimeSpan.FromTicks(Math.Max(0, ticks)),
                (string?)element.Attribute("Ac") == "1",
                StateOf((string?)element.Attribute("EntryType")),
                charge));
        }

        entries.Sort((a, b) => a.Start.CompareTo(b.Start));
        return entries;
    }

    /// <summary>
    /// The 24 hours up to the end of the hour now: the charge as a line through
    /// each entry (a straight line between two), on to the reading now.
    /// <paramref name="fullChargeMilliwattHours"/> is what 100% means.
    /// </summary>
    public static BatteryDay Build(IReadOnlyList<UsageEntry> entries, DateTime now, int? chargeNow, bool? onAcNow, int fullChargeMilliwattHours)
    {
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind).AddHours(1 - HoursShown);
        var end = start.AddHours(HoursShown);

        // The charge over time: each entry's start, then the reading now.
        var points = entries.Select(entry => (At: entry.Start, Charge: (double)entry.ChargeMilliwattHours)).ToList();

        if (chargeNow is int charge && (points.Count == 0 || points[^1].At <= now))
            points.Add((now, charge));

        var total = (end - start).TotalSeconds;
        var line = new List<ChargePoint>();

        // Where the line enters the chart, when the record goes back further.
        if (points.Count > 0 && points[0].At < start && ChargeAt(points, start) is double atStart)
            line.Add(new ChargePoint(0, Fraction(atStart, fullChargeMilliwattHours)));

        line.AddRange(points
            .Where(point => point.At >= start && point.At <= now)
            .Select(point => new ChargePoint((point.At - start).TotalSeconds / total, Fraction(point.Charge, fullChargeMilliwattHours))));

        return new BatteryDay(
            start,
            line,
            ChargingSpans(entries, start, end, now, onAcNow),
            TimeIn(entries, UsageState.Active, start, now),
            TimeIn(entries, UsageState.ScreenOff, start, now),
            TimeIn(entries, UsageState.Asleep, start, now));
    }

    private static double Fraction(double charge, int full) =>
        full > 0 ? Math.Clamp(charge / full, 0, 1) : 0;

    private static UsageState StateOf(string? type) => type switch
    {
        "Active" => UsageState.Active,
        "ConnectedStandby" or "ModernStandby" or "Standby" => UsageState.ScreenOff,
        "Suspend" or "Hibernate" => UsageState.Asleep,
        _ => UsageState.Other
    };

    // A straight line between the readings either side; nothing before the first.
    private static double? ChargeAt(List<(DateTime At, double Charge)> points, DateTime at)
    {
        if (points.Count == 0 || at < points[0].At)
            return null;

        for (var i = 1; i < points.Count; i++)
        {
            if (at > points[i].At)
                continue;

            var (fromAt, from) = points[i - 1];
            var (toAt, to) = points[i];
            var span = (toAt - fromAt).TotalSeconds;

            return span <= 0 ? to : from + (to - from) * (at - fromAt).TotalSeconds / span;
        }

        return points[^1].Charge;
    }

    // Each entry with the charger in, from its start for as long as it lasted (the
    // last one up to now), joined when they follow each other, cut to the chart.
    private static List<ChargingSpan> ChargingSpans(IReadOnlyList<UsageEntry> entries, DateTime start, DateTime end, DateTime now, bool? onAcNow)
    {
        var stretches = new List<(DateTime From, DateTime To)>();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!entry.OnAc)
                continue;

            var to = i == entries.Count - 1 ? (onAcNow == false ? entry.Start + entry.Duration : now) : entry.Start + entry.Duration;

            if (stretches.Count > 0 && entry.Start - stretches[^1].To <= JoinGap)
                stretches[^1] = (stretches[^1].From, Later(stretches[^1].To, to));
            else
                stretches.Add((entry.Start, to));
        }

        // Plugged in since the last entry.
        if (onAcNow == true && (entries.Count == 0 || !entries[^1].OnAc))
        {
            var since = entries.Count == 0 ? start : entries[^1].Start + entries[^1].Duration;
            stretches.Add((since, now));
        }

        var total = (end - start).TotalSeconds;

        return [.. stretches
            .Where(stretch => stretch.To > start && stretch.From < end)
            .Select(stretch => new ChargingSpan(
                Math.Max(0, (stretch.From - start).TotalSeconds / total),
                Math.Min(1, (stretch.To - start).TotalSeconds / total),
                StartsInside: stretch.From >= start))];
    }

    private static TimeSpan TimeIn(IReadOnlyList<UsageEntry> entries, UsageState state, DateTime start, DateTime now)
    {
        var total = TimeSpan.Zero;

        foreach (var entry in entries.Where(entry => entry.State == state))
        {
            var from = Later(entry.Start, start);
            var to = Earlier(entry.Start + entry.Duration, now);

            if (to > from)
                total += to - from;
        }

        return total;
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
