using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BladeControl.Core.Hardware;

/// <summary>
/// Everything Windows' battery driver reports about the laptop's battery, in
/// its own units: capacities in mWh, voltage in mV, rate in mW (positive while
/// charging, negative while discharging). A null field is one the battery did
/// not report.
/// </summary>
internal sealed record BatteryDetails(
    bool PluggedIn,
    bool Charging,
    bool Discharging,
    int? RemainingMilliwattHours,
    int? FullChargeMilliwattHours,
    int? DesignMilliwattHours,
    int? RateMilliwatts,
    int? VoltageMillivolts,
    int? EstimatedSecondsLeft,
    int? CycleCount,
    string? Name,
    string? Manufacturer,
    string? Chemistry);

/// <summary>
/// Reads the battery straight from Windows' battery class driver
/// (IOCTL_BATTERY_QUERY_*), the same source the Settings app uses. It needs no
/// administrator rights and no extra library, and it only reads. WMI's
/// equivalent fails on some laptops for the design capacity, which the
/// health figure needs, so it is not used.
/// </summary>
internal static class BatteryReader
{
    private static readonly Guid BatteryClass = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

    private const uint DigcfPresent = 0x02;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x03;
    private const uint OpenExisting = 3;

    private const uint IoctlQueryTag = 0x294040;
    private const uint IoctlQueryInformation = 0x294044;
    private const uint IoctlQueryStatus = 0x29404C;

    // BATTERY_QUERY_INFORMATION_LEVEL values used here.
    private const int LevelInformation = 0;
    private const int LevelEstimatedTime = 3;
    private const int LevelDeviceName = 4;
    private const int LevelManufactureName = 6;

    // BATTERY_STATUS.PowerState flags.
    private const uint PowerOnline = 0x1;
    private const uint Discharging = 0x2;
    private const uint Charging = 0x4;

    // What the driver sends for a value it does not know.
    private const uint UnknownValue = 0xFFFFFFFF;
    private const int UnknownRate = int.MinValue; // 0x80000000

    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>The first battery's details, or null when there is no battery or it cannot be read.</summary>
    public static BatteryDetails? Read()
    {
        var path = FindFirstBatteryPath();

        if (path is null)
            return null;

        using var handle = CreateFile(path, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        var tag = QueryTag(handle);

        if (tag is not uint batteryTag)
            return null;

        var information = QueryInformation(handle, batteryTag, LevelInformation, 36);
        var status = QueryStatus(handle, batteryTag);

        if (status is null)
            return null;

        var powerState = BitConverter.ToUInt32(status, 0);
        var capacity = BitConverter.ToUInt32(status, 4);
        var voltage = BitConverter.ToUInt32(status, 8);
        var rate = BitConverter.ToInt32(status, 12);

        var estimated = QueryInformation(handle, batteryTag, LevelEstimatedTime, 4) is { } time
            ? BitConverter.ToUInt32(time, 0)
            : UnknownValue;

        return new BatteryDetails(
            PluggedIn: (powerState & PowerOnline) != 0,
            Charging: (powerState & Charging) != 0,
            Discharging: (powerState & Discharging) != 0,
            RemainingMilliwattHours: Known(capacity),
            FullChargeMilliwattHours: information is null ? null : Known(BitConverter.ToUInt32(information, 16)),
            DesignMilliwattHours: information is null ? null : Known(BitConverter.ToUInt32(information, 12)),
            RateMilliwatts: rate == UnknownRate ? null : rate,
            VoltageMillivolts: Known(voltage),
            EstimatedSecondsLeft: Known(estimated),
            CycleCount: information is null ? null : Known(BitConverter.ToUInt32(information, 32)) is int cycles and > 0 ? cycles : null,
            Name: QueryText(handle, batteryTag, LevelDeviceName),
            Manufacturer: QueryText(handle, batteryTag, LevelManufactureName),
            Chemistry: information is null ? null : ReadChemistry(information));
    }

    // Zero and "unknown" both mean the battery gave no figure.
    private static int? Known(uint value) =>
        value is 0 or UnknownValue || value > int.MaxValue ? null : (int)value;

    // Four ASCII letters, for example "LION" or "LiP".
    private static string? ReadChemistry(byte[] information)
    {
        var text = Encoding.ASCII.GetString(information, 8, 4).TrimEnd('\0', ' ');
        return text.Length > 0 ? text : null;
    }

    private static uint? QueryTag(SafeFileHandle handle)
    {
        var input = BitConverter.GetBytes(0u); // Do not wait for a battery to appear.
        var output = new byte[4];

        return DeviceIoControl(handle, IoctlQueryTag, input, input.Length, output, output.Length, out var returned, IntPtr.Zero) && returned == 4
            ? BitConverter.ToUInt32(output, 0) is var tag and not 0 ? tag : null
            : null;
    }

    private static byte[]? QueryInformation(SafeFileHandle handle, uint tag, int level, int outputSize)
    {
        // BATTERY_QUERY_INFORMATION: tag, level, at-rate (0 = the current rate).
        var input = new byte[12];
        BitConverter.GetBytes(tag).CopyTo(input, 0);
        BitConverter.GetBytes(level).CopyTo(input, 4);

        var output = new byte[outputSize];

        return DeviceIoControl(handle, IoctlQueryInformation, input, input.Length, output, output.Length, out var returned, IntPtr.Zero) && returned > 0
            ? output[..returned]
            : null;
    }

    private static byte[]? QueryStatus(SafeFileHandle handle, uint tag)
    {
        // BATTERY_WAIT_STATUS with no conditions: answer at once.
        var input = new byte[20];
        BitConverter.GetBytes(tag).CopyTo(input, 0);

        var output = new byte[16];

        return DeviceIoControl(handle, IoctlQueryStatus, input, input.Length, output, output.Length, out var returned, IntPtr.Zero) && returned == 16
            ? output
            : null;
    }

    private static string? QueryText(SafeFileHandle handle, uint tag, int level)
    {
        var bytes = QueryInformation(handle, tag, level, 256);

        if (bytes is null)
            return null;

        var text = Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
        return text.Length > 0 ? text : null;
    }

    private static string? FindFirstBatteryPath()
    {
        var classGuid = BatteryClass;
        var deviceSet = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);

        if (deviceSet == InvalidHandle)
            return null;

        try
        {
            var data = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };

            if (!SetupDiEnumDeviceInterfaces(deviceSet, IntPtr.Zero, ref classGuid, 0, ref data))
                return null;

            SetupDiGetDeviceInterfaceDetail(deviceSet, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);

            if (required <= 0)
                return null;

            var buffer = Marshal.AllocHGlobal(required);

            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W: its cbSize is 8 on 64-bit, 6 on 32-bit.
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);

                if (!SetupDiGetDeviceInterfaceDetail(deviceSet, ref data, buffer, required, out _, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return Marshal.PtrToStringUni(buffer + 4);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceSet);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceSet, IntPtr deviceInfo, ref Guid classGuid, uint index, ref DeviceInterfaceData data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceSet, ref DeviceInterfaceData data, IntPtr detail, int detailSize, out int requiredSize, IntPtr deviceInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
