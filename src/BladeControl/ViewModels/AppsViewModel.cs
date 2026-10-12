using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using BladeControl.UI;
using BladeControl.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BladeControl.ViewModels;

/// <summary>
/// An app in the Apps page's list: its icon and name, and its profile, which
/// changes nothing until one of its settings is chosen; from then on it has a
/// green check beside it.
/// </summary>
internal sealed partial class AppRow : ObservableObject
{
    private readonly Action<AppRow> _changed;
    private readonly string _foundIn;
    private AppProfile _profile;
    private ImageSource? _icon;
    private bool _iconRead;

    public AppRow(AppProfile profile, Action<AppRow> changed)
    {
        _profile = profile;
        _changed = changed;
        _foundIn = AppProfileText.SourceName(profile.Source);
    }

    public AppProfile Profile
    {
        get => _profile;
        set
        {
            if (!SetProperty(ref _profile, value))
                return;

            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(IsConfigured));
            _changed(this);
        }
    }

    public string Name => _profile.Name;

    public string Path => _profile.Path;

    /// <summary>What its profile changes, or where it was found while it changes nothing.</summary>
    public string Detail => IsConfigured ? AppProfileText.Summary(_profile) : _foundIn;

    /// <summary>Where it was found ("Steam"), for the settings' title.</summary>
    public string SourceName => _foundIn;

    /// <summary>Read the first time the row is shown, so a long list opens quickly.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRead)
            {
                _iconRead = true;
                _icon = AppIcons.For(_profile.IconPath ?? (AppProfileMatcher.IsProgram(_profile.Path) ? _profile.Path : null));
            }

            return _icon;
        }
    }

    /// <summary>One of its settings is chosen: the green check.</summary>
    public bool IsConfigured => _profile.ChangesAnything;

    /// <summary>The switch in its settings: off keeps the profile but does not use it.</summary>
    public bool Enabled
    {
        get => _profile.Enabled;
        set
        {
            if (value != _profile.Enabled)
                Profile = _profile with { Enabled = value };
        }
    }

    /// <summary>Its app is running and its profile is the one on now.</summary>
    [ObservableProperty]
    private bool _isRunning;
}

/// <summary>A keyboard color in the Apps page's dropdown: its name, and its swatch ("No change" has none).</summary>
internal sealed record ColorOption(string Name, Brush? Swatch);

/// <summary>
/// The Apps page: every app installed on the PC (the Start menu's programs
/// and the games found in Steam, Epic Games, Xbox and Battle.net), and for the
/// one picked, the mode, fans, refresh rate, screen colors and keyboard to use
/// while it runs. Every two seconds it looks at what is running; when an app
/// with settings starts, they go on with a notice, and when it closes
/// everything goes back as it was. Something changed by hand meanwhile stays
/// as it is. With two such apps running, the one started last wins.
/// </summary>
internal sealed partial class AppsViewModel : ObservableObject
{
    private static readonly PerformanceMode?[] Modes = [null, PerformanceMode.Silent, PerformanceMode.Balanced, PerformanceMode.Custom];
    private static readonly bool?[] Fans = [null, false, true];
    private static readonly ColorProfile?[] Colors = [null, ColorProfile.Standard, ColorProfile.Warm, ColorProfile.Cool, ColorProfile.Contrast];
    private static readonly int?[] Brightnesses = [null, 25, 50, 75, 100];
    private static readonly GameModeBlock?[] GameModes = [null, GameModeBlock.WindowsKey, GameModeBlock.WindowsKeyAndAltTab, GameModeBlock.WindowsKeyAltTabAndAltF4];

    private readonly AppServices _services;
    private readonly PerformanceViewModel _performance;
    private readonly DisplayViewModel _display;
    private readonly LightingViewModel _lighting;
    // Game mode's hold on the keyboard, while an app whose profile asks for it runs.
    private readonly GameModeHook _gameMode = new();
    private readonly Action<string, bool> _report;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly RunningPrograms _programs = new();
    private readonly KeyboardEffect?[] _effects;
    private readonly int?[] _rates;
    private readonly RgbColor?[] _keyboardColors;

