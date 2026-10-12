using System.Text.Json;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>A settings file as exported: what it is, which version wrote it, and the settings.</summary>
internal sealed record SettingsFile(
    string? Format,
    int Version,
    string? AppVersion = null,
    DateTime? Exported = null,
    AppSettings? Settings = null);

/// <summary>Settings read from a file and ready to save, with what the confirmation should mention.</summary>
internal sealed record ImportedSettings(
    AppSettings Settings,
    int AppProfiles,
    int MissingApps,
    string? AppVersion);

/// <summary>
/// Exports the settings to a file and reads them back, to keep them through a
/// Windows reinstall or take them to another Blade. Only what the user chose
/// travels: the modes, app profiles, display, lighting and window options.
/// What only means something on this PC (what Razer's services and login
/// entries were before "Stop", the CPU boost and lid action the app is
/// holding, the window's place) is left out, and on import this PC's own is
/// kept.
/// </summary>
internal static class SettingsTransfer
{
    public const string Format = "Blade Control settings";
    public const int Version = 1;

    /// <summary>The settings as file text.</summary>
    public static string Export(AppSettings settings, string? appVersion, DateTime exported) =>
        JsonSerializer.Serialize(
            new SettingsFile(Format, Version, appVersion, exported, Portable(settings)),
            SettingsService.SerializerOptions);

    /// <summary>
    /// Reads a file's text into settings to save in place of <paramref name="current"/>.
    /// Returns null with <paramref name="error"/> in plain words when the text is not a
    /// settings file this version can read. <paramref name="exists"/> tells whether an
    /// app profile's program or folder is on this PC.
    /// </summary>
    public static ImportedSettings? Import(string text, AppSettings current, Func<string, bool> exists, out string? error)
    {
        SettingsFile? file;

        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(text, SettingsService.SerializerOptions);
        }
        catch (JsonException)
        {
            file = null;
        }

        if (file is not { Format: Format, Settings: not null })
        {
            error = L.T("This file is not a Blade Control settings file.");
            return null;
        }

        if (file.Version > Version)
        {
            error = L.T("This file was made by a newer Blade Control. Update Blade Control and try again.");
            return null;
        }

        // A profile with no name or program, as a hand-edited file might have, cannot be shown or matched.
        var profiles = file.Settings.AppProfiles?
            .Where(profile => !string.IsNullOrWhiteSpace(profile?.Name) && !string.IsNullOrWhiteSpace(profile.Path))
            .ToArray();

        var settings = Portable(file.Settings) with
        {
            AppProfiles = profiles,
            RazerServiceStartModes = current.RazerServiceStartModes,
            RazerLoginApprovals = current.RazerLoginApprovals,
            CpuBoostBeforeSilent = current.CpuBoostBeforeSilent,
            CpuEfficiencyBeforeSilent = current.CpuEfficiencyBeforeSilent,
            PlanBeforeIdle = current.PlanBeforeIdle,
            LidActionBeforeKeepRunning = current.LidActionBeforeKeepRunning,
            WindowScale = current.WindowScale,
            Window = current.Window
        };

        error = null;
        return new ImportedSettings(
            settings,
            profiles?.Length ?? 0,
            profiles?.Count(profile => !exists(profile.Path)) ?? 0,
            file.AppVersion);
    }

    /// <summary>The settings without what only means something on this PC.</summary>
    internal static AppSettings Portable(AppSettings settings) =>
        settings with
        {
            RazerServiceStartModes = null,
            RazerLoginApprovals = null,
            CpuBoostBeforeSilent = null,
            CpuEfficiencyBeforeSilent = null,
            PlanBeforeIdle = null,
            LidActionBeforeKeepRunning = null,
            WindowScale = null,
            Window = null,
            PerformanceMode = null,
            CustomCpuBoost = null,
            CustomGpuBoost = null
        };
}
