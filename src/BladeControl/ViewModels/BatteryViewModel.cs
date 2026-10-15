using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BladeControl.ViewModels;

/// <summary>A line of details: what it is, and its value.</summary>
internal sealed record PanelDetail(string Name, string Value);

/// <summary>A figure in the battery's status: what it is, its value, and a short note under it.</summary>
internal sealed record BatteryFigure(string Name, string Value, string? Caption);

/// <summary>
/// The Battery page: the charge now and where it is going (power in or out,
/// time left or to full), the last 24 hours' charge as a line with
/// the charger's stretches, and the battery's health (what a full charge
/// holds against when it was new) with its details. The figures come from
/// Windows' battery driver, read every two seconds while the page is open;
/// the 24 hours from Windows' own battery report, which it keeps whether or
/// not Blade Control runs, read every minute while the page is open. Both
/// only read.
/// </summary>
internal sealed partial class BatteryViewModel : ObservableObject
{
    private readonly AppServices _services;
    // Every two seconds plugged in, every five on battery.
    private readonly PacedTimer _readTimer;
    private readonly DispatcherTimer _dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private BatteryDetails? _battery;
    private bool _reading;
    private bool _readingDay;

    public BatteryViewModel(AppServices services)
    {
        _services = services;
        _readTimer = new PacedTimer(services.PowerSource, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

        _readTimer.Tick += async (_, _) => await ReadAsync();
        _dayTimer.Tick += async (_, _) => await ReadDayAsync();

        // A charger plugged in or out shows at once, not up to a minute later.
        services.PowerSource.PowerSourceChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(async () =>
            {
                if (!_readTimer.IsEnabled)
                    return;

                await ReadAsync();
                await ReadDayAsync();
            });
    }

    // ===== Status =====

    /// <summary>False when Windows reports no battery, or it could not be read.</summary>
    [ObservableProperty]
    private bool _hasBattery = true;

    [ObservableProperty]
    private string _chargeText = "--";

    [ObservableProperty]
    private double _chargeFraction;

    [ObservableProperty]
    private string _stateText = string.Empty;

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private bool _isPluggedIn;

    /// <summary>Power, time, the energy stored and the voltage, those the battery reports.</summary>
    [ObservableProperty]
    private IReadOnlyList<BatteryFigure> _figures = [];

    // ===== Last 24 hours =====

    /// <summary>The charge over the 24 hours: X across, Y the charge, both 0 to 1.</summary>
    [ObservableProperty]
    private IReadOnlyList<Point> _dayPoints = [];

    /// <summary>The time at the chart's left edge.</summary>
    [ObservableProperty]
    private DateTime _dayStart;

    /// <summary>The charger's stretches: X from, Y to, as fractions of the chart's width.</summary>
    [ObservableProperty]
    private IReadOnlyList<Point> _dayCharging = [];

    /// <summary>Where the charger went in, as fractions of the chart's width.</summary>
    [ObservableProperty]
    private IReadOnlyList<double> _dayPlugIns = [];

    /// <summary>The times under the chart, every six hours.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _dayTicks = [];

    /// <summary>Time with the screen on, off and asleep over the 24 hours.</summary>
    [ObservableProperty]
    private IReadOnlyList<PanelDetail> _daySummary = [];

    /// <summary>False when Windows' battery report could not be read.</summary>
    [ObservableProperty]
    private bool _hasDay = true;

    // ===== Health =====

    [ObservableProperty]
    private bool _hasHealth;

    [ObservableProperty]
    private string _healthText = "--";

    [ObservableProperty]
    private double _healthFraction;

    [ObservableProperty]
    private Brush _healthBrush = Brushes.Transparent;

    [ObservableProperty]
    private string _healthVerdict = string.Empty;

    [ObservableProperty]
    private string _healthCaption = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<PanelDetail> _details = [];

    // ===== Life cycle =====

    /// <summary>The battery is read only while the page shows it.</summary>
    public async void SetPageOpen(bool open)
    {
        if (open)
        {
            _readTimer.Start();
            _dayTimer.Start();
            await RefreshAsync();
        }
        else
        {
            _readTimer.Stop();
            _dayTimer.Stop();
        }
    }

    public async Task RefreshAsync()
    {
        await ReadAsync();
        await ReadDayAsync();
    }

    public void Dispose()
    {
        _readTimer.Stop();
        _dayTimer.Stop();
    }

    private async Task ReadAsync()
    {
        if (_reading)
            return;

        _reading = true;

        try
        {
            var battery = await Task.Run(() =>
            {
                try
                {
                    return BatteryReader.Read();
                }
                catch (Exception exception)
                {
                    AppLog.Error("Could not read the battery.", exception);
                    return null;
                }
            });

            Show(battery);
        }
        finally
        {
            _reading = false;
        }
    }

    private void Show(BatteryDetails? battery)
    {
        _battery = battery;
        HasBattery = battery is not null;

        if (battery is null)
        {
            ChargeText = "--";
            ChargeFraction = 0;
            Figures = [];
            HasHealth = false;
            Details = [];
            return;
        }

        var percent = BatteryDetailsText.ChargePercent(battery) ?? _services.PowerSource.BatteryPercent;
        ChargeText = percent is int p ? p.ToString(CultureInfo.InvariantCulture) : "--";
        ChargeFraction = (percent ?? 0) / 100.0;
        IsCharging = battery.Charging;
        IsPluggedIn = battery.PluggedIn;
        StateText = percent >= 100 && battery.PluggedIn ? L.T("Plugged in, fully charged") : BatteryDetailsText.HeaderStatus(battery);
        Figures = FiguresOf(battery);

        var health = BatteryDetailsText.HealthPercent(battery);
        HasHealth = health is not null;
        HealthText = health is int h ? h.ToString(CultureInfo.InvariantCulture) : "--";
        HealthFraction = Math.Min(1.0, (health ?? 0) / 100.0);
        HealthBrush = Resource(health switch
        {
            >= 80 => "Brush.Accent",
            >= 60 => "Brush.Warning",
            _ => "Brush.Warm"
        });
        HealthVerdict = health switch
        {
            >= 80 => L.T("Good condition"),
            >= 60 => L.T("Some wear"),
            _ => L.T("Worn")
        };
        HealthCaption = BatteryDetailsText.HealthCaption(battery) ?? string.Empty;
        Details = DetailsOf(battery);
    }

    private static List<BatteryFigure> FiguresOf(BatteryDetails battery)
    {
        var figures = new List<BatteryFigure>();

        figures.Add(BatteryDetailsText.PowerParts(battery) is var (watts, direction)
            ? new BatteryFigure(L.T("Power"), watts, direction)
            : new BatteryFigure(L.T("Power"), "--", battery.PluggedIn ? L.T("Not charging") : null));

        figures.Add(BatteryDetailsText.TimeParts(battery) is var (time, towards)
            ? new BatteryFigure(L.T("Time"), time, towards)
            : new BatteryFigure(L.T("Time"), "--", battery.PluggedIn ? (BatteryDetailsText.ChargePercent(battery) >= 100 ? L.T("Fully charged") : L.T("Not charging")) : null));

        if (BatteryDetailsText.ChargeParts(battery) is var (stored, outOf))
            figures.Add(new BatteryFigure(L.T("Stored"), stored, outOf));

        if (BatteryDetailsText.Voltage(battery) is { } voltage)
            figures.Add(new BatteryFigure(L.T("Voltage"), voltage, null));

        return figures;
    }

    private static List<PanelDetail> DetailsOf(BatteryDetails battery)
    {
        var details = new List<PanelDetail>();
        var name = string.Join(" ", new[] { battery.Manufacturer, battery.Name }.Where(part => !string.IsNullOrWhiteSpace(part)));

        if (name.Length > 0)
            details.Add(new PanelDetail(L.T("Battery"), name));

        if (BatteryDetailsText.ChemistryName(battery.Chemistry) is { } chemistry)
            details.Add(new PanelDetail(L.T("Chemistry"), chemistry));

        details.Add(new PanelDetail(L.T("Cycles"), battery.CycleCount is int cycles
            ? cycles.ToString(CultureInfo.CurrentCulture)
            : L.T("Not reported")));

        if (battery.DesignMilliwattHours is int design)
            details.Add(new PanelDetail(L.T("When new"), WattHours(design)));

        if (battery.FullChargeMilliwattHours is int full)
            details.Add(new PanelDetail(L.T("Full charge now"), WattHours(full)));

        return details;
    }

    // ===== The last 24 hours =====

    private async Task ReadDayAsync()
    {
        if (_readingDay)
            return;

        _readingDay = true;

        try
        {
            var entries = await Task.Run(() => WindowsBatteryReport.Read() is { } xml ? BatteryUsage.Parse(xml) : null);

            HasDay = entries is not null;

            if (entries is null)
                return;

            // 100% is what a full charge holds now, as the ring above counts it.
            var full = _battery?.FullChargeMilliwattHours
                ?? (entries.Count > 0 ? entries.Max(entry => entry.ChargeMilliwattHours) : 0);

            ShowDay(BatteryUsage.Build(entries, DateTime.Now, _battery?.RemainingMilliwattHours, _battery?.PluggedIn, full));
        }
        finally
        {
            _readingDay = false;
        }
    }

    private void ShowDay(BatteryDay day)
    {
        var culture = CultureInfo.CurrentCulture;

        DayStart = day.Start;
        DayPoints = [.. day.Line.Select(point => new Point(point.X, point.Fraction))];
        DayCharging = [.. day.Charging.Select(span => new Point(span.From, span.To))];
        DayPlugIns = [.. day.Charging.Where(span => span.StartsInside).Select(span => span.From)];
        DayTicks = [.. Enumerable.Range(0, 5).Select(step => day.Start.AddHours(step * 6).ToString("t", culture))];
        DaySummary =
        [
            new PanelDetail(L.T("Screen on"), Span(day.ScreenOn)),
            new PanelDetail(L.T("Screen off"), Span(day.ScreenOff)),
            new PanelDetail(L.T("Asleep"), Span(day.Asleep))
        ];
    }

    // "6 h 7 min", or a dash for none, as Settings shows it.
    private static string Span(TimeSpan time) =>
        time < TimeSpan.FromMinutes(1) ? "-" : BatteryDetailsText.Duration((int)time.TotalSeconds);

    private static string WattHours(int milliwattHours) =>
        $"{(milliwattHours / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} Wh";

    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
}
