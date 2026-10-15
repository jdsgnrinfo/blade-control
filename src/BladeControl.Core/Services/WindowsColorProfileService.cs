using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;

namespace BladeControl.Core.Services;

/// <summary>
/// Keeps the color profile picked on the Display page in Windows itself: as an
/// ICC profile for the laptop screen, for this user, whose gamma table Windows
/// loads at sign-in and after sleep or a change of screens, with or without
/// the app. Standard takes it out and puts back how the screen was set before.
/// An app's profile (a game's colors) is not installed: it only lasts while the
/// game runs, and the app sets it on the graphics card as before.
/// </summary>
internal sealed class WindowsColorProfileService(
    IDisplayColorProfiles windows,
    string folder,
    WindowsColorProfileState? saved,
    Action<WindowsColorProfileState?> persist)
{
    private readonly Lock _sync = new();
    private WindowsColorProfileState? _saved = saved;

    /// <summary>The file name the app gives a profile in Windows.</summary>
    public static string FileName(ColorProfile profile) => $"Blade Control {profile}.icm";

    /// <summary>Puts <paramref name="profile"/> in Windows for the laptop screen, or for Standard what was there before. False when Windows refused.</summary>
    public bool Apply(ColorProfile profile)
    {
        lock (_sync)
        {
            try
            {
                if (profile == ColorProfile.Standard)
                    RestoreLocked();
                else
                    InstallLocked(profile);

                return true;
            }
            catch (Exception exception)
            {
                AppLog.Error($"Could not put the {profile} color profile in Windows.", exception);
                return false;
            }
        }
    }

    private void InstallLocked(ColorProfile profile)
    {
        var name = FileName(profile);

        if (_saved?.Installed == name && windows.UsesMySettings && windows.GetDefault() == name)
            return;

        // How the screen was before the app's first profile, kept until Standard.
        var before = _saved ?? new WindowsColorProfileState(name, windows.UsesMySettings, null);

        if (_saved is null && before.UsedMySettingsBefore)
            before = before with { DefaultBefore = windows.GetDefault() };

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, IccProfile.Build($"Blade Control {profile}", ColorProfiles.Ramp(profile), DateTime.Now));
        try
        {
            windows.Install(path);
        }
        catch (System.ComponentModel.Win32Exception) when (_saved?.Installed == name)
        {
            // Already in Windows' color folder from before; it only needs to be the default again.
        }

        if (!windows.UsesMySettings)
            windows.UsesMySettings = true;

        windows.SetDefault(name);

        // The app's previous profile, now replaced, goes.
        if (_saved?.Installed is { } previous && previous != name)
            windows.Remove(previous);

        _saved = before with { Installed = name };
        persist(_saved);
        AppLog.Info($"Color profile {profile} is now the laptop screen's profile in Windows ({name}).");
    }

    private void RestoreLocked()
    {
        if (_saved is not { } state)
            return;

        if (state.DefaultBefore is { } before)
            windows.SetDefault(before);

        windows.Remove(state.Installed);

        if (!state.UsedMySettingsBefore)
            windows.UsesMySettings = false;

        _saved = null;
        persist(null);
        AppLog.Info($"The laptop screen's color profile in Windows is back as it was ({state.DefaultBefore ?? "none"}).");
    }
}
