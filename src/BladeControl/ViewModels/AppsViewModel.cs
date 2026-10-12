using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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

/// <summary>An app in the Apps page's list: its icon, name, what its profile changes, and its switch.</summary>
internal sealed partial class AppRow : ObservableObject
{
    private readonly Action<AppRow> _changed;
    private AppProfile _profile;

    public AppRow(AppProfile profile, Action<AppRow> changed)
    {
        _profile = profile;
        _changed = changed;
        Icon = AppIcons.For(profile.IconPath ?? (AppProfileMatcher.IsProgram(profile.Path) ? profile.Path : null));
    }

    public AppProfile Profile
    {
        get => _profile;
        set
        {
            if (!SetProperty(ref _profile, value))
                return;

            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Enabled));
            _changed(this);
        }
    }

    public string Name => _profile.Name;

    public string Summary => AppProfileText.Summary(_profile);

    /// <summary>Where it came from ("Steam"), and the program or folder it follows, for the editor.</summary>
    public string SourceName => AppProfileText.SourceName(_profile.Source);

    public string Path => _profile.Path;

    public ImageSource? Icon { get; }

    /// <summary>The switch beside it: off keeps the profile but does not use it.</summary>
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

/// <summary>An app or game that can be added: one running now, or one found in a launcher's library.</summary>
internal sealed class AppCandidate(string name, string path, string? iconPath, AppSource source, string detail)
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    public string? IconPath { get; } = iconPath;

    public AppSource Source { get; } = source;

    /// <summary>The launcher, or the program's file name.</summary>
    public string Detail { get; } = detail;

    public ImageSource? Icon => AppIcons.For(IconPath);
}

/// <summary>
/// The Apps page: a profile for each app or game (the mode, fans, refresh
/// rate, screen colors and keyboard to use while it runs), and what puts them
/// on. Every two seconds it looks at what is running; when an app with a
/// profile starts, its profile goes on with a notice, and when it closes
/// everything goes back as it was. Something changed by hand meanwhile stays
/// as it is. With two such apps running, the one started last wins.
/// </summary>
internal sealed partial class AppsViewModel : ObservableObject
{
    private static readonly PerformanceMode?[] Modes = [null, PerformanceMode.Silent, PerformanceMode.Balanced, PerformanceMode.Custom];
    private static readonly bool?[] Fans = [null, false, true];
    private static readonly ColorProfile?[] Colors = [null, ColorProfile.Standard, ColorProfile.Warm, ColorProfile.Cool, ColorProfile.Contrast];
    private static readonly int?[] Brightnesses = [null, 25, 50, 75, 100];

    private readonly AppServices _services;
    private readonly PerformanceViewModel _performance;
    private readonly DisplayViewModel _display;
    private readonly LightingViewModel _lighting;
    private readonly Action<string, bool> _report;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly RunningPrograms _programs = new();
    private readonly KeyboardEffect?[] _effects;
    private readonly int?[] _rates;
    private AppProfile? _active;
    private bool _checking;

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

