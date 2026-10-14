using System.Runtime.InteropServices;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;

namespace BladeControl.Core.Hardware;

/// <summary>
/// The dedicated GPU's figures. Asleep means it is powered down (the laptop
/// is running on its integrated graphics), so there is nothing to read. A
/// null figure means there was no reading.
/// </summary>
internal sealed record GpuStats(bool Asleep, double? Celsius, double? UsagePercent, int? CoreMhz, int? MemoryMhz)
{
    public static GpuStats None { get; } = new(false, null, null, null, null);
}

/// <summary>
/// Reads the dedicated GPU's temperature, load and clocks. The temperature
/// and the memory clock come from Windows (as for Task Manager), which never
/// wakes a sleeping GPU. The load and the core clock only NVIDIA's driver
/// library knows, and asking it once a second keeps the GPU awake: something
/// waking it for a moment (a service, an app starting) was enough for the
/// monitor to hold it awake for good, a few watts on battery. So the library
/// is asked only while another app is using the GPU, and let go as soon as
/// none is, so the GPU can fall asleep. Not thread-safe: one read at a time.
/// </summary>
internal sealed class GpuStatsReader : IDisposable
{
    // Who holds video memory on the GPU is looked up every few seconds, not every read.
    private static readonly TimeSpan UsersCheckEvery = TimeSpan.FromSeconds(3);
    private const int SystemProcessId = 4;

    private readonly NvidiaDriverLibrary _nvidia = new();
    private long? _adapterLuid;
    private bool _loggedFailure;
    private DateTime _usersCheckedAt;
    private bool _othersUseIt;

    public GpuStats Read()
    {
        try
        {
            _adapterLuid ??= DiscreteGpuReader.ReadDiscreteAdapters().FirstOrDefault()?.Luid;

            if (_adapterLuid is not { } luid)
                return GpuStats.None;

            if (D3dkmtGpuTemperature.QueryPerfData(luid) is not { } perf)
            {
                // The adapter may have changed (a driver reset); look it up again next time.
                _adapterLuid = null;
                return GpuStats.None;
            }

            var celsius = D3dkmtGpuTemperature.ToCelsius(perf.Temperature);
            int? memoryMhz = perf.MemoryFrequency > 0 ? (int)(perf.MemoryFrequency / 1_000_000) : null;

            // Both read zero while the GPU is powered down.
            // Let go of NVIDIA's library too, so holding it open cannot keep the GPU from sleeping.
            if (celsius is null && memoryMhz is null)
            {
                _nvidia.Dispose();
                return new GpuStats(Asleep: true, null, null, null, null);
            }

            // Awake, but no other app is on it: let NVIDIA's library go, so the
            // GPU can fall asleep, and show it idle.
            if (!OthersUseIt(luid))
            {
                _nvidia.Dispose();
                return new GpuStats(Asleep: false, celsius, 0, null, memoryMhz);
            }

            var (coreMhz, usage) = _nvidia.Read();
            return new GpuStats(Asleep: false, celsius, usage, coreMhz, memoryMhz);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                AppLog.Error("Could not read the GPU figures.", exception);
            }

            return GpuStats.None;
        }
    }

    public void Dispose() => _nvidia.Dispose();

    private bool OthersUseIt(long luid)
    {
        var now = DateTime.UtcNow;

        if (now - _usersCheckedAt < UsersCheckEvery)
            return _othersUseIt;

        _usersCheckedAt = now;
        _othersUseIt = SomeoneElseUses(DiscreteGpuReader.ReadProcessUsage([luid]), Environment.ProcessId);
        return _othersUseIt;
    }

    /// <summary>
    /// Whether an app other than this one holds video memory on the GPU.
    /// Windows' System process keeps a little there whenever the GPU is
    /// awake, so it does not count.
    /// </summary>
    internal static bool SomeoneElseUses(IEnumerable<GpuProcessUsage> usage, int thisProcessId) =>
        usage.Any(process => process.ProcessId != thisProcessId && process.ProcessId != SystemProcessId && process.DedicatedBytes > 0);

    /// <summary>
    /// NVIDIA's management library (nvml.dll), installed with the driver. On a
    /// laptop without it, or with another vendor's GPU, every figure is null.
    /// </summary>
    private sealed class NvidiaDriverLibrary : IDisposable
    {
        private const int Success = 0;
        private const int GraphicsClock = 0;

        private IntPtr _device;
        private bool _initialized;
        private bool _unavailable;

        public (int? CoreMhz, double? UsagePercent) Read()
        {
            if (_unavailable)
                return (null, null);

            try
            {
                if (!_initialized)
                {
                    if (nvmlInit_v2() != Success)
                    {
                        _unavailable = true;
                        return (null, null);
                    }

                    _initialized = true;

                    // Only NVIDIA GPUs are listed, so the first is the dedicated one.
                    if (nvmlDeviceGetHandleByIndex_v2(0, out _device) != Success)
                    {
                        _unavailable = true;
                        Dispose();
                        return (null, null);
                    }
                }

                int? clock = nvmlDeviceGetClockInfo(_device, GraphicsClock, out var mhz) == Success ? (int)mhz : null;
                double? usage = nvmlDeviceGetUtilizationRates(_device, out var rates) == Success ? rates.Gpu : null;
                return (clock, usage);
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                _unavailable = true;
                return (null, null);
            }
        }

        public void Dispose()
        {
            if (!_initialized)
                return;

            _initialized = false;
            nvmlShutdown();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Utilization
        {
            public uint Gpu;
            public uint Memory;
        }

        [DllImport("nvml.dll")]
        private static extern int nvmlInit_v2();

        [DllImport("nvml.dll")]
        private static extern int nvmlShutdown();

        [DllImport("nvml.dll")]
        private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport("nvml.dll")]
        private static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint clockMhz);

        [DllImport("nvml.dll")]
        private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
    }
}
