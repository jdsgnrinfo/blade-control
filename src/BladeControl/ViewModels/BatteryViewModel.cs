using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BladeControl.ViewModels;

/// <summary>A figure in the battery's status: what it is, its value, and a short note under it.</summary>
internal sealed record BatteryFigure(string Name, string Value, string? Caption);

/// <summary>
/// The Battery page: the charge now and where it is going (power in or out,
/// time left or to full), the last hour's charge as a line, and the battery's
/// health (what a full charge holds against when it was new) with its
/// details. All of it from Windows' battery driver, which only reads. The
/// details are read every two seconds while the page is open; the charge is
/// noted every half minute all the time, so the line has a past to show.
/// </summary>
internal sealed partial class BatteryViewModel : ObservableObject
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(1);

    private readonly AppServices _services;
    private readonly DispatcherTimer _readTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Queue<(DateTime At, int Percent)> _history = new();
    private bool _reading;

    public BatteryViewModel(AppServices services)
    {
        _services = services;

        _readTimer.Tick += async (_, _) => await ReadAsync();
        _historyTimer.Tick += (_, _) => NoteCharge();

        // A charger plugged in or out shows at once, not up to two seconds later.
        services.PowerSource.PowerSourceChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(async () =>
            {
                NoteCharge();

                if (_readTimer.IsEnabled)
                    await ReadAsync();
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

    // ===== Last hour =====

    [ObservableProperty]
    private IReadOnlyList<Point> _historyPoints = [];

    /// <summary>How much the charge moved over the line, "+8%" or "−12%"; empty with too few readings.</summary>
    [ObservableProperty]
    private string _historyChange = string.Empty;

    /// <summary>Over how long, "in 42 min".</summary>
    [ObservableProperty]
    private string _historySpan = string.Empty;

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

    /// <summary>At startup: the line starts with the charge now.</summary>
    public void Start()
    {
        NoteCharge();
        _historyTimer.Start();
    }

    /// <summary>The details are read only while the page shows them.</summary>
    public void SetPageOpen(bool open)
    {
        if (open)
        {
            _ = ReadAsync();
            _readTimer.Start();
        }
        else
        {
            _readTimer.Stop();
        }
    }

    public Task RefreshAsync() => ReadAsync();

    public void Dispose()
    {
        _readTimer.Stop();
        _historyTimer.Stop();
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

    // ===== The last hour's line =====

    private void NoteCharge()
    {
        if (_services.PowerSource.BatteryPercent is not int percent)
            return;

        var now = DateTime.UtcNow;
        _history.Enqueue((now, percent));

        while (_history.Count > 0 && now - _history.Peek().At > HistoryWindow)
            _history.Dequeue();

        HistoryPoints = [.. _history.Select(sample => new Point(
            1 - (now - sample.At).TotalSeconds / HistoryWindow.TotalSeconds,
            sample.Percent / 100.0))];

        var (firstAt, firstPercent) = _history.Peek();
        var minutes = (int)Math.Round((now - firstAt).TotalMinutes);

        if (_history.Count < 2 || minutes < 1)
        {
            HistoryChange = string.Empty;
            HistorySpan = L.T("Gathering readings");
            return;
        }

        var change = percent - firstPercent;
        HistoryChange = change switch
        {
            > 0 => $"+{change}%",
            < 0 => $"−{-change}%",
            _ => "0%"
        };
        HistorySpan = L.F("in {0}", BatteryDetailsText.Duration(minutes * 60));
    }

    private static string WattHours(int milliwattHours) =>
        $"{(milliwattHours / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} Wh";

    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
}
