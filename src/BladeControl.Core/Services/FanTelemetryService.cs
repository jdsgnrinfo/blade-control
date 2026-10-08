using System.ComponentModel;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

internal sealed class FanTelemetryService(IRazerTransport transport)
{
    private const byte CpuFanZone = 0x01;
    private const byte GpuFanZone = 0x02;

    private FanRpmReading? _publishedReading;
    private bool _stoppedReadingPending;
    private bool _readFailing;

    /// <summary>
    /// False once the laptop has said it does not report fan speeds (the Blade
    /// 15 Base 2020 does not implement the command). It is not asked again.
    /// </summary>
    public bool IsSupported { get; private set; } = true;

    public Task<FanRpmReading?> ReadAsync() => Task.Run(() =>
    {
        // A model without this command answers "not supported" once; asking
        // again every couple of seconds would be pointless traffic.
        if (!IsSupported)
            return null;

        try
        {
            var reading = Read();

            if (_readFailing)
            {
                _readFailing = false;
                AppLog.Info("Fan telemetry recovered.");
            }

            return reading;
        }
        catch (RazerCommandNotSupportedException)
        {
            IsSupported = false;
            AppLog.Info("This laptop does not report fan speeds through its controller.");
            return null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            // Polled every couple of seconds: log only the first failure of a
            // streak so a disconnected device does not flood the log.
            if (!_readFailing)
            {
                _readFailing = true;
                AppLog.Error("Fan telemetry is temporarily unavailable.", exception);
            }

            return _publishedReading;
        }
    });

    private FanRpmReading? Read()
    {
        var reading = new FanRpmReading(
            ReadFanRpm(CpuFanZone),
            ReadFanRpm(GpuFanZone));

        return Filter(reading);
    }

    private int ReadFanRpm(byte fanZone)
    {
        // Older models (Blade 15 Base 2020) do not have this command. Their
        // 0x0D81 is no substitute: it holds the controller's fan target, which
        // sits at 2000 whatever the fans are really doing.
        var response = transport.Send(
            RazerCommands.GetActualFanRpm,
            [0x00, fanZone, 0x00]);

        if (RazerHidPacket.GetArgument(response, 1) != fanZone)
            throw new InvalidOperationException("The fan response used an unexpected zone.");

        return RazerHidPacket.GetArgument(response, 2) * 100;
    }

    private FanRpmReading? Filter(FanRpmReading reading)
    {
        var hasOnlyOneStoppedFan =
            (reading.CpuFanRpm == 0) != (reading.GpuFanRpm == 0);

        if (hasOnlyOneStoppedFan)
            return _publishedReading;

        var bothFansStopped =
            reading.CpuFanRpm == 0 && reading.GpuFanRpm == 0;

        if (bothFansStopped && !_stoppedReadingPending)
        {
            _stoppedReadingPending = true;
            return _publishedReading;
        }

        _stoppedReadingPending = false;
        _publishedReading = reading;
        return _publishedReading;
    }
}
