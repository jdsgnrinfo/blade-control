using System.Windows.Media;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BladeControl.ViewModels;

/// <summary>How an effect's icon is drawn (see the EffectTile style).</summary>
public enum EffectIcon
{
    Static,
    Breathing,
    Spectrum,
    Wave,
    Off
}

/// <summary>One choice among the keyboard's or the logo's effects, or the colors.</summary>
internal sealed partial class LightingChoice<T>(T value, string name, string description, EffectIcon icon, Action<T> pick) : ObservableObject
{
    private bool _isSelected;

    public T Value { get; } = value;

    public string Name { get; } = name;

    public string Description { get; } = description;

    public EffectIcon Icon { get; } = icon;

    /// <summary>The swatch's color, for the color choices.</summary>
    public Brush? Swatch { get; init; }

    /// <summary>Checked by the radio button: picking it asks the laptop; the laptop's answer sets it for real.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
                pick(Value);

            // Shown as the laptop reports it, through Show.
            OnPropertyChanged();
        }
    }

    internal void Show(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}

/// <summary>
/// The Lighting page: the keyboard's effect, color and brightness, the logo's
/// effect and brightness, and when the keyboard light goes off by itself. As in
/// Razer Helper, every change is sent, then the laptop is read back and shown
/// as it says it is; a change asked for meanwhile waits its turn (only the latest).
/// </summary>
internal sealed partial class LightingViewModel : ObservableObject
{
    /// <summary>The idle times offered, in minutes.</summary>
    public static IReadOnlyList<int> IdleMinuteChoices { get; } = [1, 2, 5, 10, 15, 30];

    /// <summary>The battery charges offered, in percent.</summary>
    public static IReadOnlyList<int> BatteryPercentChoices { get; } = [10, 15, 20, 25, 30, 40, 50];

    private static readonly (string Name, RgbColor Color)[] PresetColors =
    [
        ("Razer green", new RgbColor(0x44, 0xD6, 0x2C)),
        ("White", RgbColor.White),
        ("Red", new RgbColor(0xFF, 0x00, 0x00)),
        ("Orange", new RgbColor(0xFF, 0x80, 0x00)),
        ("Yellow", new RgbColor(0xFF, 0xFF, 0x00)),
        ("Cyan", new RgbColor(0x00, 0xFF, 0xFF)),
        ("Blue", new RgbColor(0x00, 0x00, 0xFF)),
        ("Purple", new RgbColor(0x80, 0x00, 0xFF)),
        ("Pink", new RgbColor(0xFF, 0x00, 0x80))
    ];

    private readonly AppServices _services;
    private readonly Action<string, bool> _report;
    private readonly bool _offersColor;
    private readonly DispatcherTimer _lightsOffTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    // Every second plugged in, every three on battery.
    private readonly PacedTimer _brightnessWatch;
    private readonly DisplayStateWatcher _displayWatcher = new();
    private bool _busy;
    private (Func<Task> Change, string FailureMessage, bool ReadBack)? _queued;
    private bool _showing;
    private DateTime _lastBrightnessChange;
    private RgbColor _color = PresetColors[0].Color;
    private KeyboardEffect? _keyboardEffect;

