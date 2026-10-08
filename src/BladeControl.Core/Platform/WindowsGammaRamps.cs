using System.Runtime.InteropServices;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>
/// The screens' gamma ramps through GDI, as f.lux and calibration loaders set
/// them: per screen, on whichever graphics card drives it (on this laptop the
/// built-in screen is the Intel one's). Needs no administrator rights.
/// </summary>
internal sealed class WindowsGammaRamps : IGammaRamps
{
    private const int AttachedToDesktop = 0x1;

    public IReadOnlyList<string> Displays()
    {
        var displays = new List<string>();
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };

        for (uint index = 0; EnumDisplayDevices(null, index, ref device, 0); index++)
        {
            if ((device.StateFlags & AttachedToDesktop) != 0)
                displays.Add(device.DeviceName);

            device.Size = Marshal.SizeOf<DisplayDevice>();
        }

        return displays;
    }

    public ushort[]? Get(string display)
    {
        var ramp = new ushort[3 * ColorProfiles.Levels];
        return WithDc(display, dc => GetDeviceGammaRamp(dc, ramp)) ? ramp : null;
    }

    public bool Set(string display, ushort[] ramp) => WithDc(display, dc => SetDeviceGammaRamp(dc, ramp));

    private static bool WithDc(string display, Func<IntPtr, bool> use)
    {
        var dc = CreateDC(null, display, null, IntPtr.Zero);

        if (dc == IntPtr.Zero)
            return false;

        try
        {
            return use(dc);
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr initData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool GetDeviceGammaRamp(IntPtr dc, [Out] ushort[] ramp);

    [DllImport("gdi32.dll")]
    private static extern bool SetDeviceGammaRamp(IntPtr dc, ushort[] ramp);
}
