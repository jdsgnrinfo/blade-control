using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BladeControl.ViewModels;

/// <summary>
/// The panel at the right of every page: the laptop's picture and name, its
/// temperatures (with the last hour's average), its fans and its battery.
/// Read once a second while the window is open.
/// </summary>
internal sealed partial class DevicePanelViewModel : ObservableObject
{
    private static readonly TimeSpan AverageWindow = TimeSpan.FromHours(1);

    private readonly AppServices _services;
    // Every second plugged in, every five on battery.
    private readonly PacedTimer _timer;
    private readonly Queue<(DateTime At, double Cpu)> _cpuHistory = new();
    private readonly Queue<(DateTime At, double Gpu)> _gpuHistory = new();
    private bool _reading;

    public DevicePanelViewModel(AppServices services)
    {
        _services = services;
        _timer = new PacedTimer(services.PowerSource, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        var (title, subtitle) = SplitName(services.Model?.Name);
        ModelTitle = title;
        ModelSubtitle = subtitle ?? (services.Model is null ? L.T("Not detected") : string.Empty);
        Picture = LoadPicture(services.Model?.Name);
        HasFanSpeeds = services.Model?.HasFanSpeeds == true;

        _timer.Tick += async (_, _) => await ReadAsync();
    }

    public string ModelTitle { get; }

    public string ModelSubtitle { get; }

    public ImageSource? Picture { get; }

    public bool HasPicture => Picture is not null;

    /// <summary>Windows' power plan, under the battery.</summary>
    public PowerPlanViewModel Plan { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuText), nameof(CpuFraction), nameof(CpuAverageText), nameof(CpuArcStart), nameof(CpuArcEnd))]
    private double? _cpuCelsius;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuText), nameof(GpuFraction), nameof(GpuAverageText), nameof(GpuArcStart), nameof(GpuArcEnd))]
    private double? _gpuCelsius;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuText), nameof(GpuText), nameof(CpuAverageText), nameof(GpuAverageText), nameof(UseCelsius))]
    private bool _useFahrenheit;

    public bool UseCelsius
    {
        get => !UseFahrenheit;
        set => UseFahrenheit = !value;
    }

    /// <summary>The temperatures section, off unless turned on in Settings, Experimental. Off, nothing is read for it.</summary>
    public bool ShowTemperatures
    {
        get => _services.Settings.ShowTemperatures;
        set
        {
            if (value == ShowTemperatures)
                return;

            _services.UpdateSettings(settings => settings with { ShowTemperatures = value });
            AppLog.Info($"The device panel's temperatures turned {(value ? "on" : "off")}.");
            OnPropertyChanged();
        }
    }

    public string CpuText => Degrees(CpuCelsius);

    // An undetected dGPU (asleep, turned off or unread) shows 0° in either unit, not "--".
    public string GpuText => GpuCelsius is null ? "0°" : Degrees(GpuCelsius);

    /// <summary>How far round the ring goes: 0 at 20 °C, full at 100 °C.</summary>
    public double CpuFraction => Fraction(CpuCelsius);

    public double GpuFraction => Fraction(GpuCelsius);

    /// <summary>
    /// The ring's colors by how hot it is: blue under 60 °C; above, its tip turns
    /// towards red, red by 85 °C, and from 72 °C its start follows, so a hot
    /// ring is a gradient from blue to red and a very hot one all red.
    /// </summary>
    public Brush CpuArcStart => HeatBrush(CpuCelsius, tip: false);

    public Brush CpuArcEnd => HeatBrush(CpuCelsius, tip: true);

    public Brush GpuArcStart => HeatBrush(GpuCelsius, tip: false);

    public Brush GpuArcEnd => HeatBrush(GpuCelsius, tip: true);

    public string CpuAverageText => Degrees(Average(_cpuHistory.Select(sample => sample.Cpu)));

    public string GpuAverageText => Average(_gpuHistory.Select(sample => sample.Gpu)) is { } average ? Degrees(average) : "0°";

    [ObservableProperty]
    private bool _hasFanSpeeds;

    [ObservableProperty]
    private string _cpuFanText = "--";

    [ObservableProperty]
    private string _gpuFanText = "--";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryText), nameof(BatteryFraction), nameof(HasBattery))]
    private int? _batteryPercent;

    [ObservableProperty]
    private bool _isPluggedIn;

    public bool HasBattery => BatteryPercent is not null;

    public string BatteryText => BatteryPercent is int percent ? $"{percent}%" : "--";

    public double BatteryFraction => (BatteryPercent ?? 0) / 100.0;

    /// <summary>The BIOS version as Windows recorded it at startup ("01.04"), with its date on hover; null when Windows does not say.</summary>
    public string? BiosText { get; } = BiosInfo.Version();

    public string? BiosDate { get; } = BiosInfo.Date();

    public bool HasBios => BiosText is not null;

    /// <summary>The controller's firmware version ("1.1"), read once; null until the laptop says.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFirmware))]
    private string? _firmwareText;

    public bool HasFirmware => FirmwareText is not null;

    /// <summary>The charger in ("USB-C · 65 W", "Main"); null on battery, so the row is hidden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharger))]
    private string? _chargerText;

    public bool HasCharger => ChargerText is not null;

    // The charger is asked as it is plugged in, then every few seconds: it
    // cannot change without Windows seeing the plug go out and back in.
    private static readonly TimeSpan ChargerEvery = TimeSpan.FromSeconds(5);
    private DateTime _chargerReadAt;
    private bool _chargerWasPluggedIn;

    public void Start()
    {
        _ = ReadAsync();
        _timer.Start();

        // The plan may have been changed in Windows while the window was away.
        Plan.Refresh();
    }

    /// <summary>Stops reading while the window is hidden: nothing would show it.</summary>
    public void Stop() => _timer.Stop();

    private async Task ReadAsync()
    {
        if (_reading)
            return;

        _reading = true;

        try
        {
            var knowFirmware = HasFirmware;
            var pluggedIn = _services.PowerSource.IsPluggedIn == true;
            var askCharger = pluggedIn && (!_chargerWasPluggedIn || DateTime.UtcNow - _chargerReadAt >= ChargerEvery);
            _chargerWasPluggedIn = pluggedIn;

            if (askCharger)
                _chargerReadAt = DateTime.UtcNow;

            // The temperatures are only read while their section shows (Settings, Experimental).
            var readTemperatures = ShowTemperatures;

            var (cpu, gpu, fans, firmware, charger) = await Task.Run(async () =>
            {
                var ec = readTemperatures ? Safe("EC temperatures", _services.EcTemperatures.Read) : null;
                var gpuTemperature = readTemperatures ? Safe("GPU temperature", _services.GpuTemperature.ReadCelsius) ?? ec?.GpuCelsius : null;
                var fanReading = HasFanSpeeds ? await _services.FanTelemetry.ReadAsync() : null;
                var firmwareVersion = knowFirmware ? null : Safe("firmware version", _services.FirmwareVersion.Read);
                var chargerIn = askCharger ? Safe("charger", _services.Charger.Read) : null;
                return ((double?)ec?.CpuCelsius, gpuTemperature, fanReading, firmwareVersion, chargerIn);
            });

            if (firmware is not null)
                FirmwareText = firmware;

            // On battery the row goes. Plugged in, a reading of "battery" is the
            // moment between the two, so the charger last seen stays.
            if (!pluggedIn)
                ChargerText = null;
            else if (charger is not null)
                ChargerText = charger;

            if (readTemperatures)
            {
                var now = DateTime.UtcNow;
                Record(_cpuHistory, now, cpu);
                Record(_gpuHistory, now, gpu);
                CpuCelsius = cpu;
                GpuCelsius = gpu;
            }

            if (HasFanSpeeds && !_services.FanTelemetry.IsSupported)
                HasFanSpeeds = false;

            CpuFanText = fans is null ? "--" : $"{fans.CpuFanRpm} RPM";
            GpuFanText = fans is null ? "--" : $"{fans.GpuFanRpm} RPM";

            BatteryPercent = _services.PowerSource.BatteryPercent;
            IsPluggedIn = _services.PowerSource.IsPluggedIn != false;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the laptop's sensors.", exception);
        }
        finally
        {
            _reading = false;
        }
    }

    private static TResult? Safe<TResult>(string what, Func<TResult> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not read the {what}.", exception);
            return default;
        }
    }

    private static void Record(Queue<(DateTime, double)> history, DateTime now, double? value)
    {
        if (value is double reading)
            history.Enqueue((now, reading));

        while (history.Count > 0 && now - history.Peek().Item1 > AverageWindow)
            history.Dequeue();
    }

    private static double? Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : list.Average();
    }

    private string Degrees(double? celsius) => celsius is double value
        ? $"{Math.Round(UseFahrenheit ? value * 9 / 5 + 32 : value)}°"
        : "--";

    private const double CoolBelow = 60;
    private const double RedFrom = 85;

    private static readonly Color Cool = Color.FromRgb(0x4A, 0x8F, 0xE0);
    private static readonly Color Hot = Color.FromRgb(0xE0, 0x57, 0x4A);

    private static Brush HeatBrush(double? celsius, bool tip)
    {
        var heat = celsius is double value ? Math.Clamp((value - CoolBelow) / (RedFrom - CoolBelow), 0, 1) : 0;
        var amount = tip ? heat : Math.Max(0, 2 * heat - 1);
        var brush = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(Cool.R + (Hot.R - Cool.R) * amount),
            (byte)Math.Round(Cool.G + (Hot.G - Cool.G) * amount),
            (byte)Math.Round(Cool.B + (Hot.B - Cool.B) * amount)));
        brush.Freeze();
        return brush;
    }

    private static double Fraction(double? celsius) =>
        celsius is double value ? Math.Clamp((value - 20) / 80, 0, 1) : 0;

    /// <summary>"Razer Blade 15 Base (2020)" becomes "Blade 15 Base" over "2020", as the concept shows a model and its year.</summary>
    internal static (string Title, string? Subtitle) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ("Razer Blade", null);

        var subtitle = Regex.Match(name, @"\(([^)]*)\)") is { Success: true } match ? match.Groups[1].Value : null;
        var title = Regex.Replace(name, @"\s*\([^)]*\)", string.Empty);
        title = Regex.Replace(title, @"^Razer\s+", string.Empty).Trim();

        return (title, subtitle);
    }

    // The closest picture in Assets\Laptops; none means the panel shows no picture.
    private static ImageSource? LoadPicture(string? model)
    {
        foreach (var name in LaptopPictureNames.CandidatesFor(model))
        {
            var uri = new Uri($"pack://application:,,,/Assets/Laptops/{name}.png");

            try
            {
                if (Application.GetResourceStream(uri) is null)
                    continue;

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (System.IO.IOException)
            {
                // Not there: try the next, less exact name.
            }
        }

        return null;
    }
}
