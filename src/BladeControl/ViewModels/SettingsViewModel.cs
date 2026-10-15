using System.IO;
using System.Reflection;
using System.Windows;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using BladeControl.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>
/// The app's own settings: the language, starting with Windows and staying on
/// top; then Razer's drivers, the logs and the reset. Every switch applies as
/// soon as it is flipped, so there is nothing to confirm.
/// </summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly AppLanguage[] Languages = Enum.GetValues<AppLanguage>();

    private readonly AppServices _services;
    private readonly Action<string, bool> _report;
    private bool _loading;

    /// <param name="monitor">The Performance page's system monitor, whose switch is here.</param>
    public SettingsViewModel(AppServices services, Action<string, bool> report, SystemMonitorViewModel monitor)
    {
        _services = services;
        _report = report;
        Monitor = monitor;
        Updates = new UpdatesViewModel(services);
        _languageIndex = Array.IndexOf(Languages, L.Current);
        _startAtLogin = ReadStartAtLogin();
    }

    /// <summary>The system monitor, for its switch (bound to <see cref="SystemMonitorViewModel.IsEnabled"/>).</summary>
    public SystemMonitorViewModel Monitor { get; }

    /// <summary>Looking for and installing a newer Blade Control.</summary>
    public UpdatesViewModel Updates { get; }

    // ===== General =====

    /// <summary>Each language named in itself, so it can be found whatever the current one is.</summary>
    public IReadOnlyList<string> LanguageNames { get; } = [.. Languages.Select(language => language == AppLanguage.Spanish ? "Español" : "English")];

    private int _languageIndex;

    /// <summary>Picking another language restarts the app in it.</summary>
    public int LanguageIndex
    {
        get => _languageIndex;
        set
        {
            if (value < 0 || !SetProperty(ref _languageIndex, value) || Languages[value] == L.Current)
                return;

            _services.UpdateSettings(settings => settings with { Language = L.Code(Languages[value]) });
            Restart();
        }
    }

    private bool _startAtLogin;

    public bool StartAtLogin
    {
        get => _startAtLogin;
        set
        {
            if (_loading || !SetProperty(ref _startAtLogin, value))
                return;

            try
            {
                _services.StartupRegistration.SetEnabled(value);
                AppLog.Info($"Start at login turned {(value ? "on" : "off")}.");
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not change the start-at-login setting.", exception);
                _report(L.T("Could not change Start at login."), true);

                // Show what is really true.
                _loading = true;
                StartAtLogin = !value;
                _loading = false;
            }
        }
    }

    public bool AlwaysOnTop
    {
        get => _services.Settings.AlwaysOnTop;
        set
        {
            if (value == AlwaysOnTop)
                return;

            _services.UpdateSettings(settings => settings with { AlwaysOnTop = value });

            if (Application.Current.MainWindow is { } window)
                window.Topmost = value;

            OnPropertyChanged();
        }
    }

    /// <summary>Ctrl+Shift+F1, F2 and F3 switch performance mode from any program. Off frees them for another program.</summary>
    public bool ProfileShortcuts
    {
        get => _services.Settings.ProfileShortcuts;
        set
        {
            if (value == ProfileShortcuts)
                return;

            _services.UpdateSettings(settings => settings with { ProfileShortcuts = value });
            OnPropertyChanged();
            ProfileShortcutsChanged?.Invoke(this, value);
        }
    }

    /// <summary>The mode shortcuts were turned on or off; the app takes or frees the keys.</summary>
    public event EventHandler<bool>? ProfileShortcutsChanged;

    /// <summary>Under the switch: the shortcuts, or which ones another program already owns.</summary>
    [ObservableProperty]
    private string _profileShortcutsHint = L.T("Ctrl+Shift+F1, F2 and F3: Silent, Balanced and Custom.");

    /// <summary>Windows can change Start at login meanwhile (Task Manager's Startup apps): read again when the page opens.</summary>
    public void Refresh()
    {
        _loading = true;
        StartAtLogin = ReadStartAtLogin();
        _loading = false;
    }

    // ===== Experimental =====

    /// <summary>Silent turns Windows' CPU boost off, which is what quiets it; off leaves the boost as the power plan has it.</summary>
    public bool SilentTurnsBoostOff
    {
        get => _services.Settings.SilentTurnsBoostOff;
        set
        {
            if (value == SilentTurnsBoostOff)
                return;

            _services.UpdateSettings(settings => settings with { SilentTurnsBoostOff = value });
            _services.SilentBoost.SetEnabled(value);
            AppLog.Info($"Silent turning the CPU boost off turned {(value ? "on" : "off")}.");
            OnPropertyChanged();
        }
    }

    // ===== Maintenance =====

    public string Version { get; } = "Blade Control " + FormatVersion(Assembly.GetEntryAssembly()?.GetName().Version);

    [RelayCommand]
    private static void OpenDrivers() => ExternalLinks.OpenRazerDrivers();

    [RelayCommand]
    private static void OpenLogs() => ExternalLinks.OpenLogFolder();

    [RelayCommand]
    private static void OpenDonation() => ExternalLinks.OpenDonation();

    private const string SettingsFileFilter = "Blade Control (*.json)|*.json";

    /// <summary>Writes the settings to a file the user picks.</summary>
    [RelayCommand]
    private void ExportSettings()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Export settings"),
            Filter = SettingsFileFilter,
            FileName = $"Blade Control {DateTime.Now:yyyy-MM-dd}.json"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, SettingsTransfer.Export(_services.Settings, AppVersion, DateTime.Now));
            AppLog.Info($"Settings exported to {dialog.FileName}.");
            _report(L.T("Settings exported."), false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not export the settings.", exception);
            _report(L.T("Could not export the settings."), true);
        }
    }

    /// <summary>Reads settings from a file, says what they bring before anything changes, then restarts with them.</summary>
    [RelayCommand]
    private void ImportSettings()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.T("Import settings"), Filter = SettingsFileFilter };

        if (dialog.ShowDialog() != true)
            return;

        string text;

        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not read the settings file.", exception);
            Dialogs.Tell(L.T("The file could not be read."), L.T("Import settings"));
            return;
        }

        var imported = SettingsTransfer.Import(text, _services.Settings, path => File.Exists(path) || Directory.Exists(path), out var error);

        if (imported is null)
        {
            Dialogs.Tell(error!, L.T("Import settings"));
            return;
        }

        var missing = imported.MissingApps > 0
            ? "\r\n\r\n" + L.F("Apps not on this PC: {0}. Their profiles are kept for when you install them.", imported.MissingApps)
            : string.Empty;

        var answer = Dialogs.Ask(
            L.F("Replace your settings with the ones in this file?\r\n\r\n" +
                "It brings your performance modes, {0}, and the display, lighting and Blade Control options.",
                imported.AppProfiles == 1 ? L.T("1 app profile") : L.F("{0} app profiles", imported.AppProfiles)) +
            missing + "\r\n\r\n" + L.T("Blade Control will restart."),
            L.T("Import settings"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        _services.UpdateSettings(_ => imported.Settings);
        AppLog.Info($"Settings imported from {dialog.FileName}: {imported.AppProfiles} app profiles, {imported.MissingApps} not on this PC.");
        Restart();
    }

    private static string AppVersion => FormatVersion(Assembly.GetEntryAssembly()?.GetName().Version);

    /// <summary>Says exactly what will change before anything does; No is the default.</summary>
    [RelayCommand]
    private async Task ResetAsync()
    {
        var answer = Dialogs.Ask(
            L.T("Reset Blade Control to how it was the first time you opened it?\r\n\r\n" +
                "This will:\r\n" +
                "  - clear your saved settings, including the options in this window and your never-close list\r\n" +
                "  - turn off Start at login\r\n" +
                "  - set the laptop to Balanced mode with no battery charge limit\r\n\r\n" +
                "It will not change Razer's background services or anything else on your PC. Blade Control will restart."),
            L.T("Reset to defaults"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        // Before the settings holding them are cleared: the CPU boost Silent took
        // away and the lid action the lid option changed come back.
        _services.SilentBoost.Restore();
        _services.LidClose.Restore();
        await _services.BatteryMode.RestoreAsync("of a reset");

        var result = await _services.FactoryReset.RunAsync(_services.Settings);

        if (!result.Succeeded)
            Dialogs.Tell(string.Join(Environment.NewLine, result.Problems) + Environment.NewLine + Environment.NewLine +
                L.T("Blade Control will restart now. Details are in the log."), L.T("Reset to defaults"));

        Restart();
    }

    private void Restart()
    {
        if (AppRestart.Relaunch())
        {
            App.QuitForRestart();
            return;
        }

        Dialogs.Tell(L.T("Blade Control could not restart itself. Please close it and open it again."), "Blade Control");
    }

    private bool ReadStartAtLogin()
    {
        try
        {
            return _services.StartupRegistration.IsEnabled;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the start-at-login setting.", exception);
            return false;
        }
    }

    private static string FormatVersion(Version? version) =>
        version is null ? string.Empty : $"{version.Major}.{version.Minor}.{version.Build}";
}
