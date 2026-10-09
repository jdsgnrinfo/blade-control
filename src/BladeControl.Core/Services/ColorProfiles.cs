namespace BladeControl.Core.Services;

/// <summary>The screen's color profiles, as offered in Display.</summary>
internal enum ColorProfile
{
    /// <summary>The screen as it was before the app changed it.</summary>
    Standard,

    /// <summary>Less blue, about 5000 K: easier on the eyes at night.</summary>
    Warm,

    /// <summary>
    /// Neutral: a white of about 8000 K. Laptop panels lean warm on their own,
    /// which throws colors off, so red and green come down a little.
    /// </summary>
    Cool,

    /// <summary>Darker shadows and brighter highlights, the same colors.</summary>
    Contrast
}

/// <summary>
/// The gamma ramp for each profile: for every channel, red, green then blue,
/// 256 levels from 0 to 65535, the curve the graphics card sends the screen's
/// colors through. The changes stay within the range Windows accepts.
/// </summary>
internal static class ColorProfiles
{
    public const int Levels = 256;

    /// <summary>The saved name of a profile; null for Standard, the default.</summary>
    public static string? ToSetting(ColorProfile profile) => profile == ColorProfile.Standard ? null : profile.ToString();

    /// <summary>The profile a setting names; Standard when it names none.</summary>
    public static ColorProfile Parse(string? setting) =>
        Enum.TryParse<ColorProfile>(setting, ignoreCase: true, out var profile) && Enum.IsDefined(profile) ? profile : ColorProfile.Standard;

    /// <summary>The ramp for <paramref name="profile"/>: 768 values, red, green then blue.</summary>
    public static ushort[] Ramp(ColorProfile profile)
    {
        var (red, green, blue) = profile switch
        {
            ColorProfile.Warm => (1.00, 0.90, 0.76),
            ColorProfile.Cool => (0.90, 0.95, 1.00),
            _ => (1.00, 1.00, 1.00)
        };

        var ramp = new ushort[3 * Levels];

        for (var level = 0; level < Levels; level++)
        {
            var value = Curve(profile, level / (double)(Levels - 1));
            ramp[level] = ToWord(value * red);
            ramp[Levels + level] = ToWord(value * green);
            ramp[2 * Levels + level] = ToWord(value * blue);
        }

        return ramp;
    }

    // Straight for all but Contrast, which leans halfway towards an S curve:
    // the darks a little darker, the lights a little lighter, the middle kept.
    private static double Curve(ColorProfile profile, double x)
    {
        if (profile != ColorProfile.Contrast)
            return x;

        var s = x * x * (3 - 2 * x);
        return x + 0.5 * (s - x);
    }

    private static ushort ToWord(double value) => (ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue);
}

/// <summary>Reads and sets the gamma ramp of the screens on the desktop.</summary>
internal interface IGammaRamps
{
    /// <summary>The names of the screens on the desktop, such as \\.\DISPLAY1.</summary>
    IReadOnlyList<string> Displays();

    /// <summary>The screen's ramp now; null when it cannot be read.</summary>
    ushort[]? Get(string display);

    /// <summary>Sets the screen's ramp; false when Windows or the driver refuses it.</summary>
    bool Set(string display, ushort[] ramp);
}

/// <summary>
/// Puts a color profile on every screen and keeps it there. Before its first
/// change to a screen it keeps the ramp the screen had (a calibration, say),
/// so Standard, and the app closing, give exactly that back rather than a
/// plain straight line.
/// </summary>
internal sealed class ColorProfileService(IGammaRamps ramps)
{
    private readonly Dictionary<string, ushort[]> _original = new(StringComparer.OrdinalIgnoreCase);

    public ColorProfile Current { get; private set; } = ColorProfile.Standard;

    /// <summary>Applies <paramref name="profile"/> to every screen; false when no screen took it.</summary>
    public bool Apply(ColorProfile profile)
    {
        Current = profile;
        return Reapply();
    }

    /// <summary>
    /// Puts the current profile back, as Windows may reset the ramps on waking,
    /// on a new screen or a change of mode. Standard touches only screens the
    /// app has changed. False when a screen refused the profile.
    /// </summary>
    public bool Reapply()
    {
        var applied = true;

        foreach (var display in ramps.Displays())
        {
            if (Current == ColorProfile.Standard)
            {
                if (_original.Remove(display, out var original))
                    applied &= ramps.Set(display, original);

                continue;
            }

            if (!_original.ContainsKey(display) && ramps.Get(display) is { } before)
                _original[display] = IsOurs(before) ? ColorProfiles.Ramp(ColorProfile.Standard) : before;

            applied &= ramps.Set(display, ColorProfiles.Ramp(Current));
        }

        return applied;
    }

    // One of the app's own profiles, left on the screen when it last stopped
    // without restoring it (a crash, say): the screen's own ramp was the straight one.
    private static bool IsOurs(ushort[] ramp) =>
        Enum.GetValues<ColorProfile>()
            .Where(profile => profile != ColorProfile.Standard)
            .Any(profile => ColorProfiles.Ramp(profile).AsSpan().SequenceEqual(ramp));

    /// <summary>Gives every screen the app changed its own ramp back, keeping the chosen profile for next time.</summary>
    public void Restore()
    {
        foreach (var (display, original) in _original)
            ramps.Set(display, original);

        _original.Clear();
    }
}