        Apps = [.. (services.Settings.AppProfiles ?? []).Select(profile => new AppRow(profile, OnRowChanged))];
        Apps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasApps));

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

        _selected = Apps.FirstOrDefault();
        _isAdding = Apps.Count == 0;

        _performance.AppOverrideTakenOver += (_, _) => AppLog.Info("The mode was changed by hand while an app profile held it; it is kept when the app closes.");
        _watch.Tick += async (_, _) => await CheckAsync();
    }

    // ===== The list =====

    public ObservableCollection<AppRow> Apps { get; }

    public bool HasApps => Apps.Count > 0;

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

    partial void OnSelectedChanged(AppRow? value)
    {
        if (value is not null)
            IsAdding = false;

        RaiseEditor();
    }

    /// <summary>The right column shows the selected app's settings (otherwise the list of apps to add).</summary>
    public bool ShowsEditor => Selected is not null && !IsAdding;

    // ===== The selected app's settings =====

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
        set => Edit(value, _effects, (profile, effect) => profile with { Keyboard = effect });
    }

    /// <summary>No brightness to choose while the profile turns the keyboard off.</summary>
    public bool OffersBrightness => Selected?.Profile.Keyboard != KeyboardEffect.Off;

    public int BrightnessIndex
    {
        get => IndexOf(Brightnesses, Selected?.Profile.KeyboardBrightness);
        set => Edit(value, Brightnesses, (profile, percent) => profile with { KeyboardBrightness = percent });
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
                     nameof(ColorIndex), nameof(KeyboardIndex), nameof(OffersBrightness), nameof(BrightnessIndex), nameof(ShowsEditor)
                 })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { } row)
            return;

        var index = Apps.IndexOf(row);
        Apps.Remove(row);
        Save();
        AppLog.Info($"App profile removed: {row.Name}.");

        Selected = Apps.Count == 0 ? null : Apps[Math.Min(index, Apps.Count - 1)];
        IsAdding = Apps.Count == 0;
        _ = CheckAsync();
    }

    private void OnRowChanged(AppRow row)
    {
        Save();
        _ = CheckAsync();
    }

    private void Save() =>
        _services.UpdateSettings(settings => settings with { AppProfiles = [.. Apps.Select(row => row.Profile)] });

    // ===== Adding an app =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEditor))]
    private bool _isAdding;

    partial void OnIsAddingChanged(bool value)
    {
        if (value)
            _ = LoadCandidatesAsync();
    }

    /// <summary>Which list to add from: games found in launchers, or the apps open now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddingOpenApps))]
    private bool _addingGames = true;

    public bool AddingOpenApps
    {
        get => !AddingGames;
        set => AddingGames = !value;
    }

    partial void OnAddingGamesChanged(bool value) => _ = LoadCandidatesAsync();

    public ObservableCollection<AppCandidate> Candidates { get; } = [];

    [ObservableProperty]
    private bool _scanning;

    /// <summary>Under the list: nothing found, and where it was looked for.</summary>
    [ObservableProperty]
    private string _candidatesStatus = string.Empty;

    /// <summary>When the page opens: with no apps yet, the games to add are listed straight away.</summary>
    public void Refresh()
    {
        if (IsAdding && Candidates.Count == 0)
            _ = LoadCandidatesAsync();
    }

    [RelayCommand]
    private void StartAdd()
    {
        if (IsAdding)
            _ = LoadCandidatesAsync();
        else
            IsAdding = true;
    }

    /// <summary>Back to the selected app's settings; with none yet there is nothing to go back to.</summary>
    [RelayCommand]
    private void CancelAdd()
    {
        if (HasApps)
        {
            IsAdding = false;
            Selected ??= Apps[0];
        }
    }

    [RelayCommand]
    private void AddCandidate(AppCandidate? candidate)
    {
        if (candidate is not null)
            Add(candidate.Name, candidate.Path, candidate.IconPath, candidate.Source);
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

        if (dialog.ShowDialog() == true)
            Add(ProgramName(dialog.FileName), dialog.FileName, dialog.FileName, AppSource.Manual);
    }

    private void Add(string name, string path, string? iconPath, AppSource source)
    {
        if (Apps.FirstOrDefault(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            Selected = existing;
            return;
        }

        var row = new AppRow(new AppProfile(name, path, iconPath, source), OnRowChanged);
        Apps.Add(row);
        Save();
        AppLog.Info($"App profile added: {name} ({source}).");
        Selected = row;
        IsAdding = false;
    }

    private bool _loading;

    private async Task LoadCandidatesAsync()
    {
        if (_loading)
            return;

        _loading = true;
        Scanning = true;
        Candidates.Clear();
        CandidatesStatus = string.Empty;
        var games = AddingGames;

        try
        {
            var found = await Task.Run(() => games ? FindGames() : FindOpenApps());
            var added = new HashSet<string>(Apps.Select(row => row.Path), StringComparer.OrdinalIgnoreCase);

            // The list may have been switched meanwhile: only the latest is shown.
            if (games == AddingGames)
            {
                foreach (var candidate in found.Where(candidate => !added.Contains(candidate.Path)))
                    Candidates.Add(candidate);

                if (Candidates.Count == 0)
                    CandidatesStatus = games
                        ? L.T("No games found in Steam, Epic Games, Xbox or Battle.net.")
                        : L.T("No other apps with a window are open.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not list the apps to add.", exception);
            CandidatesStatus = L.T("Could not list the apps.");
        }

        Scanning = false;
        _loading = false;

        // Switched while this one was loading.
        if (games != AddingGames && IsAdding)
            _ = LoadCandidatesAsync();
    }

    private static List<AppCandidate> FindGames() =>
        [.. GameLibrary.Scan().Select(game => new AppCandidate(
            game.Name, game.Folder, GameLibrary.FindProgram(game), game.Source, AppProfileText.SourceName(game.Source)))];

    // The apps with a window open now, Windows' own and Blade Control left out.
    private static List<AppCandidate> FindOpenApps()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var found = new List<AppCandidate>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var path = process.MainModule?.FileName;

                    if (path is null || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                        continue;

                    found.Add(new AppCandidate(ProgramName(path), path, path, AppSource.Manual, System.IO.Path.GetFileName(path)));
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                {
                    // One we may not look at (running as administrator): left out.
                }
            }
        }

        return [.. found
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)];
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
    }

    private async Task CheckAsync()
    {
        if (_checking)
            return;

        _checking = true;

        try
        {
            var profiles = ProfilesOn ? Apps.Select(row => row.Profile).Where(profile => profile.Enabled && profile.ChangesAnything).ToList() : [];
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

    private async Task SwitchAsync(AppProfile? target, bool notify = true)
    {
        var previous = _active;
        _active = target;

        foreach (var row in Apps)
            row.IsRunning = target is not null && SameApp(row.Profile, target);

        if (target is null)
        {
            AppLog.Info($"App profile for {previous?.Name} off; putting back what it changed.");

            // Said at once: putting everything back takes a moment.
            if (notify && previous is not null)
                ModeToastWindow.ShowAppNotice(previous.Name, IconOf(previous), null, L.T("Settings put back"), applied: true);

            await _performance.SetAppOverrideAsync(null, null);
            await _display.SetAppOverrideAsync(null, null);
            await _lighting.SetAppOverrideAsync(null, null);
            return;
        }

        var sameApp = previous is not null && SameApp(previous, target);

        // Another app's: the keyboard goes back to how it was first, so the next one starts from there.
        if (previous is not null && !sameApp)
            await _lighting.SetAppOverrideAsync(null, null);

        var (applied, status) = await _performance.SetAppOverrideAsync(target.Performance, target.MaxFan);
        var colors = target.ColorProfile is null ? (ColorProfile?)null : ColorProfiles.Parse(target.ColorProfile);
        var shown = await _display.SetAppOverrideAsync(target.RefreshHz, colors);
        await _lighting.SetAppOverrideAsync(target.Keyboard, target.KeyboardBrightness);

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

    private static bool SameApp(AppProfile one, AppProfile other) =>
        string.Equals(one.Path, other.Path, StringComparison.OrdinalIgnoreCase);

    private static ImageSource? IconOf(AppProfile profile) =>
        AppIcons.For(profile.IconPath ?? (AppProfileMatcher.IsProgram(profile.Path) ? profile.Path : null));
}