    public LightingViewModel(AppServices services, Action<string, bool> report)
    {
        _services = services;
        _report = report;
        _brightnessWatch = new PacedTimer(services.PowerSource, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        _offersColor = services.Model?.HasKeyboardColor == true;

        // Wave only where the keyboard has zones to move across.
        KeyboardEffect[] effects = services.Model?.HasWaveEffect ?? true
            ? [KeyboardEffect.StaticGreen, KeyboardEffect.Breathing, KeyboardEffect.Spectrum, KeyboardEffect.Wave, KeyboardEffect.Off]
            : [KeyboardEffect.StaticGreen, KeyboardEffect.Breathing, KeyboardEffect.Spectrum, KeyboardEffect.Off];

        KeyboardEffects = [.. effects.Select(effect => new LightingChoice<KeyboardEffect>(
            effect, NameOf(effect), DescriptionOf(effect), IconOf(effect), PickKeyboardEffect))];

        LogoModes = [.. new[] { LogoMode.On, LogoMode.Breathing, LogoMode.Off }.Select(mode => new LightingChoice<LogoMode>(
            mode,
            L.T(mode == LogoMode.On ? "Static" : mode.ToString()),
            string.Empty,
            mode switch { LogoMode.Breathing => EffectIcon.Breathing, LogoMode.Off => EffectIcon.Off, _ => EffectIcon.Static },
            PickLogo))];

        Colors = _offersColor
            ? [.. PresetColors.Select(preset => new LightingChoice<RgbColor>(preset.Color, L.T(preset.Name), string.Empty, EffectIcon.Static, PickColor)
            {
                Swatch = Frozen(new SolidColorBrush(Color.FromRgb(preset.Color.Red, preset.Color.Green, preset.Color.Blue)))
            })]
            : [];

        var settings = services.Settings;
        _withScreen = settings.KeyboardOffWithScreen;
        _whenIdle = settings.LightsOffWhenIdle;
        _idleMinutes = Nearest(IdleMinuteChoices, settings.LightsOffIdleMinutes);
        _onLowBattery = settings.LightsOffOnLowBattery;
        _batteryPercent = Nearest(BatteryPercentChoices, settings.LightsOffBatteryPercent);

        _displayWatcher.DisplayChanged += (_, on) => _services.KeyboardLightsOff.OnDisplayChanged(on);
        _lightsOffTimer.Tick += (_, _) => CheckLightsOff();
        _brightnessWatch.Tick += async (_, _) => await ReadKeyboardBrightnessAsync();
        ApplyLightsOff();
    }

    // ===== Effects =====

    public IReadOnlyList<LightingChoice<KeyboardEffect>> KeyboardEffects { get; }

    public IReadOnlyList<LightingChoice<LogoMode>> LogoModes { get; }

    public IReadOnlyList<LightingChoice<RgbColor>> Colors { get; }

    public bool OffersColor => _offersColor;

    /// <summary>The effect the keyboard shows, with a line about it, under the icons.</summary>
    [ObservableProperty]
    private string _effectName = string.Empty;

    [ObservableProperty]
    private string _effectDescription = string.Empty;

    // ===== Brightness =====

    private int _keyboardBrightness;

    public int KeyboardBrightness
    {
        get => _keyboardBrightness;
        set
        {
            if (!SetProperty(ref _keyboardBrightness, value) || _showing)
                return;

            // The steps of a drag only send; letting go reads back (see CommitKeyboardBrightness).
            _lastBrightnessChange = DateTime.UtcNow;
            _ = ApplyAsync(() => _services.Lighting.SetKeyboardBrightnessAsync(value), L.T("Could not change the keyboard brightness."), readBack: false);
        }
    }

    public void CommitKeyboardBrightness()
    {
        var value = _keyboardBrightness;
        _ = ApplyAsync(() => _services.Lighting.SetKeyboardBrightnessAsync(value), L.T("Could not change the keyboard brightness."));
    }

    // ===== When the light goes off =====

    public IReadOnlyList<string> IdleMinuteTexts { get; } = [.. IdleMinuteChoices.Select(minutes => L.F("{0} min", minutes))];

    public IReadOnlyList<string> BatteryPercentTexts { get; } = [.. BatteryPercentChoices.Select(percent => $"{percent}%")];

    private bool _withScreen;

    public bool WithScreen
    {
        get => _withScreen;
        set
        {
            if (SetProperty(ref _withScreen, value))
                SaveLightsOff();
        }
    }

    private bool _whenIdle;

    public bool WhenIdle
    {
        get => _whenIdle;
        set
        {
            if (SetProperty(ref _whenIdle, value))
                SaveLightsOff();
        }
    }

    private int _idleMinutes;

    /// <summary>The index of the chosen time in <see cref="IdleMinuteChoices"/>.</summary>
    public int IdleMinutesIndex
    {
        get => _idleMinutes;
        set
        {
            if (value >= 0 && SetProperty(ref _idleMinutes, value))
                SaveLightsOff();
        }
    }

    private bool _onLowBattery;

    public bool OnLowBattery
    {
        get => _onLowBattery;
        set
        {
            if (SetProperty(ref _onLowBattery, value))
                SaveLightsOff();
        }
    }

    private int _batteryPercent;

    /// <summary>The index of the chosen charge in <see cref="BatteryPercentChoices"/>.</summary>
    public int BatteryPercentIndex
    {
        get => _batteryPercent;
        set
        {
            if (value >= 0 && SetProperty(ref _batteryPercent, value))
                SaveLightsOff();
        }
    }

    // ===== Life cycle =====

    /// <summary>Reads what the laptop's lighting is set to.</summary>
    public async Task RefreshAsync()
    {
        if (_busy)
            return;

        _busy = true;
        Show(await ReadStateOrUnknownAsync(L.T("Could not read the lighting state.")));
        EndBusy();
    }

    /// <summary>While the page is open, the slider follows the Fn brightness keys.</summary>
    public void SetPageOpen(bool open)
    {
        if (open)
        {
            _ = RefreshAsync();
            _brightnessWatch.Start();
        }
        else
        {
            _brightnessWatch.Stop();
        }
    }

    /// <summary>As the app exits: never leave the keyboard dark behind.</summary>
    public void Shutdown()
    {
        _lightsOffTimer.Stop();
        _brightnessWatch.Stop();
        _displayWatcher.Dispose();
        _services.KeyboardLightsOff.Restore();
    }

    // ===== Picking =====

    // Each pick is shown at once; the laptop's answer, read back after, corrects it if need be.
    private void PickKeyboardEffect(KeyboardEffect effect)
    {
        ShowPending(_state with { Keyboard = effect });
        _ = ApplyAsync(
            _offersColor && IsColored(effect)
                ? () => SetColoredEffectAsync(effect, _color)
                : () => _services.Lighting.SetKeyboardEffectAsync(effect),
            L.T("Could not change the keyboard lighting."));
    }

    private void PickLogo(LogoMode mode)
    {
        ShowPending(_state with { Logo = mode });
        _ = ApplyAsync(() => _services.Lighting.SetLogoAsync(mode), L.T("Could not change the logo lighting."));
    }

    // A color goes on the effect shown when it takes one, Static otherwise.
    private void PickColor(RgbColor color)
    {
        _color = color;
        var effect = _keyboardEffect is { } shown && IsColored(shown) ? shown : KeyboardEffect.StaticGreen;
        ShowPending(_state with { Keyboard = effect, KeyboardColor = color });
        _ = ApplyAsync(() => SetColoredEffectAsync(effect, color), L.T("Could not change the keyboard color."));
    }

    private static bool IsColored(KeyboardEffect effect) => effect is KeyboardEffect.StaticGreen or KeyboardEffect.Breathing;

    private Task SetColoredEffectAsync(KeyboardEffect effect, RgbColor color) =>
        effect == KeyboardEffect.Breathing
            ? _services.Lighting.SetKeyboardBreathingAsync(color)
            : _services.Lighting.SetKeyboardColorAsync(color);

    // ===== App profiles =====

    // The keyboard as it was before an app's profile changed it, and what the
    // profile set; null while no app profile holds the keyboard.
    private LightingState? _beforeApp;
    private (KeyboardEffect? Effect, int? Brightness, RgbColor? Color) _appSet;

    /// <summary>The keyboard's effects offered on this laptop, for the Apps page.</summary>
    public IReadOnlyList<KeyboardEffect> OfferedEffects => [.. KeyboardEffects.Select(choice => choice.Value)];

    /// <summary>The colors offered, with their names and swatches, for the Apps page; none where the keyboard shows only green.</summary>
    public IReadOnlyList<LightingChoice<RgbColor>> OfferedColors => Colors;

    /// <summary>An effect's name as this page shows it.</summary>
    public string EffectNameOf(KeyboardEffect effect) => NameOf(effect);

    /// <summary>
    /// Puts an app's keyboard effect, color and brightness on, or with all
    /// null puts back what the keyboard had before. Something changed by hand
    /// meanwhile (the Fn keys, this page) stays as it is. A color goes with an
    /// effect that takes one; without an effect, on the effect shown when it does.
    /// </summary>
    public async Task SetAppOverrideAsync(KeyboardEffect? effect, int? brightness, RgbColor? color = null)
    {
        if (!_offersColor)
            color = null;

        if (effect is null && brightness is null && color is null)
        {
            if (_beforeApp is { } before)
                await RestoreBeforeAppAsync(before);

            _beforeApp = null;
            return;
        }

        _beforeApp ??= await ReadStateOrUnknownAsync(L.T("Could not read the lighting state."));
        // Only an effect that shows a color takes one.
        var colored = effect ?? (_keyboardEffect is { } shown && IsColored(shown) ? shown : null);

        if (colored is null || !IsColored(colored.Value))
            color = null;

        _appSet = (effect, brightness, color);

        if (effect is not null || color is not null)
            await ApplyAsync(EffectChange((effect ?? colored)!.Value, color ?? _color), L.T("Could not change the keyboard lighting."));

        if (brightness is { } percent)
            await ApplyAsync(() => _services.Lighting.SetKeyboardBrightnessAsync(Math.Clamp(percent, 0, 100)), L.T("Could not change the keyboard brightness."));
    }

    private async Task RestoreBeforeAppAsync(LightingState before)
    {
        var now = await ReadStateOrUnknownAsync(L.T("Could not read the lighting state."));

        // The effect, or only the color, the app set and still on: put back as it was.
        var effectStill = _appSet.Effect is { } set && now.Keyboard == set && before.Keyboard != set;
        var colorStill = _appSet.Color is { } setColor && now.KeyboardColor == setColor && before.KeyboardColor != setColor;

        if ((effectStill || colorStill) && before.Keyboard is { } effect)
            await ApplyAsync(EffectChange(effect, before.KeyboardColor ?? _color), L.T("Could not change the keyboard lighting."));

        if (_appSet.Brightness is { } setPercent && before.KeyboardBrightness is { } percent && percent != setPercent &&
            now.KeyboardBrightness is { } shown && Math.Abs(shown - setPercent) <= 1)
            await ApplyAsync(() => _services.Lighting.SetKeyboardBrightnessAsync(percent), L.T("Could not change the keyboard brightness."));
    }

    private Func<Task> EffectChange(KeyboardEffect effect, RgbColor color) =>
        _offersColor && IsColored(effect)
            ? () => SetColoredEffectAsync(effect, color)
            : () => _services.Lighting.SetKeyboardEffectAsync(effect);

    // ===== Talking to the laptop =====

    // Runs one change, then shows what the laptop really did. A change asked
    // for meanwhile waits its turn (only the latest); a drag step never pushes
    // out one that reads back.
    private async Task ApplyAsync(Func<Task> change, string failureMessage, bool readBack = true)
    {
        if (_busy)
        {
            if (readBack || _queued is not { ReadBack: true })
                _queued = (change, failureMessage, readBack);

            return;
        }

        _busy = true;
        Exception? failure = null;

        try
        {
            await change();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (readBack || failure is not null)
            Show(await ReadStateOrUnknownAsync(L.T("Could not read the lighting state after a change.")));

        if (failure is not null)
        {
            AppLog.Error(failureMessage, failure);
            _report(failureMessage, true);
        }

        EndBusy();
    }

    private void EndBusy()
    {
        _busy = false;

        if (_queued is { } next)
        {
            _queued = null;
            _ = ApplyAsync(next.Change, next.FailureMessage, next.ReadBack);
        }
    }

    private async Task ReadKeyboardBrightnessAsync()
    {
        // Not while a change is under way or the slider was just moved.
        if (_busy || DateTime.UtcNow - _lastBrightnessChange < TimeSpan.FromSeconds(2))
            return;

        try
        {
            var percent = await _services.Lighting.ReadKeyboardBrightnessAsync();

            if (!_busy && percent != _keyboardBrightness)
            {
                _showing = true;
                KeyboardBrightness = percent;
                _showing = false;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the keyboard brightness.", exception);
            _brightnessWatch.Stop(); // Asked again when the page opens next.
        }
    }

    // A failed read is logged and shown as "unknown", never as an old value.
    private async Task<LightingState> ReadStateOrUnknownAsync(string logMessage)
    {
        try
        {
            return await _services.Lighting.ReadStateAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(logMessage, exception);
            return LightingState.Unknown;
        }
    }

    private LightingState _state = LightingState.Unknown;

    private void Show(LightingState state)
    {
        _state = state;

        // The next Static (or a color picked) starts from the color really lit.
        if (state.KeyboardColor is { } color)
            _color = color;

        _keyboardEffect = state.Keyboard;

        _showing = true;
        KeyboardBrightness = state.KeyboardBrightness ?? _keyboardBrightness;
        _showing = false;

        ShowSelections();
    }

    private void ShowPending(LightingState state)
    {
        _state = state;
        ShowSelections();
    }

    private void ShowSelections()
    {
        foreach (var choice in KeyboardEffects)
            choice.Show(choice.Value == _state.Keyboard);

        foreach (var choice in LogoModes)
            choice.Show(choice.Value == _state.Logo);

        foreach (var choice in Colors)
            choice.Show(_state.KeyboardColor == choice.Value);

        var shown = KeyboardEffects.FirstOrDefault(choice => choice.Value == _state.Keyboard);
        EffectName = shown?.Name ?? string.Empty;
        EffectDescription = shown?.Description ?? string.Empty;
    }

    // ===== Lights off =====

    private void SaveLightsOff()
    {
        _services.UpdateSettings(settings => settings with
        {
            KeyboardOffWithScreen = _withScreen,
            LightsOffWhenIdle = _whenIdle,
            LightsOffIdleMinutes = IdleMinuteChoices[_idleMinutes],
            LightsOffOnLowBattery = _onLowBattery,
            LightsOffBatteryPercent = BatteryPercentChoices[_batteryPercent]
        });

        ApplyLightsOff();
    }

    // Which reasons turn the keyboard light off, and the check for idle and
    // low battery while either is on.
    private void ApplyLightsOff()
    {
        var lightsOff = _services.KeyboardLightsOff;
        lightsOff.SetEnabled(LightsOffReason.Screen, _withScreen);
        lightsOff.SetEnabled(LightsOffReason.Idle, _whenIdle);
        lightsOff.SetEnabled(LightsOffReason.LowBattery, _onLowBattery);

        if (_whenIdle || _onLowBattery)
        {
            CheckLightsOff();
            _lightsOffTimer.Start();
        }
        else
        {
            _lightsOffTimer.Stop();
        }
    }

    // Idle: no key or mouse for the chosen minutes. Low battery: unplugged,
    // below the chosen charge. A key press brings the light back within a second.
    private void CheckLightsOff()
    {
        var lightsOff = _services.KeyboardLightsOff;
        lightsOff.SetActive(LightsOffReason.Idle,
            _whenIdle && UserIdle.Time() >= TimeSpan.FromMinutes(IdleMinuteChoices[_idleMinutes]));

        var low = _services.PowerSource.IsPluggedIn == false
            && _services.PowerSource.BatteryPercent is int percent
            && percent < BatteryPercentChoices[_batteryPercent];

        lightsOff.SetActive(LightsOffReason.LowBattery, _onLowBattery && low);
    }

    // ===== Texts =====

    private string NameOf(KeyboardEffect effect) =>
        L.T(effect == KeyboardEffect.StaticGreen ? (_offersColor ? "Static" : "Static green") : effect.ToString());

    private string DescriptionOf(KeyboardEffect effect) => effect switch
    {
        KeyboardEffect.StaticGreen => _offersColor ? L.T("A steady light in the color you choose.") : L.T("A steady light in Razer green."),
        KeyboardEffect.Breathing => L.T("The light fades in and out slowly."),
        KeyboardEffect.Spectrum => L.T("Cycles smoothly through every color."),
        KeyboardEffect.Wave => L.T("Animates all colors in a wave across the keyboard."),
        _ => L.T("The keyboard backlight is off.")
    };

    private static EffectIcon IconOf(KeyboardEffect effect) => effect switch
    {
        KeyboardEffect.Breathing => EffectIcon.Breathing,
        KeyboardEffect.Spectrum => EffectIcon.Spectrum,
        KeyboardEffect.Wave => EffectIcon.Wave,
        KeyboardEffect.Off => EffectIcon.Off,
        _ => EffectIcon.Static
    };

    // The offered value closest to a saved one (a settings file edited by hand may hold any).
    private static int Nearest(IReadOnlyList<int> offered, int value) =>
        offered.Select((each, index) => (Distance: Math.Abs(each - value), Index: index)).MinBy(pair => pair.Distance).Index;

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
