using System.Globalization;
using System.Text;

namespace BladeControl.Core.Services;

/// <summary>The equalizer's ten bands: where each sits and how far it moves.</summary>
internal static class EqBands
{
    /// <summary>Each band's frequency, in Hz, low to high.</summary>
    public static readonly int[] Frequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public static int Count => Frequencies.Length;

    /// <summary>How far a band lifts, in dB. Equalizer APO takes any gain; the preamp gives back as much volume as the highest lift.</summary>
    public const double MaxGain = 24;

    /// <summary>How far a band cuts, in dB.</summary>
    public const double MinGain = -24;

    /// <summary>The smallest move, in dB.</summary>
    public const double Step = 0.5;

    /// <summary>A band's frequency as it is written under it: 31, 500, 1k, 16k.</summary>
    public static string Label(int frequency) =>
        frequency >= 1000 ? $"{frequency / 1000}k" : frequency.ToString(CultureInfo.InvariantCulture);

    /// <summary>A gain kept on the band's range, on a whole step.</summary>
    public static double Snap(double gain) =>
        Math.Round(Math.Clamp(gain, MinGain, MaxGain) / Step, MidpointRounding.AwayFromZero) * Step;

    public static double[] Flat() => new double[Count];
}

/// <summary>A named curve to start from: one of the app's, or one the user saved.</summary>
internal sealed record EqPreset(string Name, double[] Gains)
{
    /// <summary>The app's own, in the list's order; their names are translated on screen.</summary>
    public static readonly IReadOnlyList<EqPreset> BuiltIn =
    [
        new("Flat", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        new("Bass boost", [6, 5, 4, 2, 0, 0, 0, 0, 0, 0]),
        new("Treble boost", [0, 0, 0, 0, 0, 1, 2, 4, 5, 6]),
        new("Vocal", [-2, -1, 0, 2, 4, 4, 3, 1, 0, -1]),
        new("Gaming", [3, 2, 0, -1, 0, 2, 4, 5, 4, 2]),
        new("Loudness", [6, 4, 1, 0, -1, 0, 0, 2, 4, 5])
    ];

    public static bool IsBuiltIn(string name) =>
        BuiltIn.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a device's curve is exactly this one (the boosts are the device's own, not the preset's).</summary>
    public bool Matches(AudioDeviceEq device) => Gains.SequenceEqual(device.Gains);
}

/// <summary>
/// The three boosts beside the equalizer, each from 0 (off) to 10, added to
/// whatever curve plays. Bass boost lifts everything under about 120 Hz;
/// Dynamic boost brings out both ends and the presence range, for a fuller,
/// punchier sound; Clarity lifts the mids and treble, for crisper voices
/// and detail.
/// </summary>
internal static class EqBoosts
{
    public const int Levels = 10;

    // What each boost writes, per level: a filter's type, frequency, gain per level, and Q (peaks only).
    internal static readonly (string Type, double Frequency, double GainPerLevel, double Q)[] Bass =
    [
        ("LS", 120, 1.0, 0)
    ];

    internal static readonly (string Type, double Frequency, double GainPerLevel, double Q)[] Dynamic =
    [
        ("LS", 80, 0.4, 0),
        ("PK", 3000, 0.3, 1.0),
        ("HS", 7000, 0.6, 0)
    ];

    internal static readonly (string Type, double Frequency, double GainPerLevel, double Q)[] Clarity =
    [
        ("PK", 2000, 0.4, 0.8),
        ("HS", 5000, 0.5, 0)
    ];

    public static int Clamp(int level) => Math.Clamp(level, 0, Levels);
}

/// <summary>
/// One output device's equalizer: on or off, the preset it came from (if it
/// still has one), its own curve, and its two boosts, which is what plays.
/// </summary>
internal sealed record AudioDeviceEq(bool Enabled, string? Preset, double[] Gains, int BassBoost = 0, int DynamicBoost = 0, int ClarityBoost = 0)
{
    /// <summary>A device seen for the first time: on, and flat, so nothing changes until the curve does.</summary>
    public static AudioDeviceEq Default => new(true, "Flat", EqBands.Flat());

    /// <summary>The curve with its gains and boosts trimmed to their ranges (a hand-edited settings file may hold anything).</summary>
    public AudioDeviceEq Normalized() => this with
    {
        Gains = [.. Enumerable.Range(0, EqBands.Count).Select(band => EqBands.Snap(band < (Gains?.Length ?? 0) ? Gains![band] : 0))],
        BassBoost = EqBoosts.Clamp(BassBoost),
        DynamicBoost = EqBoosts.Clamp(DynamicBoost),
        ClarityBoost = EqBoosts.Clamp(ClarityBoost)
    };

    public AudioDeviceEq WithPreset(EqPreset preset) =>
        this with { Preset = preset.Name, Gains = [.. preset.Gains] };

    /// <summary>
    /// How far the curve and the boosts together lift the loudest frequency,
    /// in dB (0 when nothing is lifted): the preamp takes that much off, so a
    /// boost never clips. Estimated on a fine grid across the audible range.
    /// </summary>
    public double Headroom()
    {
        var curve = Normalized();
        var highest = 0.0;

        for (var octave = Math.Log2(20); octave <= Math.Log2(20000); octave += 1.0 / 24)
            highest = Math.Max(highest, curve.LevelAt(Math.Pow(2, octave)));

        return Math.Ceiling(highest / EqBands.Step) * EqBands.Step;
    }

