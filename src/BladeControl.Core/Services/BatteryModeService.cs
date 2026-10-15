using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;

namespace BladeControl.Core.Services;

/// <summary>
/// Battery mode's changes to Windows and the keyboard: while it is on and the
/// laptop runs on battery, the screen at 25% and the keyboard at 20% (never
/// raised), Energy saver on, and transparency, window and pointer shadows and
/// window contents while dragging off. Plugging in, turning it off or closing
/// the app puts back what it changed. What it replaced is saved with the
/// settings as it goes, so a crash is undone at the next start; a level the
/// user changed by hand meanwhile stays as they chose.
/// Silent and the offer to close the GPU's apps are the pages' (see
/// PerformanceViewModel and OptimizeViewModel).
/// </summary>
internal sealed class BatteryModeService : IDisposable
{
    public const int ScreenPercent = 25;
    public const int KeyboardPercent = 20;
    private const uint EnergySaverAlways = 100;

    private readonly IPowerSource _power;
    private readonly IVisualEffects _effects;
    private readonly IScreenBrightness _screen;
    private readonly IPowerPlanValue _energySaver;
    private readonly Func<Task<int?>> _readKeyboard;
    private readonly Func<int, Task> _setKeyboard;
    private readonly Action<BatteryModeSaved?> _persist;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private BatteryModeSaved? _saved;
    private bool _enabled;

    // Nothing happens before the app starts it: a screenshot run never touches anything.
    private volatile bool _started;

    public BatteryModeService(
        IPowerSource power,
        IVisualEffects effects,
        IScreenBrightness screen,
        IPowerPlanValue energySaver,
        Func<Task<int?>> readKeyboard,
        Func<int, Task> setKeyboard,
        bool enabled,
        BatteryModeSaved? saved,
        Action<BatteryModeSaved?> persist)
    {
        _power = power;
        _effects = effects;
        _screen = screen;
        _energySaver = energySaver;
        _readKeyboard = readKeyboard;
        _setKeyboard = setKeyboard;
        _enabled = enabled;
        _saved = saved;
        _persist = persist;
        _power.PowerSourceChanged += OnPowerSourceChanged;
    }

    /// <summary>Whether battery mode's changes are in place now.</summary>
    public bool IsApplied => _saved is not null;

    /// <summary>At startup: puts on, or puts back what a previous run left.</summary>
    public Task StartAsync()
    {
        _started = true;
        return UpdateAsync("Blade Control started");
    }

    public Task SetEnabledAsync(bool enabled)
    {
        _enabled = enabled;
        return UpdateAsync(enabled ? "battery mode was turned on" : "battery mode was turned off");
    }

