using System.Runtime.InteropServices;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using BladeControl.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BladeControl.ViewModels;

/// <summary>
/// The Audio page, as in Razer Helper: the output device Windows plays to
/// (picking another switches Windows to it), and that device's equalizer:
/// on or off, a preset to start from, ten bands, and Bass enhancer, Dynamic
/// boost and Clarity added to whatever curve plays. Equalizer APO does the
/// equalizing; when it is missing, or not hooked into the device, a note
/// says so with the way to fix it.
/// </summary>
/// <remarks>
/// Each device keeps its own curve and boosts, which play as soon as they
/// change: Equalizer APO reads its file again by itself. A preset is where a
/// curve starts; changing a band keeps the preset named until Save keeps the
/// change in it (the app's own presets never change: Save asks for a new name).
/// </remarks>
internal sealed partial class AudioViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string, bool> _report;
    private readonly List<EqPreset> _savedPresets;
    private readonly Dictionary<string, AudioDeviceEq> _devices;

    // Writes wait a moment after the last change, so a drag writes the file a few times, not on every step.
    private readonly DispatcherTimer _writeDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private IReadOnlyList<AudioDevice> _shownDevices = [];
    private string? _deviceId;
    private bool _showing;

    public AudioViewModel(AppServices services, Action<string, bool> report)
    {
        _services = services;
        _report = report;
        _savedPresets = [.. (services.Settings.EqPresets ?? []).Where(preset => !EqPreset.IsBuiltIn(preset.Name))];
        _devices = new Dictionary<string, AudioDeviceEq>(services.Settings.AudioDevices ?? [], StringComparer.OrdinalIgnoreCase);

        _writeDelay.Tick += (_, _) =>
        {
            _writeDelay.Stop();
            Commit();
        };

        RefreshPresetList();
    }

    // ===== Output device =====

    [ObservableProperty]
    private IReadOnlyList<string> _deviceNames = [];

    private int _deviceIndex = -1;

    /// <summary>The device shown; picking another makes it the one Windows plays to.</summary>
    public int DeviceIndex
    {
        get => _deviceIndex;
        set
        {
            if (value < 0 || value >= _shownDevices.Count || !SetProperty(ref _deviceIndex, value) || _showing)
                return;

            var id = _shownDevices[value].Id;
            MakeDefault(id);
            ShowDevice(id);
        }
    }

    [ObservableProperty]
    private bool _hasDevice;

    // ===== Equalizer =====

    private bool _eqEnabled = true;

    public bool EqEnabled
    {
        get => _eqEnabled;
        set
        {
            if (SetProperty(ref _eqEnabled, value))
                Change(device => device with { Enabled = value });
        }
    }

    [ObservableProperty]
    private IReadOnlyList<string> _presetNames = [];

    private int _presetIndex = -1;

    public int PresetIndex
    {
        get => _presetIndex;
        set
        {
            if (!SetProperty(ref _presetIndex, value) || _showing || Presets.ElementAtOrDefault(value) is not { } preset)
                return;

            Change(device => device.WithPreset(preset));
            ShowCurve();
        }
    }

    private IReadOnlyList<double> _gains = EqBands.Flat();

    /// <summary>The ten bands, as the graph shows and sets them.</summary>
    public IReadOnlyList<double> Gains
    {
        get => _gains;
        set
        {
            if (SetProperty(ref _gains, value))
                Change(device => device with { Gains = [.. value] });
        }
    }

    private int _bassBoost;

    public int BassBoost
    {
        get => _bassBoost;
        set
        {
            if (SetProperty(ref _bassBoost, value))
                Change(device => device with { BassBoost = value });
        }
    }

    private int _dynamicBoost;

    public int DynamicBoost
    {
        get => _dynamicBoost;
        set
        {
            if (SetProperty(ref _dynamicBoost, value))
                Change(device => device with { DynamicBoost = value });
        }
    }

    private int _clarityBoost;

    public int ClarityBoost
    {
        get => _clarityBoost;
        set
        {
            if (SetProperty(ref _clarityBoost, value))
                Change(device => device with { ClarityBoost = value });
        }
    }

    public int BoostLevels => EqBoosts.Levels;

    /// <summary>Save keeps a change in the user's own preset, or asks for a name; nothing to keep, nothing to do.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _canSave;

    /// <summary>Only the user's own presets can be deleted.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _canDelete;

    // ===== Why the equalizer cannot play yet =====

    [ObservableProperty]
    private bool _showsNotice;

    [ObservableProperty]
    private string _noticeText = string.Empty;

    [ObservableProperty]
    private string _noticeButton = string.Empty;

    [ObservableProperty]
    private bool _noticeHasButton;

    [ObservableProperty]
    private bool _noticeIsError;

    private bool _noticeOpensSelector;

    // ===== Life cycle =====

    /// <summary>When the page opens: the devices plugged in now, and whether Equalizer APO is ready for the one shown.</summary>
    public void Refresh()
    {
        _shownDevices = WindowsAudio.OutputDevices();

        _showing = true;
        DeviceNames = [.. _shownDevices.Select(device => device.Name)];
        _showing = false;

        var defaultId = WindowsAudio.DefaultOutputId();
        var keep = _shownDevices.FirstOrDefault(device => Same(device.Id, defaultId))
                   ?? _shownDevices.FirstOrDefault(device => Same(device.Id, _deviceId))
                   ?? _shownDevices.FirstOrDefault();

        if (keep is null)
        {
            _deviceId = null;
            HasDevice = false;
            ShowState();
            return;
        }

        ShowDevice(keep.Id);
    }

    /// <summary>As the page closes or the app exits: a change still waiting is written now, not lost.</summary>
    public void Flush()
    {
        if (!_writeDelay.IsEnabled)
            return;

        _writeDelay.Stop();
        Commit();
    }

    // ===== Commands =====

    [RelayCommand]
    private void Reset()
    {
        Change(device => device.WithPreset(EqPreset.BuiltIn[0]) with { BassBoost = 0, DynamicBoost = 0, ClarityBoost = 0 });
        ShowCurve();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var current = Current;

        if (current.Preset is { } name && !EqPreset.IsBuiltIn(name) && IndexOfPreset(name) >= 0)
            Keep(new EqPreset(name, [.. current.Gains]));
        else
            New();
    }

    [RelayCommand]
    private void New()
    {
        if (_deviceId is null)
            return;

        var suggested = Current.Preset is { } name && !EqPreset.IsBuiltIn(name) ? name : L.T("My preset");

        if (PresetNameWindow.Ask(suggested) is { } chosen)
            Keep(new EqPreset(chosen, [.. Current.Gains]));
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (Current.Preset is not { } name)
            return;

        _savedPresets.RemoveAll(each => Same(each.Name, name));

        // Any device that started from it keeps its curve, with no preset named.
        foreach (var (id, device) in _devices.ToList())
        {
            if (Same(device.Preset, name))
                _devices[id] = device with { Preset = null };
        }

        RefreshPresetList();
        Commit();
    }

    [RelayCommand]
    private async Task NoticeAction()
    {
        if (!_noticeOpensSelector)
        {
            ExternalLinks.OpenEqualizerApoDownload();
            return;
        }

        await EqualizerApo.OpenDeviceSelectorAsync();
        ShowState();

        // A device just hooked in gets its curve at once.
        if (_deviceId is not null && !ShowsNotice)
            Commit();
    }

    // ===== Showing =====

    /// <summary>The presets offered: the app's, then the user's.</summary>
    private IReadOnlyList<EqPreset> Presets => [.. EqPreset.BuiltIn, .. _savedPresets];

    private AudioDeviceEq Current =>
        _deviceId is not null && _devices.TryGetValue(_deviceId, out var saved) ? saved.Normalized() : AudioDeviceEq.Default;

    private void ShowDevice(string id)
    {
        _deviceId = id;
        HasDevice = true;

        _showing = true;
        _deviceIndex = _shownDevices.ToList().FindIndex(device => Same(device.Id, id));
        OnPropertyChanged(nameof(DeviceIndex));
        _showing = false;

        ShowCurve();
        ShowState();
    }

    // The device's equalizer, as saved.
    private void ShowCurve()
    {
        var device = Current;

        _showing = true;
        SetProperty(ref _eqEnabled, device.Enabled, nameof(EqEnabled));
        SetProperty(ref _gains, [.. device.Gains], nameof(Gains));
        SetProperty(ref _bassBoost, device.BassBoost, nameof(BassBoost));
        SetProperty(ref _dynamicBoost, device.DynamicBoost, nameof(DynamicBoost));
        SetProperty(ref _clarityBoost, device.ClarityBoost, nameof(ClarityBoost));
        _showing = false;

        RefreshPresetList();
    }

    // Why the equalizer cannot play yet, when it cannot.
    private void ShowState()
    {
        var installed = EqualizerApo.IsInstalled;
        var hooked = installed && _deviceId is not null && EqualizerApo.IsHookedOn(_deviceId);

        ShowsNotice = !hooked;
        NoticeIsError = false;
        _noticeOpensSelector = installed;
        NoticeText = !installed
            ? L.T("The equalizer needs Equalizer APO.")
            : _deviceId is not null
                ? L.T("Equalizer APO isn't turned on for this device yet.")
                : L.T("No output device is plugged in.");
        NoticeButton = L.T(installed ? "Turn on for this device" : "Download Equalizer APO");
        NoticeHasButton = !installed || _deviceId is not null;
    }

    private void RefreshPresetList()
    {
        _showing = true;
        PresetNames = [.. Presets.Select(preset => EqPreset.IsBuiltIn(preset.Name) ? L.T(preset.Name) : preset.Name)];
        _presetIndex = IndexOfPreset(Current.Preset);
        OnPropertyChanged(nameof(PresetIndex));
        _showing = false;

        RefreshPresetButtons();
    }

    private void RefreshPresetButtons()
    {
        var current = Current;
        var preset = Presets.ElementAtOrDefault(IndexOfPreset(current.Preset));

        CanDelete = _deviceId is not null && preset is not null && !EqPreset.IsBuiltIn(preset.Name);
        CanSave = _deviceId is not null && (preset is null || !preset.Matches(current));
    }

    private int IndexOfPreset(string? name) =>
        name is null ? -1 : Presets.ToList().FindIndex(preset => Same(preset.Name, name));

    // ===== Changing =====

    // Changes the shown device's equalizer, saves it, and writes it out shortly.
    private void Change(Func<AudioDeviceEq, AudioDeviceEq> change)
    {
        if (_deviceId is null || _showing)
            return;

        _devices[_deviceId] = change(Current);
        RefreshPresetButtons();

        _writeDelay.Stop();
        _writeDelay.Start();
    }

    // Adds or replaces one of the user's presets and makes it the device's.
    private void Keep(EqPreset preset)
    {
        _savedPresets.RemoveAll(each => Same(each.Name, preset.Name));
        _savedPresets.Add(preset);
        _devices[_deviceId!] = Current with { Preset = preset.Name };

        RefreshPresetList();
        Commit();
    }

    private void Commit()
    {
        _services.UpdateSettings(settings => settings with
        {
            EqPresets = [.. _savedPresets],
            AudioDevices = new Dictionary<string, AudioDeviceEq>(_devices, StringComparer.OrdinalIgnoreCase)
        });

        if (EqualizerApo.ConfigFolder() is not { } folder)
            return;

        try
        {
            new EqualizerApoWriter(folder).Write(EqualizerApoConfig.Build(_devices));
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not write Equalizer APO's config.", exception);
            ShowsNotice = true;
            NoticeIsError = true;
            NoticeText = L.T("Could not save the equalizer to Equalizer APO.");
            NoticeHasButton = false;
        }
    }

    // The device picked becomes the one Windows plays to.
    private void MakeDefault(string id)
    {
        try
        {
            WindowsAudio.SetDefaultOutput(id);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            AppLog.Error("Could not change the default audio output.", exception);
            _report(L.T("Could not change the output device."), true);
        }
    }

    private static bool Same(string? one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
}
