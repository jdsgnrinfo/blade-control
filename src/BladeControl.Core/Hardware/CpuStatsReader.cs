using System.ComponentModel;
using System.Diagnostics;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Hardware;

/// <summary>The CPU as Task Manager shows it. A null figure means there was no reading.</summary>
internal sealed record CpuStats(double? UsagePercent, double? CurrentMhz)
{
    public static CpuStats None { get; } = new(null, null);
}

/// <summary>
/// Reads the CPU's load and current clock from Windows' performance
/// counters: the same numbers Task Manager shows. Only reads; no driver and
/// no administrator rights. Not thread-safe: one read at a time.
/// </summary>
internal sealed class CpuStatsReader : IDisposable
{
    private const string Category = "Processor Information";
    private const string Total = "_Total";

    private PerformanceCounter? _utility;
    private PerformanceCounter? _performance;
    private PerformanceCounter? _baseFrequency;
    private bool _primed;
    private bool _loggedFailure;

    public CpuStats Read()
    {
        try
        {
            _utility ??= new PerformanceCounter(Category, "% Processor Utility", Total, readOnly: true);
            _performance ??= new PerformanceCounter(Category, "% Processor Performance", Total, readOnly: true);
            _baseFrequency ??= new PerformanceCounter(Category, "Processor Frequency", Total, readOnly: true);

            // Rate counters compare with the previous read: the first one has
            // nothing to compare with and always says 0.
            var utility = _utility.NextValue();
            var performance = _performance.NextValue();
            var baseMhz = _baseFrequency.NextValue();

            if (!_primed)
            {
                _primed = true;
                return CpuStats.None;
            }

            // Utility is measured against the base clock, so it goes past 100 under boost; Task Manager caps it too.
            return new CpuStats(
                Math.Clamp(utility, 0, 100),
                baseMhz > 0 && performance > 0 ? baseMhz * performance / 100 : null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                AppLog.Error("Could not read the CPU figures.", exception);
            }

            return CpuStats.None;
        }
    }

    public void Dispose()
    {
        _utility?.Dispose();
        _performance?.Dispose();
        _baseFrequency?.Dispose();
    }
}
