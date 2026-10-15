using System.Windows;
using System.Windows.Threading;
using BladeControl.Core.Services;

namespace BladeControl.Services;

/// <summary>
/// A timer that ticks less often on battery. Every sensor read wakes the
/// processor (and some talk to the laptop's controller), so on battery the
/// app reads every few seconds instead of every second; plugged in it stays
/// quick. The pace follows the charger as it is plugged in or out.
/// </summary>
internal sealed class PacedTimer
{
    private readonly DispatcherTimer _timer = new();
    private readonly IPowerSource _power;
    private readonly TimeSpan _pluggedIn;
    private readonly TimeSpan _onBattery;

    public PacedTimer(IPowerSource power, TimeSpan pluggedIn, TimeSpan onBattery)
    {
        _power = power;
        _pluggedIn = pluggedIn;
        _onBattery = onBattery;
        _timer.Interval = Current();

        // Windows also reports every change of charge; the interval only changes with the source.
        power.PowerSourceChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var interval = Current();

            if (_timer.Interval != interval)
                _timer.Interval = interval;
        });
    }

    public event EventHandler Tick
    {
        add => _timer.Tick += value;
        remove => _timer.Tick -= value;
    }

    public bool IsEnabled => _timer.IsEnabled;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    // Unknown counts as plugged in, as everywhere else in the app.
    private TimeSpan Current() => _power.IsPluggedIn == false ? _onBattery : _pluggedIn;
}
