using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Platform;

/// <summary>
/// The dedicated GPU as Device Manager lists it: its device instance id, its
/// name, whether it is turned on and, when Windows says, whether it is awake
/// (in D0) rather than asleep in a low power state.
/// </summary>
internal sealed record DedicatedGpu(string InstanceId, string Name, bool Enabled, bool? Awake = null);

/// <summary>
/// The NVIDIA card as a device: found and read through Windows' device setup
/// API (no rights needed), and turned off or on as Device Manager's Disable
/// and Enable do, through Windows' own pnputil (administrator rights, so only
/// from the elevated command). Turned off, it stays off across restarts until
/// turned on again; the integrated GPU, which drives the laptop's own screen,
/// is never touched.
/// </summary>
internal static class DedicatedGpuDevice
{
    private const string NvidiaVendor = "VEN_10DE";
    private const int DigcfPresent = 0x2;
    private const int SpdrpDeviceDesc = 0x0;
    private const int SpdrpFriendlyName = 0xC;
    private const uint DnHasProblem = 0x400;
    private const uint CmProbDisabled = 22;
    private const int CrSuccess = 0;

    // Windows' "display adapters" device class.
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");

    /// <summary>The NVIDIA display adapter, or null when there is none (or Windows will not say).</summary>
    public static DedicatedGpu? Find()
    {
        var classGuid = DisplayClass;
        var set = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DigcfPresent);

        if (set == new IntPtr(-1))
        {
            AppLog.Error("Could not list the display adapters.", new Win32Exception(Marshal.GetLastWin32Error()));
            return null;
        }

        try
        {
            var info = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };

            for (var index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                var id = InstanceId(set, ref info);

                if (id is null || !id.Contains(NvidiaVendor, StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = Property(set, ref info, SpdrpFriendlyName) ?? Property(set, ref info, SpdrpDeviceDesc) ?? "NVIDIA";
                var disabled = CM_Get_DevNode_Status(out var status, out var problem, info.DevInst, 0) == CrSuccess
                    && (status & DnHasProblem) != 0 && problem == CmProbDisabled;

                return new DedicatedGpu(id, name, Enabled: !disabled, Awake: disabled ? false : IsAwake(info.DevInst));
            }

            return null;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>
    /// Turns the device on or off through pnputil. Needs administrator rights:
    /// call it from the elevated command only. Returns pnputil's exit code
    /// (0 done, 3010 done once Windows restarts).
    /// </summary>
    public static int SetEnabled(string instanceId, bool enabled)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        start.ArgumentList.Add(enabled ? "/enable-device" : "/disable-device");
        start.ArgumentList.Add(instanceId);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("pnputil could not be started.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode is not (0 or RestartNeeded))
            AppLog.Info($"pnputil {(enabled ? "enable" : "disable")} answered {process.ExitCode}: {output.Trim()}");

        return process.ExitCode;
    }

    /// <summary>pnputil's (and Windows') "done, but only after a restart".</summary>
    public const int RestartNeeded = 3010;

    /// <summary>
    /// Whether the device is in D0, from the power state Windows last recorded
    /// for it (Device Manager's "Power data"). Reading it never wakes the card.
    /// Null when Windows will not say.
    /// </summary>
    private static bool? IsAwake(uint devInst)
    {
        var key = PowerDataKey;
        var buffer = new byte[64];
        var size = (uint)buffer.Length;

        if (CM_Get_DevNode_Property(devInst, ref key, out _, buffer, ref size, 0) != CrSuccess || size < 8)
            return null;

        // CM_POWER_DATA: its size, then the most recent DEVICE_POWER_STATE (1 is D0, 4 is D3).
        return BitConverter.ToInt32(buffer, 4) switch
        {
            PowerDeviceD0 => true,
            > PowerDeviceD0 and <= PowerDeviceD3 => false,
            _ => null
        };
    }

    private const int PowerDeviceD0 = 1;
    private const int PowerDeviceD3 = 4;

    // DEVPKEY_Device_PowerData.
    private static readonly DevPropKey PowerDataKey = new() { FormatId = new("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 32 };

    private static string? InstanceId(IntPtr set, ref SpDevinfoData info)
    {
        var buffer = new StringBuilder(512);
        return SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Capacity, out _) ? buffer.ToString() : null;
    }

    private static string? Property(IntPtr set, ref SpDevinfoData info, int property)
    {
        var buffer = new byte[1024];

        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out _, buffer, buffer.Length, out var size) || size < 2)
            return null;

        return Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SpDevinfoData info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SpDevinfoData info, StringBuilder id, int size, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SpDevinfoData info, int property, out int type, byte[] buffer, int size, out int required);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static extern int CM_Get_DevNode_Property(uint devInst, ref DevPropKey key, out uint type, byte[] buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, int flags);
}
