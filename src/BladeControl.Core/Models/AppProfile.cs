using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>What game mode keeps from Windows while an app runs: the Windows key, and Alt+Tab and Alt+F4 with it.</summary>
internal enum GameModeBlock
{
    WindowsKey,
    WindowsKeyAndAltTab,
    WindowsKeyAltTabAndAltF4
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
    RgbColor? KeyboardColor = null,
    // Game mode while the app runs: which keys are kept from Windows.
    // Read leniently: a version before the choice of keys saved it as true or false.
    [property: JsonConverter(typeof(GameModeBlockConverter))] GameModeBlock? GameMode = null)
{
    /// <summary>Whether the profile changes anything at all.</summary>
    public bool ChangesAnything =>
        Mode is not null || MaxFan is not null || RefreshHz is not null || ColorProfile is not null ||
        Keyboard is not null || KeyboardBrightness is not null || KeyboardColor is not null || GameMode is not null;

    /// <summary>The effects that show a chosen color; the others run through their own colors.</summary>
    public static bool TakesColor(KeyboardEffect? effect) => effect is KeyboardEffect.StaticGreen or KeyboardEffect.Breathing;

    /// <summary>The performance part, as the Performance page applies it; null when it leaves the mode alone.</summary>
    public PowerProfile? Performance => Mode is null
        ? null
        : new PowerProfile(Mode, Mode == PerformanceMode.Custom ? Cpu ?? CpuBoost.High : null, Mode == PerformanceMode.Custom ? Gpu ?? GpuBoost.High : null);
}

/// <summary>
/// Reads an app profile's game mode by name ("WindowsKey"...) and also as the
/// true or false an earlier version wrote: true is the Windows key, false is
/// no change. Anything else unknown is no change too, so one odd value never
/// stops the whole settings file from loading.
/// </summary>
internal sealed class GameModeBlockConverter : JsonConverter<GameModeBlock?>
{
    public override bool HandleNull => true;

    public override GameModeBlock? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => GameModeBlock.WindowsKey,
        JsonTokenType.String when Enum.TryParse<GameModeBlock>(reader.GetString(), out var block) && Enum.IsDefined(block) => block,
        _ => SkipUnknown(ref reader)
    };

    public override void Write(Utf8JsonWriter writer, GameModeBlock? value, JsonSerializerOptions options)
    {
        if (value is { } block)
            writer.WriteStringValue(block.ToString());
        else
            writer.WriteNullValue();
    }

    private static GameModeBlock? SkipUnknown(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}
