using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BladeControl.Core.Services;

internal sealed class PowerSourceService : IPowerSource, IDisposable
{
    private readonly Func<bool?> _readPluggedIn;
    private readonly object _sync = new();
    private bool? _lastPluggedIn;

    public PowerSourceService() : this(ReadWindowsPowerLine)
    {
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
    }

    // The reader is a parameter so the filtering can be tested without Windows.
    internal PowerSourceService(Func<bool?> readPluggedIn)
    {
        _readPluggedIn = readPluggedIn;
        _lastPluggedIn = readPluggedIn();
    }

    public bool? IsPluggedIn => _readPluggedIn();

    // Windows reports 0 to 100, or 255 when there is no battery or it cannot tell.
    public int? BatteryPercent =>
        ReadStatus() is { BatteryLifePercent: <= 100 } status
            ? status.BatteryLifePercent
            : null;

    public event EventHandler? PowerSourceChanged;

    private void SystemEvents_PowerModeChanged(
        object? sender,
        PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
            OnStatusChange();
    }

    /// <summary>
    /// Windows reports a status change for every battery percentage step as well
    /// as for plugging in and unplugging. Only the latter matters to anyone
    /// listening, so the rest is dropped here instead of waking every listener.
    /// </summary>
    internal void OnStatusChange()
    {
        var pluggedIn = _readPluggedIn();

        lock (_sync)
        {
            if (pluggedIn == _lastPluggedIn)
                return;

            _lastPluggedIn = pluggedIn;
        }

        PowerSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool? ReadWindowsPowerLine() => ReadStatus()?.ACLineStatus switch
    {
        1 => true,
        0 => false,
        _ => null
    };

    private static SystemPowerStatus? ReadStatus() =>
        GetSystemPowerStatus(out var status) ? status : null;

    // ACLineStatus is 0 on battery, 1 plugged in and 255 when Windows cannot tell.
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
    }
}