    // The saved profiles, by the program or folder they follow: what the watch puts on.
    private readonly Dictionary<string, AppProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private AppProfile? _active;
    private bool _checking;
    private bool _listed;
    private bool _listing;

    public AppsViewModel(
        AppServices services,
        PerformanceViewModel performance,
        DisplayViewModel display,
        LightingViewModel lighting,
        Action<string, bool> report)
    {
        _services = services;
        _performance = performance;
        _display = display;
        _lighting = lighting;
        _report = report;

        foreach (var profile in services.Settings.AppProfiles ?? [])
            _profiles[profile.Path] = profile;

        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = item => item is AppRow row &&
            (string.IsNullOrWhiteSpace(SearchText) || row.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));

        _effects = [null, .. lighting.OfferedEffects.Select(effect => (KeyboardEffect?)effect)];
        _rates = [null, .. display.FixedRates.Select(hertz => (int?)hertz)];

        ModeOptions = [L.T("No change"), L.T("Silent"), L.T("Balanced"), L.T("Custom")];
        CpuOptions = [L.T("Low"), L.T("Medium"), L.T("High"), L.T("Boost")];
        GpuOptions = [L.T("Low"), L.T("Medium"), L.T("High")];
        FanOptions = [L.T("No change"), L.T("Automatic"), L.T("Maximum")];
        RateOptions = [L.T("No change"), .. _rates.Skip(1).Select(hertz => $"{hertz} Hz")];
        ColorOptions = [L.T("No change"), .. Colors.Skip(1).Select(colors => AppProfileText.ColorName(colors!.Value))];
        KeyboardOptions = [L.T("No change"), .. _effects.Skip(1).Select(effect => lighting.EffectNameOf(effect!.Value))];
        BrightnessOptions = [L.T("No change"), .. Brightnesses.Skip(1).Select(percent => $"{percent}%")];
        GameModeOptions = [L.T("No change"), .. GameModes.Skip(1).Select(block => AppProfileText.GameModeName(block!.Value))];

        _keyboardColors = [null, .. lighting.OfferedColors.Select(choice => (RgbColor?)choice.Value)];
        KeyboardColorOptions = [new ColorOption(L.T("No change"), null), .. lighting.OfferedColors.Select(choice => new ColorOption(choice.Name, choice.Swatch))];

