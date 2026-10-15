using System.Globalization;
using System.Windows.Threading;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;
using BladeControl.Core.Services;
using BladeControl.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BladeControl.ViewModels;

/// <summary>One ring of the system monitor: how busy a part is, and two figures about it.</summary>
internal sealed partial class UsageGauge(string name, string firstLabel, string secondLabel) : ObservableObject
{
    public string Name { get; } = name;

    public string FirstLabel { get; } = firstLabel;

    public string SecondLabel { get; } = secondLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fraction), nameof(PercentText))]
    private double? _percent;

    /// <summary>How far round the ring goes, 0 to 1.</summary>
    public double Fraction => Math.Clamp((Percent ?? 0) / 100, 0, 1);

    public string PercentText => Percent is { } value ? Math.Round(value).ToString(CultureInfo.CurrentCulture) : HardwareStatsText.NoReading;

    [ObservableProperty]
    private string _first = HardwareStatsText.NoReading;

    [ObservableProperty]
    private string _second = HardwareStatsText.NoReading;
}

/// <summary>
/// The system monitor under Battery health, as in Razer Helper: how busy the
/// CPU, the GPU and the RAM are, with the CPU's and GPU's temperature and
/// clock, and the RAM's speed and what is free. Read once a second, only while
/// the Performance page is open, and not at all once turned off in Settings.
/// </summary>
internal sealed class SystemMonitorViewModel : ObservableObject, IDisposable
{
    private bool _pageOpen;
    private readonly AppServices _services;
    private readonly CpuStatsReader _cpuReader = new();
    private readonly GpuStatsReader _gpuReader = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Task _read = Task.CompletedTask;
    private int? _memoryMhz;
    private bool _memoryMhzRead;

    public SystemMonitorViewModel(AppServices services)
    {
        _services = services;
        Gauges =
        [
            new UsageGauge("CPU", L.T("Temperature"), L.T("Frequency")),
            new UsageGauge("GPU", L.T("Temperature"), L.T("Frequency")),
            new UsageGauge("RAM", L.T("Speed"), L.T("Available"))
        ];

        _timer.Tick += (_, _) => StartRead();
    }

    public IReadOnlyList<UsageGauge> Gauges { get; }

    /// <summary>
    /// The monitor is on (Settings, General). Off, its card is hidden and
    /// nothing is read for it, NVIDIA's library included.
    /// </summary>
    public bool IsEnabled
    {
        get => _services.Settings.SystemMonitor;
        set
        {
            if (value == IsEnabled)
                return;

            _services.UpdateSettings(settings => settings with { SystemMonitor = value });
            OnPropertyChanged();

            if (value && _pageOpen)
                Begin();
            else if (!value)
                Halt();
        }
    }

    /// <summary>The Performance page opened: reads now and once a second, if the monitor is on.</summary>
    public void Start()
    {
        _pageOpen = true;

        if (IsEnabled)
            Begin();
    }

    /// <summary>The page closed or the window went to the tray.</summary>
    public void Stop()
    {
        _pageOpen = false;
        Halt();
    }

    // The CPU's first read only primes its counters.
    private void Begin()
    {
        StartRead();
        _timer.Start();
    }

    // Lets go of NVIDIA's library too once the last read is done, so nothing
    // here can keep the GPU awake while the monitor is not showing.
    private void Halt()
    {
        _timer.Stop();
        _read.ContinueWith(_ => _gpuReader.Dispose(), TaskScheduler.Default);
    }

    public void Dispose()
    {
        _timer.Stop();
        _read.ContinueWith(_ =>
        {
            _cpuReader.Dispose();
            _gpuReader.Dispose();
        }, TaskScheduler.Default);
    }

    private async void StartRead()
    {
        if (!_read.IsCompleted)
            return;

        var read = Task.Run(() =>
        {
            if (!_memoryMhzRead)
            {
                _memoryMhz = SystemInfoReader.ReadMemoryModules().Mhz;
                _memoryMhzRead = true;
            }

            // The CPU's temperature comes from the laptop's controller, the same reading the panel shows.
            double? cpuCelsius = null;

            try
            {
                cpuCelsius = _services.EcTemperatures.Read().CpuCelsius;
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not read the CPU temperature.", exception);
            }

            return (Cpu: _cpuReader.Read(), CpuCelsius: cpuCelsius, Gpu: _gpuReader.Read(), Memory: SystemInfoReader.ReadMemory());
        });

        _read = read;

        try
        {
            var (cpu, cpuCelsius, gpu, memory) = await read;
            Show(cpu, cpuCelsius, gpu, memory);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the system monitor.", exception);
        }
    }

    private void Show(CpuStats cpu, double? cpuCelsius, GpuStats gpu, MemoryUsage? memory)
    {
        var culture = CultureInfo.CurrentCulture;
        var (cpuGauge, gpuGauge, ramGauge) = (Gauges[0], Gauges[1], Gauges[2]);

        cpuGauge.Percent = cpu.UsagePercent ?? cpuGauge.Percent;
        cpuGauge.First = HardwareStatsText.Celsius(cpuCelsius);
        cpuGauge.Second = HardwareStatsText.Gigahertz(cpu.CurrentMhz, culture);

        if (gpu.Asleep)
        {
            gpuGauge.Percent = 0;
            gpuGauge.First = HardwareStatsText.NoReading;
            gpuGauge.Second = L.T("Asleep");
        }
        else
        {
            gpuGauge.Percent = gpu.UsagePercent ?? gpuGauge.Percent;
            gpuGauge.First = HardwareStatsText.Celsius(gpu.Celsius);
            gpuGauge.Second = HardwareStatsText.Megahertz(gpu.CoreMhz);
        }

        if (memory is { TotalBytes: > 0 } ram)
        {
            ramGauge.Percent = 100.0 * (ram.TotalBytes - ram.AvailableBytes) / ram.TotalBytes;
            ramGauge.First = HardwareStatsText.Megahertz(_memoryMhz);
            ramGauge.Second = SystemInfoText.Size(ram.AvailableBytes, culture);
        }
    }
}
