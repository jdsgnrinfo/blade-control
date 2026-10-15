namespace BladeControl.Core.Models;

/// <summary>
/// What battery mode changed and how it was before, so plugging in can put it
/// back. Each value is null when battery mode did not change it. The screen
/// and keyboard keep the level battery mode set too: a level changed by hand
/// meanwhile is left as the user chose.
/// </summary>
internal sealed record BatteryModeSaved(
    bool? Transparency = null,
    bool? WindowShadows = null,
    bool? PointerShadow = null,
    bool? DragWindowContents = null,
    SavedPlanValue? EnergySaver = null,
    int? ScreenBrightness = null,
    int? ScreenBrightnessSet = null,
    int? KeyboardBrightness = null,
    int? KeyboardBrightnessSet = null);
