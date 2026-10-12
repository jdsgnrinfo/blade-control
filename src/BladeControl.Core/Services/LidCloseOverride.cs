using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>
/// Keeps the laptop working with its lid closed while a monitor is connected:
/// the active power plan's lid action, plugged in only, becomes "do nothing".
/// On battery the lid still puts it to sleep, so it never runs on in a bag.
/// What it replaced is kept (and saved with the settings, see
/// <see cref="SavedPlanValue"/>) and given back when the monitor goes, the
/// option is turned off, the app exits or the settings are reset.
/// </summary>
internal sealed class LidCloseOverride(
    IPowerPlanValue lidAction,
    SavedPlanValue? saved,
    Action<SavedPlanValue?> persist)
{
    /// <summary>The lid action that leaves the laptop running.</summary>
    public const uint DoNothing = 0;

    private readonly Lock _sync = new();
    private SavedPlanValue? _saved = saved;
    private bool _wanted;

    /// <summary>Whether the lid action is changed now.</summary>
    public bool IsActive
    {
        get
        {
            lock (_sync)
                return _saved is not null;
        }
    }

    /// <summary>
    /// The option and whether a monitor is connected: with both, the lid action is
    /// changed; otherwise what it replaced is given back.
    /// </summary>
    public void Update(bool enabled, bool externalDisplay)
    {
        lock (_sync)
        {
            _wanted = enabled && externalDisplay;

            if (_wanted)
                ApplyLocked();
            else
                RestoreLocked();
        }
    }

    /// <summary>Makes sure the lid action is still changed, for a check every few seconds: another plan may have become active.</summary>
    public void Recheck()
    {
        lock (_sync)
        {
            if (_wanted)
                ApplyLocked();
        }
    }

    /// <summary>Gives the old lid action back now, as the app exits or the settings are reset.</summary>
    public void Restore()
    {
        lock (_sync)
        {
            _wanted = false;
            RestoreLocked();
        }
    }

    private void ApplyLocked()
    {
        try
        {
            var current = lidAction.Read();

            if (_saved is { } saved)
            {
                // Still ours: nothing to do.
                if (current.Scheme == saved.Scheme && current.PluggedIn == DoNothing)
                    return;

                if (current.Scheme == saved.Scheme)
                {
                    lidAction.Write(current.Scheme, DoNothing, current.OnBattery);
                    AppLog.Info("Lid closed: the lid action was changed back by something else; set to do nothing again.");
                    return;
                }

                // Another plan is active now: the one changed before gets its own back.
                lidAction.Write(saved.Scheme, saved.PluggedIn, saved.OnBattery);
            }

            // The plan already does nothing on lid close: there is nothing to change or give back.
            if (current.PluggedIn == DoNothing)
            {
                _saved = null;
                persist(null);
                return;
            }

            _saved = current;
            persist(current);
            lidAction.Write(current.Scheme, DoNothing, current.OnBattery);
            AppLog.Info($"Lid closed: plugged in, closing the lid now does nothing (it was {current.PluggedIn}).");
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not change what closing the lid does.", exception);
        }
    }

    private void RestoreLocked()
    {
        if (_saved is not { } saved)
            return;

        try
        {
            lidAction.Write(saved.Scheme, saved.PluggedIn, saved.OnBattery);
            AppLog.Info($"Lid closed: the lid action is back to {saved.PluggedIn} plugged in.");
        }
        catch (Exception exception)
        {
            // Kept, so the next chance tries again.
            AppLog.Error("Could not give back what closing the lid does.", exception);
            return;
        }

        _saved = null;
        persist(null);
    }
}
