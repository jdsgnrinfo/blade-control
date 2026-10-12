using System.ServiceProcess;

namespace BladeControl.Core.Models;

internal sealed record AppSettings(
    string? DisplayMode = null,
    int? BatteryChargeLimit = null,
    PowerProfile? PluggedInProfile = null,
    PowerProfile? OnBatteryProfile = null,
    // What each Razer service was set to before "Stop" disabled it, so "Start"
    // can put back exactly that instead of guessing Automatic.
    Dictionary<string, ServiceStartMode>? RazerServiceStartModes = null,
    // Written by earlier versions, before profiles existed. Read once and
    // folded into PluggedInProfile by SettingsService, then dropped.
    string? PerformanceMode = null,
    string? CustomCpuBoost = null,
    string? CustomGpuBoost = null,
    // Settings window. Both are on unless the user turns them off, which is
    // also what a settings file from an earlier version means.
    bool AutoSwitchProfiles = true,
    bool HideWhenClickedAway = true,
    // Experimental, on unless turned off: Silent turns Windows' CPU boost off.
    bool SilentTurnsBoostOff = true,
    // Off unless the user turns it on: it closes other programs.
    bool CloseGpuAppsOnUnplug = false,
    // Program names (e.g. "blender") to never close, on top of the built-in
    // list. Edited by hand in settings.json.
    string[]? NeverCloseApps = null,
    // What Task Manager's startup switch was for each Razer login entry before
    // "Stop" turned it off (as stored text; empty means it had none), so "Start"
    // can put back exactly that.
    Dictionary<string, string>? RazerLoginApprovals = null,
    // Off unless the user turns it on: the window stays above other windows
    // (borderless-window games included).
    bool AlwaysOnTop = false,
    // How much bigger than the base design the window is drawn (1 = base size).
    // Null picks a size from Windows' display scaling. Edited by hand in
    // settings.json, for example 1.5; applies the next time the app starts.
    double? WindowScale = null,
    // On unless the user turns it off: the keyboard backlight goes off while
    // the screen is off (display timeout, sleep, closed lid).
    bool KeyboardOffWithScreen = true,
    // On unless the user turns it off: Ctrl+Shift+F1/F2/F3 switch performance
    // mode from any program. Off frees them for another program that wants them.
    bool ProfileShortcuts = true,
    // Silent always turns off Windows' processor boost. The boost setting it
    // replaced, kept until Balanced or Custom gives it back (closing the app
    // does not); null when Silent has not changed it.
    SavedPlanValue? CpuBoostBeforeSilent = null,
    // Left by an earlier version, whose Silent also changed the CPU's energy
    // preference: given back once at startup, then cleared.
    SavedPlanValue? CpuEfficiencyBeforeSilent = null,
    // The interface language: "en" or "es". Null follows the Windows display
    // language. Changing it restarts the app.
    string? Language = null,
    // Left by an earlier version's Idle option, when it ended with its plan
    // switched: the plan it replaced, given back once at startup, then cleared.
    Guid? PlanBeforeIdle = null,
    // The screen's color profile (Warm, Cool, Contrast); null is Standard,
    // the screen as it was.
    string? ColorProfile = null,
    // Off unless the user turns them on: the keyboard light also goes off
    // after this many minutes without a key or the mouse, and on battery
    // below this charge.
    bool LightsOffWhenIdle = false,
    int LightsOffIdleMinutes = 10,
    bool LightsOffOnLowBattery = false,
    int LightsOffBatteryPercent = 20,
    // The Apps page: what changes while each app or game runs, and the switch
    // that pauses them all (on unless turned off).
    AppProfile[]? AppProfiles = null,
    bool AppProfilesOn = true,
    // The main window's sidebar folded down to its icons, as it was left.
    bool SidebarCollapsed = false,
    // Where the window was and how big, as it was left (its size before it
    // was maximized, if it was); null until it has been closed once.
    WindowPlacement? Window = null,
    // Off unless the user turns it on: with a monitor connected and the
    // charger in, closing the lid leaves the laptop running.
    bool KeepRunningWithLidClosed = false,
    // The lid action that option replaced, kept until it is given back (the
    // monitor goes, the option is turned off, the app exits); null when the
    // lid action is the plan's own.
    SavedPlanValue? LidActionBeforeKeepRunning = null);

/// <summary>The window's position and size in device-independent pixels, and whether it was maximized.</summary>
internal sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