        _performance.AppOverrideTakenOver += (_, _) => AppLog.Info("The mode was changed by hand while an app profile held it; it is kept when the app closes.");
        _watch.Tick += async (_, _) => await CheckAsync();
    }

    // ===== The list =====

    public ObservableCollection<AppRow> Apps { get; } = [];

    /// <summary>The list as shown: filtered by the search box.</summary>
    public ICollectionView AppsView { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => AppsView.Refresh();

    /// <summary>The apps are being looked for.</summary>
    [ObservableProperty]
    private bool _scanning;

    /// <summary>Under the list: nothing found.</summary>
    [ObservableProperty]
    private string _listStatus = string.Empty;

    /// <summary>The switch at the top of the list: off pauses every profile and puts back what was changed.</summary>
    public bool ProfilesOn
    {
        get => _services.Settings.AppProfilesOn;
        set
        {
            if (value == ProfilesOn)
                return;

            _services.UpdateSettings(settings => settings with { AppProfilesOn = value });
            AppLog.Info($"App profiles turned {(value ? "on" : "off")}.");
            OnPropertyChanged();
            _ = CheckAsync();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEditor))]
    private AppRow? _selected;

    partial void OnSelectedChanged(AppRow? value) => RaiseEditor();

    /// <summary>An app is picked: its settings show; otherwise "No app".</summary>
    public bool ShowsEditor => Selected is not null;

    /// <summary>When the page opens: no app picked; the first time, the apps on the PC are looked for.</summary>
    public void Refresh()
    {
        Selected = null;

        if (!_listed)
            _ = ListAppsAsync();
    }

    /// <summary>The refresh button: the apps on the PC are looked for again (one just installed, say).</summary>
    public Task RescanAsync() => ListAppsAsync();

    private async Task ListAppsAsync()
    {
        if (_listing)
            return;

        _listing = true;
        Scanning = true;
        ListStatus = string.Empty;

        try
        {
            var found = await Task.Run(InstalledPrograms.Scan);
            var picked = Selected?.Path;

            // Each app found, with its saved profile if it has one; then the saved ones not found (a program picked by hand).
            var rows = found
                .Select(app => _profiles.GetValueOrDefault(app.Path) ?? new AppProfile(app.Name, app.Path, app.IconPath, app.Source))
                .Concat(_profiles.Values.Where(profile => !found.Any(app => SameApp(app.Path, profile.Path))))
                .Select(profile => new AppRow(profile, OnRowChanged))
                .OrderByDescending(row => row.IsConfigured)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Apps.Clear();

            foreach (var row in rows)
                Apps.Add(row);

            Selected = picked is null ? null : Apps.FirstOrDefault(row => SameApp(row.Path, picked));
            ShowRunning();
            _listed = true;
            ListStatus = Apps.Count == 0 ? L.T("No apps found.") : string.Empty;
            AppLog.Info($"Apps page: {found.Count} apps found.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not list the apps.", exception);
            ListStatus = L.T("Could not list the apps.");
        }

        Scanning = false;
        _listing = false;
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("Choose the app's program"),
            Filter = L.T("Programs") + " (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var path = dialog.FileName;

        // One already listed is just picked; another goes at the top.
        if (Apps.FirstOrDefault(row => SameApp(row.Path, path)) is not { } row)
        {
            row = new AppRow(new AppProfile(ProgramName(path), path, path), OnRowChanged);
            Apps.Insert(0, row);
            AppLog.Info($"Apps page: {row.Name} added by hand.");
        }

        SearchText = string.Empty;
        Selected = row;
    }

    // ===== The picked app's settings =====

    public IReadOnlyList<string> ModeOptions { get; }

    public IReadOnlyList<string> CpuOptions { get; }

    public IReadOnlyList<string> GpuOptions { get; }

    public IReadOnlyList<string> FanOptions { get; }

    public IReadOnlyList<string> RateOptions { get; }

    public IReadOnlyList<string> ColorOptions { get; }

    public IReadOnlyList<string> KeyboardOptions { get; }

    public IReadOnlyList<string> BrightnessOptions { get; }

    public bool HasMaxFan => _performance.HasMaxFan;

    public int ModeIndex
    {
        get => IndexOf(Modes, Selected?.Profile.Mode);
        set => Edit(value, Modes, (profile, mode) => profile with { Mode = mode });
    }

    /// <summary>Custom's levels are only asked for in Custom.</summary>
    public bool IsCustomMode => Selected?.Profile.Mode == PerformanceMode.Custom;

    public int CpuIndex
    {
        get => (int)(Selected?.Profile.Cpu ?? CpuBoost.High);
        set => Edit(value, Enum.GetValues<CpuBoost>(), (profile, level) => profile with { Cpu = level });
    }

    public int GpuIndex
    {
        get => (int)(Selected?.Profile.Gpu ?? GpuBoost.High);
        set => Edit(value, Enum.GetValues<GpuBoost>(), (profile, level) => profile with { Gpu = level });
    }

    public int FanIndex
    {
        get => IndexOf(Fans, Selected?.Profile.MaxFan);
        set => Edit(value, Fans, (profile, fan) => profile with { MaxFan = fan });
    }

    public int RateIndex
    {
        get => IndexOf(_rates, Selected?.Profile.RefreshHz);
        set => Edit(value, _rates, (profile, hertz) => profile with { RefreshHz = hertz });
    }

    public int ColorIndex
    {
        get => IndexOf(Colors, Selected?.Profile.ColorProfile is { } colors ? ColorProfiles.Parse(colors) : null);
        set => Edit(value, Colors, (profile, colors) => profile with { ColorProfile = colors?.ToString() });
    }

    public int KeyboardIndex
    {
        get => IndexOf(_effects, Selected?.Profile.Keyboard);

        // An effect that runs through its own colors drops the color chosen.
        set => Edit(value, _effects, (profile, effect) => profile with
        {
            Keyboard = effect,
            KeyboardColor = AppProfile.TakesColor(effect) ? profile.KeyboardColor : null
        });
    }

    /// <summary>"No change", then each color with its swatch.</summary>
    public IReadOnlyList<ColorOption> KeyboardColorOptions { get; }

    /// <summary>A color to choose: on a laptop that shows one, with an effect that takes one (Static, Breathing).</summary>
    public bool OffersKeyboardColor => _keyboardColors.Length > 1 && AppProfile.TakesColor(Selected?.Profile.Keyboard);

    public int KeyboardColorIndex
    {
        get => IndexOf(_keyboardColors, Selected?.Profile.KeyboardColor);
        set => Edit(value, _keyboardColors, (profile, color) => profile with { KeyboardColor = color });
    }

    /// <summary>No brightness to choose while the profile turns the keyboard off.</summary>
    public bool OffersBrightness => Selected?.Profile.Keyboard != KeyboardEffect.Off;

    public int BrightnessIndex
    {
        get => IndexOf(Brightnesses, Selected?.Profile.KeyboardBrightness);
        set => Edit(value, Brightnesses, (profile, percent) => profile with { KeyboardBrightness = percent });
    }

    public IReadOnlyList<string> GameModeOptions { get; }

    public int GameModeIndex
    {
        get => IndexOf(GameModes, Selected?.Profile.GameMode);
        set => Edit(value, GameModes, (profile, block) => profile with { GameMode = block });
    }

    // A dropdown sends -1 while its list is swapped: ignored.
    private void Edit<T>(int index, IReadOnlyList<T> values, Func<AppProfile, T, AppProfile> edit)
    {
        if (Selected is not { } row || index < 0 || index >= values.Count)
            return;

        var edited = edit(row.Profile, values[index]);

        if (edited == row.Profile)
            return;

        row.Profile = edited;
        RaiseEditor();
    }

    private static int IndexOf<T>(IReadOnlyList<T> values, T value) =>
        Math.Max(0, values.ToList().IndexOf(value));

    private void RaiseEditor()
    {
        foreach (var name in new[]
                 {
                     nameof(ModeIndex), nameof(IsCustomMode), nameof(CpuIndex), nameof(GpuIndex), nameof(FanIndex), nameof(RateIndex),
                     nameof(ColorIndex), nameof(KeyboardIndex), nameof(OffersKeyboardColor), nameof(KeyboardColorIndex),
                     nameof(OffersBrightness), nameof(BrightnessIndex), nameof(GameModeIndex), nameof(ShowsEditor)
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Every setting of the picked app back to No change: it no longer changes anything.</summary>
    [RelayCommand]
    private void Reset()
    {
        if (Selected is not { } row || !row.IsConfigured && row.Enabled)
            return;

        var profile = row.Profile;
        row.Profile = new AppProfile(profile.Name, profile.Path, profile.IconPath, profile.Source);
        AppLog.Info($"App profile reset: {row.Name}.");
        RaiseEditor();
    }

    // A setting changed: an app that now changes something is saved, one that changes nothing is let go.
    private void OnRowChanged(AppRow row)
    {
        if (row.Profile.ChangesAnything)
            _profiles[row.Path] = row.Profile;
        else
            _profiles.Remove(row.Path);

        _services.UpdateSettings(settings => settings with { AppProfiles = [.. _profiles.Values] });
        _ = CheckAsync();
    }

    // The name a program gives itself ("Google Chrome"), else its file name.
    private static string ProgramName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var name = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;

            if (!string.IsNullOrWhiteSpace(name))
                return GameLibrary.CleanName(name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    // ===== Putting profiles on and taking them off =====

    /// <summary>Starts watching for the apps: after the laptop's own settings have been put back at startup.</summary>
    public void Start()
    {
        _watch.Start();
        _ = CheckAsync();
    }

    /// <summary>As the app quits: an app profile still on is taken off, so the laptop is left as it was.</summary>
    public async Task StopAsync()
    {
        _watch.Stop();

        if (_active is not null)
            await SwitchAsync(null, notify: false);

        _gameMode.Dispose();
    }

    private async Task CheckAsync()
    {
        if (_checking)
            return;

        _checking = true;

        try
        {
            var profiles = ProfilesOn ? _profiles.Values.Where(profile => profile.Enabled && profile.ChangesAnything).ToList() : [];
            AppProfile? target = null;

            if (profiles.Count > 0)
            {
                var running = await Task.Run(_programs.Snapshot);
                target = AppProfileMatcher.Find(running, profiles);
            }

            if (target != _active)
                await SwitchAsync(target);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not check the app profiles.", exception);
        }
        finally
        {
            _checking = false;
        }
    }

    private void ShowRunning()
    {
        foreach (var row in Apps)
            row.IsRunning = _active is not null && SameApp(row.Path, _active.Path);
    }

    private async Task SwitchAsync(AppProfile? target, bool notify = true)
    {
        var previous = _active;
        _active = target;
        ShowRunning();

        if (target is null)
        {
            AppLog.Info($"App profile for {previous?.Name} off; putting back what it changed.");

            // Said at once: putting everything back takes a moment.
            if (notify && previous is not null)
                ModeToastWindow.ShowAppNotice(previous.Name, IconOf(previous), null, L.T("Settings put back"), applied: true);

            await _performance.SetAppOverrideAsync(null, null);
            await _display.SetAppOverrideAsync(null, null);
            await _lighting.SetAppOverrideAsync(null, null);
            _gameMode.Block(null);
            return;
        }

        var sameApp = previous is not null && SameApp(previous.Path, target.Path);

        // Another app's: the keyboard goes back to how it was first, so the next one starts from there.
        if (previous is not null && !sameApp)
            await _lighting.SetAppOverrideAsync(null, null);

        var (applied, status) = await _performance.SetAppOverrideAsync(target.Performance, target.MaxFan);
        var colors = target.ColorProfile is null ? (ColorProfile?)null : ColorProfiles.Parse(target.ColorProfile);
        var shown = await _display.SetAppOverrideAsync(target.RefreshHz, colors);
        await _lighting.SetAppOverrideAsync(target.Keyboard, target.KeyboardBrightness, target.KeyboardColor);
        _gameMode.Block(target.GameMode);

        var summary = AppProfileText.Summary(target);
        AppLog.Info($"App profile for {target.Name} on: {summary}{(applied ? string.Empty : $" ({status})")}{(shown ? string.Empty : " (screen change waiting or refused)")}.");

        if (!notify)
            return;

        // An edit to the profile of the app running now: the page is open, so the line under the tabs says it.
        if (sameApp)
            _report(applied ? L.T("App profile applied.") : status, !applied);
        else
            ModeToastWindow.ShowAppNotice(target.Name, IconOf(target), target.Mode, applied ? summary : status, applied);
    }

    private static bool SameApp(string one, string other) =>
        string.Equals(one.TrimEnd('\\'), other.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static ImageSource? IconOf(AppProfile profile) =>
        AppIcons.For(profile.IconPath ?? (AppProfileMatcher.IsProgram(profile.Path) ? profile.Path : null));
}
