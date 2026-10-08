using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>Why the keyboard's light may be off.</summary>
internal enum LightsOffReason
{
    /// <summary>The screen is off (Windows' display timeout, sleep, a closed lid).</summary>
    Screen,

    /// <summary>No key or mouse for the chosen time.</summary>
    Idle,

    /// <summary>On battery, below the chosen charge.</summary>
    LowBattery
}

/// <summary>
/// Turns the keyboard backlight off while any of its reasons holds and is
/// turned on (the screen off, the laptop idle, the battery low), and brings
/// it back at the brightness it had once none does. The screen comes from the
/// display on and off notices Windows sends; idle and battery are reported by
/// the window. "Off" is brightness 0, so the effect and color are kept.
///
/// The light fades rather than snapping, both ways: a few steps over about
/// half a second, on a worker thread, so the window never waits on it. The
/// screen changing its mind mid-fade turns the fade around from wherever it
/// got to. Every write to the keyboard runs in turn on one queue, so two
/// fades never cross.
/// </summary>
internal sealed class KeyboardScreenOffService
{
    // About 0.45 s each way.
    internal const int FadeSteps = 10;
    private static readonly TimeSpan FadeStepDelay = TimeSpan.FromMilliseconds(50);

    private readonly Func<int> _readBrightness;
    private readonly Action<int> _setBrightness;
    private readonly Action<TimeSpan> _pause;
    private readonly Func<Action, Task> _run;
    private readonly Lock _sync = new();

    // The queue of writes: each runs once the one before has finished.
    private Task _queue = Task.CompletedTask;

    // The reasons turned on, those that hold now, and whether the light is
    // (going) off for them.
    private readonly HashSet<LightsOffReason> _enabled = [LightsOffReason.Screen];
    private readonly HashSet<LightsOffReason> _active = [];
    private bool _dimmed;

    // The fade under way or queued, either way; a new one stops it.
    private CancellationTokenSource? _fade;

    // The brightness to bring back, kept until the fade back up has finished;
    // and what the last write left the keyboard at: anything else when the
    // screen comes on means the Fn keys changed it.
    private int? _restoreTo;
    private int _left;

    /// <param name="pause">Waits between fade steps; Thread.Sleep unless a test passes its own.</param>
    /// <param name="run">Starts queued work; a worker thread unless a test runs it in place.</param>
    public KeyboardScreenOffService(Func<int> readBrightness, Action<int> setBrightness, Action<TimeSpan>? pause = null, Func<Action, Task>? run = null)
    {
        _readBrightness = readBrightness;
        _setBrightness = setBrightness;
        _pause = pause ?? Thread.Sleep;
        _run = run ?? (work => Task.Run(work));
    }

    public KeyboardScreenOffService(LightingService lighting)
        : this(lighting.ReadKeyboardBrightness, lighting.SetKeyboardBrightness)
    {
    }

    /// <summary>Whether the light goes off with the screen. Off leaves the keyboard alone for it, and lights it again if it was dimmed for it.</summary>
    public bool Enabled
    {
        get
        {
            lock (_sync)
                return _enabled.Contains(LightsOffReason.Screen);
        }
    }

    public void SetEnabled(bool enabled) => SetEnabled(LightsOffReason.Screen, enabled);

    /// <summary>Turns a reason on or off; off brings the light back unless another reason still holds.</summary>
    public void SetEnabled(LightsOffReason reason, bool enabled)
    {
        lock (_sync)
        {
            if (enabled)
                _enabled.Add(reason);
            else
                _enabled.Remove(reason);

            Update(enabled ? $"{Describe(reason)} was turned on" : $"{Describe(reason)} was turned off");
        }
    }

    /// <summary>Windows reported the screen on (true) or off (false).</summary>
    public void OnDisplayChanged(bool on) => SetActive(LightsOffReason.Screen, !on);

    /// <summary>Says whether a reason holds now: the screen is off, the laptop idle, the battery low.</summary>
    public void SetActive(LightsOffReason reason, bool active)
    {
        lock (_sync)
        {
            if (active)
                _active.Add(reason);
            else
                _active.Remove(reason);

            Update(active ? Describe(reason) : $"{Describe(reason)} no longer holds");
        }
    }

    // Off while any reason that is turned on holds; back once none does.
    private void Update(string why)
    {
        var dim = _active.Any(_enabled.Contains);

        if (dim == _dimmed)
            return;

        if (!dim)
        {
            QueueRestore(why);
            return;
        }

        _dimmed = true;
        var token = StartFade();
        Queue(() => FadeOut(why, token));
    }

    private static string Describe(LightsOffReason reason) => reason switch
    {
        LightsOffReason.Screen => "the screen is off",
        LightsOffReason.Idle => "the laptop is idle",
        _ => "the battery is low"
    };

    /// <summary>Lights the keyboard again if this service dimmed it, e.g. as the app exits. Waits until done.</summary>
    public void Restore()
    {
        Task done;

        lock (_sync)
        {
            QueueRestore("Blade Control is exiting");
            done = _queue;
        }

        done.Wait();
    }

    private void FadeOut(string why, CancellationToken token)
    {
        try
        {
            var from = _readBrightness();

            lock (_sync)
            {
                // A fade back up cut short already holds the brightness to bring back.
                if (_restoreTo is null)
                {
                    // Already dark (dimmed to 0 by hand): nothing to do or undo.
                    if (from <= 0)
                        return;

                    _restoreTo = from;
                }

                _left = from;
            }

            if (Fade(from, 0, token))
                AppLog.Info($"Keyboard backlight faded out ({why}).");
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not turn the keyboard backlight off ({why}).", exception);
        }
    }

    // Stops a fade under way and queues the fade back up after it.
    private void QueueRestore(string reason)
    {
        _dimmed = false;
        var token = StartFade();
        Queue(() => RestoreNow(reason, token));
    }

    private void RestoreNow(string reason, CancellationToken token)
    {
        int brightness;
        int left;

        lock (_sync)
        {
            if (_restoreTo is not { } saved)
                return;

            brightness = saved;
            left = _left;
        }

        try
        {
            // Changed while the screen was off (the Fn keys): that wins.
            if (_readBrightness() != left)
            {
                lock (_sync)
                    _restoreTo = null;

                return;
            }

            if (!Fade(left, brightness, token))
                return; // Turned around: the fade down that stopped it keeps what to bring back.

            lock (_sync)
                _restoreTo = null;

            AppLog.Info($"Keyboard backlight turned back on ({reason}).");
        }
        catch (Exception exception)
        {
            lock (_sync)
                _restoreTo = null;

            AppLog.Error("Could not turn the keyboard backlight back on.", exception);
        }
    }

    // Steps from one brightness to another, eased: quick at first, softer at
    // the end. False when stopped part way.
    private bool Fade(int from, int to, CancellationToken token)
    {
        for (var step = 1; step <= FadeSteps; step++)
        {
            if (token.IsCancellationRequested)
                return false;

            var remaining = 1 - (double)step / FadeSteps;
            var value = (int)Math.Round(to + (from - to) * remaining * remaining);

            _setBrightness(value);

            lock (_sync)
                _left = value;

            if (step < FadeSteps)
                _pause(FadeStepDelay);
        }

        return true;
    }

    // A fresh token for the next fade, stopping the one before.
    private CancellationToken StartFade()
    {
        _fade?.Cancel();
        _fade = new CancellationTokenSource();
        return _fade.Token;
    }

    private void Queue(Action work)
    {
        var before = _queue;
        _queue = _run(() =>
        {
            before.Wait();
            work();
        });
    }
}