    /// <summary>Puts back what battery mode changed, whatever the power source: as the app exits, or before a reset.</summary>
    public async Task RestoreAsync(string why)
    {
        if (!_started)
            return;

        await _turn.WaitAsync().ConfigureAwait(false);

        try
        {
            await RestoreLockedAsync(why).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    public void Dispose() => _power.PowerSourceChanged -= OnPowerSourceChanged;

    // Windows also reports battery percentage changes: the update does nothing when nothing changed.
    private void OnPowerSourceChanged(object? sender, EventArgs e) =>
        _ = UpdateAsync("the power source changed");

    private async Task UpdateAsync(string why)
    {
        if (!_started)
            return;

        await _turn.WaitAsync().ConfigureAwait(false);

        try
        {
            var wanted = _enabled && !PowerProfileRules.TreatAsPluggedIn(_power.IsPluggedIn);

            if (wanted && _saved is null)
                await ApplyLockedAsync(why).ConfigureAwait(false);
            else if (!wanted && _saved is not null)
                await RestoreLockedAsync(why).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Battery mode could not be updated.", exception);
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task ApplyLockedAsync(string why)
    {
        var saved = new BatteryModeSaved();

        // Saved first, then changed: a crash half way still knows what to put back.
        void Keep(BatteryModeSaved next)
        {
            saved = next;
            _saved = next;
            _persist(next);
        }

        Keep(saved);

        Try("Energy saver", () =>
        {
            var plan = _energySaver.Read();

            if (plan.OnBattery == EnergySaverAlways)
                return;

            Keep(saved with { EnergySaver = plan });
            _energySaver.Write(plan.Scheme, plan.PluggedIn, EnergySaverAlways);
        });

        Try("transparency", () => TurnOff(() => _effects.Transparency, value => _effects.Transparency = value, before => Keep(saved with { Transparency = before })));
        Try("window shadows", () => TurnOff(() => _effects.WindowShadows, value => _effects.WindowShadows = value, before => Keep(saved with { WindowShadows = before })));
        Try("pointer shadow", () => TurnOff(() => _effects.PointerShadow, value => _effects.PointerShadow = value, before => Keep(saved with { PointerShadow = before })));
        Try("window contents while dragging", () => TurnOff(() => _effects.DragWindowContents, value => _effects.DragWindowContents = value, before => Keep(saved with { DragWindowContents = before })));

        Try("screen brightness", () =>
        {
            if (_screen.Read() is not int now || now <= ScreenPercent)
                return;

            Keep(saved with { ScreenBrightness = now, ScreenBrightnessSet = ScreenPercent });
            _screen.Set(ScreenPercent);
        });

        try
        {
            if (await _readKeyboard().ConfigureAwait(false) is int now && now > KeyboardPercent)
            {
                Keep(saved with { KeyboardBrightness = now, KeyboardBrightnessSet = KeyboardPercent });
                await _setKeyboard(KeyboardPercent).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Battery mode could not dim the keyboard.", exception);
        }

        AppLog.Info($"Battery mode on ({why}): {Describe(saved)}.");
    }

    private async Task RestoreLockedAsync(string why)
    {
        if (_saved is not { } saved)
            return;

        // Energy saver first: it dims the screen a little while on, which the screen check below must not see.
        if (saved.EnergySaver is { } plan)
            Try("Energy saver", () => _energySaver.Write(plan.Scheme, plan.PluggedIn, plan.OnBattery));

        if (saved.Transparency is { } transparency)
            Try("transparency", () => _effects.Transparency = transparency);

        if (saved.WindowShadows is { } shadows)
            Try("window shadows", () => _effects.WindowShadows = shadows);

        if (saved.PointerShadow is { } pointer)
            Try("pointer shadow", () => _effects.PointerShadow = pointer);

        if (saved.DragWindowContents is { } drag)
            Try("window contents while dragging", () => _effects.DragWindowContents = drag);

        // A level changed by hand since is the user's: only the one battery mode set goes back.
        if (saved is { ScreenBrightness: int screen, ScreenBrightnessSet: int screenSet })
        {
            Try("screen brightness", () =>
            {
                if (_screen.Read() is int now && Math.Abs(now - screenSet) <= 2)
                    _screen.Set(screen);
            });
        }

        if (saved is { KeyboardBrightness: int keyboard, KeyboardBrightnessSet: int keyboardSet })
        {
            try
            {
                if (await _readKeyboard().ConfigureAwait(false) is int now && Math.Abs(now - keyboardSet) <= 1)
                    await _setKeyboard(keyboard).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Error("Battery mode could not put the keyboard's brightness back.", exception);
            }
        }

        _saved = null;
        _persist(null);
        AppLog.Info($"Battery mode off ({why}): {Describe(saved)} put back.");
    }

    // Turns an effect off, keeping that it was on.
    private static void TurnOff(Func<bool> read, Action<bool> write, Action<bool> keep)
    {
        if (!read())
            return;

        keep(true);
        write(false);
    }

    private static void Try(string what, Action change)
    {
        try
        {
            change();
        }
        catch (Exception exception)
        {
            AppLog.Error($"Battery mode could not change the {what}.", exception);
        }
    }

    private static string Describe(BatteryModeSaved saved)
    {
        var parts = new List<string>();

        if (saved.EnergySaver is not null) parts.Add("Energy saver");
        if (saved.Transparency is not null) parts.Add("transparency");
        if (saved.WindowShadows is not null) parts.Add("window shadows");
        if (saved.PointerShadow is not null) parts.Add("pointer shadow");
        if (saved.DragWindowContents is not null) parts.Add("dragging");
        if (saved.ScreenBrightness is { } screen) parts.Add($"screen {screen}%");
        if (saved.KeyboardBrightness is { } keyboard) parts.Add($"keyboard {keyboard}%");

        return parts.Count == 0 ? "nothing needed changing" : string.Join(", ", parts);
    }
}
