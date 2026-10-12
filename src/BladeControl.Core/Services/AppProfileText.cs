using BladeControl.Core.Localization;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>The short line under an app in the Apps page, and in its notice: what its profile changes.</summary>
internal static class AppProfileText
{
    public const string Separator = " · ";

    /// <summary>For example "Custom · Max fan · 165 Hz · Neutral · Keyboard off".</summary>
    public static string Summary(AppProfile profile)
    {
        var parts = new List<string>();

        if (profile.Mode is { } mode)
            parts.Add(L.T(mode.ToString()));

        if (profile.MaxFan is { } fan)
            parts.Add(fan ? L.T("Max fan") : L.T("Auto fan"));

        if (profile.RefreshHz is { } hertz)
            parts.Add($"{hertz} Hz");

        if (profile.ColorProfile is { } colors)
            parts.Add(ColorName(ColorProfiles.Parse(colors)));

        if (profile.Keyboard == KeyboardEffect.Off)
            parts.Add(L.T("Keyboard off"));
        else if (profile.Keyboard is { } effect)
            parts.Add(L.F("Keyboard: {0}", EffectName(effect)));

        if (profile.KeyboardBrightness is { } percent && profile.Keyboard != KeyboardEffect.Off)
            parts.Add(L.F("Keyboard {0}%", percent));

        return parts.Count == 0 ? L.T("No changes yet") : string.Join(Separator, parts);
    }

    public static string ColorName(ColorProfile profile) => profile switch
    {
        ColorProfile.Warm => L.T("Night light"),
        ColorProfile.Cool => L.T("Neutral"),
        ColorProfile.Contrast => L.T("Contrast"),
        _ => L.T("Standard")
    };

    public static string EffectName(KeyboardEffect effect) => effect switch
    {
        KeyboardEffect.StaticGreen => L.T("Static"),
        KeyboardEffect.Off => L.T("Off"),
        _ => L.T(effect.ToString())
    };

    /// <summary>The launcher a game was found in, as shown beside it.</summary>
    public static string SourceName(AppSource source) => source switch
    {
        AppSource.Steam => "Steam",
        AppSource.Epic => "Epic Games",
        AppSource.Xbox => "Xbox",
        AppSource.BattleNet => "Battle.net",
        _ => L.T("App")
    };
}