    // The change at one frequency, in dB: the bands joined in straight lines
    // on a log scale (as Equalizer APO joins them), plus the boosts' filters.
    private double LevelAt(double frequency)
    {
        var bands = EqBands.Frequencies;
        double level;

        if (frequency <= bands[0])
            level = Gains[0];
        else if (frequency >= bands[^1])
            level = Gains[^1];
        else
        {
            var upper = Array.FindIndex(bands, band => band >= frequency);
            var share = Math.Log(frequency / bands[upper - 1]) / Math.Log((double)bands[upper] / bands[upper - 1]);
            level = Gains[upper - 1] + (Gains[upper] - Gains[upper - 1]) * share;
        }

        foreach (var (filters, boost) in new[] { (EqBoosts.Bass, BassBoost), (EqBoosts.Dynamic, DynamicBoost), (EqBoosts.Clarity, ClarityBoost) })
        {
            foreach (var (type, center, perLevel, q) in filters)
            {
                var gain = perLevel * boost;
                var ratio = frequency / center;

                level += type switch
                {
                    "LS" => gain / (1 + ratio * ratio * ratio * ratio),
                    "HS" => gain * ratio * ratio * ratio * ratio / (1 + ratio * ratio * ratio * ratio),
                    _ => gain / (1 + Math.Pow(q * (ratio - 1 / ratio), 2))
                };
            }
        }

        return level;
    }
}

/// <summary>
/// The file Equalizer APO plays: a section per output device whose equalizer
/// is on, picked by the device's id, with the preamp that keeps it from
/// clipping, its ten bands and its boosts. A device that is off, or not in
/// the file, plays as it is.
/// </summary>
internal static class EqualizerApoConfig
{
    /// <summary>The first line of a file the app wrote, which tells it from the user's own.</summary>
    public const string Marker = "# Written by Blade Control.";

    /// <summary>The id Equalizer APO knows a device by: the last part of Windows' endpoint id, "{0.0.0.00000000}.{guid}".</summary>
    public static string DeviceGuid(string endpointId)
    {
        var dot = endpointId.LastIndexOf('.');
        return (dot >= 0 ? endpointId[(dot + 1)..] : endpointId).ToLowerInvariant();
    }

    public static string Build(IEnumerable<KeyValuePair<string, AudioDeviceEq>> devices)
    {
        var text = new StringBuilder();
        text.AppendLine(Marker);
        text.AppendLine("# Set from the app's Audio page; changes made here are replaced the next time it saves.");

        foreach (var (endpointId, device) in devices.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!device.Enabled)
                continue;

            var curve = device.Normalized();

            text.AppendLine();
            text.AppendLine($"Device: {DeviceGuid(endpointId)}");
            text.AppendLine($"Preamp: {Number(-Preamp(curve) + 0.0)} dB"); // + 0.0: never "-0".
            text.AppendLine("GraphicEQ: " + string.Join("; ",
                EqBands.Frequencies.Select((frequency, band) => $"{frequency} {Number(curve.Gains[band])}")));

            foreach (var (filters, boost) in new[] { (EqBoosts.Bass, curve.BassBoost), (EqBoosts.Dynamic, curve.DynamicBoost), (EqBoosts.Clarity, curve.ClarityBoost) })
            {
                if (boost == 0)
                    continue;

                foreach (var (type, frequency, perLevel, q) in filters)
                    text.AppendLine($"Filter: ON {type} Fc {Number(frequency)} Hz Gain {Number(perLevel * boost)} dB" + (type == "PK" ? $" Q {Number(q)}" : ""));
            }
        }

        return text.ToString();
    }

    // As much down as the loudest frequency goes up, and a decibel more: the
    // equalizer's filter rings a little past its curve, and a lift right at
    // full scale is what clips first (in a Bluetooth codec, say). Nothing lifted, nothing taken.
    private const double PreampMargin = 1;

    private static double Preamp(AudioDeviceEq curve) => curve.Headroom() is var headroom and > 0 ? headroom + PreampMargin : 0;

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>
/// Writes the equalizer into Equalizer APO's config.txt, which it reads again
/// by itself whenever the file changes. The first time, the file it finds
/// (Equalizer APO's sample, or the user's own) is kept beside it, untouched.
/// </summary>
internal sealed class EqualizerApoWriter(string configFolder)
{
    public const string BackupName = "config.before-BladeControl.txt";

    private string ConfigPath => Path.Combine(configFolder, "config.txt");

    public void Write(string text)
    {
        var backup = Path.Combine(configFolder, BackupName);

        if (File.Exists(ConfigPath) && !File.Exists(backup) && !File.ReadAllText(ConfigPath).StartsWith(EqualizerApoConfig.Marker, StringComparison.Ordinal))
            File.Copy(ConfigPath, backup);

        // Written beside it and moved in, so Equalizer APO never reads half a file.
        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, ConfigPath, overwrite: true);
    }
}
