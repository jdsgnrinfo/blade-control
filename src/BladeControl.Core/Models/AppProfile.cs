namespace BladeControl.Core.Models;

/// <summary>Where an app in the Apps page came from: picked by hand, or found in a game launcher's library.</summary>
internal enum AppSource
{
    Manual,
    Steam,
    Epic,
    Xbox,
    BattleNet
}

/// <summary>
/// What changes while one app or game is running. <see cref="Path"/> is the
/// program (an .exe) or, for a game found in a launcher's library, its install
/// folder: any program running from inside it counts, so a game that starts
/// through its own launcher or a helper is still caught. Every setting left
/// null stays as it is; the ones set are put back when the app closes.
/// </summary>
internal sealed record AppProfile(
    string Name,
    string Path,
    // The program whose icon is shown; the same as Path when Path is an .exe.
    string? IconPath = null,
    AppSource Source = AppSource.Manual,
    bool Enabled = true,
    PerformanceMode? Mode = null,
    // Custom's levels; they only matter when Mode is Custom.
    CpuBoost? Cpu = null,
    GpuBoost? Gpu = null,
    bool? MaxFan = null,
    int? RefreshHz = null,
    // A ColorProfile's name (Standard, Warm, Cool, Contrast).
    string? ColorProfile = null,
    KeyboardEffect? Keyboard = null,
    // 0 to 100.
    int? KeyboardBrightness = null,
    // The keyboard's color, with an effect that takes one (Static, Breathing),
    // on the laptops that show one.
    RgbColor? KeyboardColor = null)
{
    /// <summary>Whether the profile changes anything at all.</summary>
    public bool ChangesAnything =>
        Mode is not null || MaxFan is not null || RefreshHz is not null || ColorProfile is not null ||
        Keyboard is not null || KeyboardBrightness is not null || KeyboardColor is not null;

    /// <summary>The effects that show a chosen color; the others run through their own colors.</summary>
    public static bool TakesColor(KeyboardEffect? effect) => effect is KeyboardEffect.StaticGreen or KeyboardEffect.Breathing;

    /// <summary>The performance part, as the Performance page applies it; null when it leaves the mode alone.</summary>
    public PowerProfile? Performance => Mode is null
        ? null
        : new PowerProfile(Mode, Mode == PerformanceMode.Custom ? Cpu ?? CpuBoost.High : null, Mode == PerformanceMode.Custom ? Gpu ?? GpuBoost.High : null);
}
