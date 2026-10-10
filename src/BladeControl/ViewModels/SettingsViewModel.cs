using System.Reflection;
using System.Windows;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Platform;
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

    private readonly Func<bool, Task<bool>> _useGaming;

    /// <param name="useGaming">Turns Gaming in Balanced's place on or off on the Performance page; false when the laptop refused it.</param>
    public SettingsViewModel(AppServices services, Action<string, bool> report, Func<bool, Task<bool>> useGaming)
    {
        _services = services;
        _report = report;
        _useGaming = useGaming;
        _languageIndex = Array.IndexOf(Languages, L.Current);
        _startAtLogin = ReadStartAtLogin();
    }

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

    /// <summary>Windows can change Start at login meanwhile (Task Manager's Startup apps): read again when the page opens.</summary>
    public void Refresh()
    {
        _loading = true;
        StartAtLogin = ReadStartAtLogin();
        _loading = false;

        // The Performance page turns Gaming off by itself when the laptop refuses it.
        OnPropertyChanged(nameof(GamingInsteadOfBalanced));
    }

    // ===== Experimental =====

    /// <summary>Plugged in, the Performance page's middle mode is Gaming instead of Balanced.</summary>
    public bool GamingInsteadOfBalanced
    {
        get => _services.Settings.GamingInsteadOfBalanced;
        set
        {
            if (value != GamingInsteadOfBalanced)
                _ = UseGamingAsync(value);
        }
    }

    private async Task UseGamingAsync(bool on)
    {
        if (await _useGaming(on) && on)
            _report(L.T("Gaming takes Balanced's place while plugged in."), false);

        // Shows what was kept: off again if the laptop refused it.
        OnPropertyChanged(nameof(GamingInsteadOfBalanced));
    }

    // ===== Maintenance =====

    public string Version { get; } = "Blade Control " + FormatVersion(Assembly.GetEntryAssembly()?.GetName().Version);

    [RelayCommand]
    private static void OpenDrivers() => ExternalLinks.OpenRazerDrivers();

    [RelayCommand]
    private static void OpenLogs() => ExternalLinks.OpenLogFolder();

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

        // Before the settings holding it are cleared: the CPU boost Silent took away comes back.
        _services.SilentBoost.Restore();

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
            App.Quit();
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
