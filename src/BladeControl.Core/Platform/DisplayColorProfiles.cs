using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BladeControl.Core.Platform;

/// <summary>The laptop screen's ICC profiles in Windows' color management, for the current user.</summary>
internal interface IDisplayColorProfiles
{
    /// <summary>Whether the screen uses this user's own profiles ("Use my settings for this device") rather than the PC's.</summary>
    bool UsesMySettings { get; set; }

    /// <summary>The file name of the screen's default profile for this user, or null when it has none.</summary>
    string? GetDefault();

    /// <summary>Copies a profile into Windows' color folder and returns the name it is known by there.</summary>
    string Install(string path);

    /// <summary>Makes an installed profile the screen's default for this user; Windows loads its gamma table at once.</summary>
    void SetDefault(string name);

    /// <summary>Takes a profile off the screen for this user, and out of Windows' color folder.</summary>
    void Remove(string name);
}

/// <summary>
/// The laptop screen's profiles through Windows' color system (mscms), as
/// Color Management does with "Use my settings for this device": only this
/// user's colors change. Windows loads the default profile's gamma table into
/// the graphics card as soon as it is set, at sign-in and after a change of
/// screens. Nothing here needs administrator rights.
/// </summary>
internal sealed class DisplayColorProfiles : IDisplayColorProfiles
{
    private const int CurrentUser = 1;
    private const int IccProfile = 0;
    private const int StandardDisplayColorMode = 7;
    private const uint ClassMonitor = 0x6D6E7472; // 'mntr'

    public bool UsesMySettings
    {
        get
        {
            var (adapter, source) = InternalScreen();
            return ColorProfileGetDisplayUserScope(adapter, source, out var scope) >= 0 && scope == CurrentUser;
        }
        set
        {
            if (!WcsSetUsePerUserProfiles(MonitorId(), ClassMonitor, value))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not change whether the screen uses this user's color settings.");
        }
    }

    public string? GetDefault()
    {
        var (adapter, source) = InternalScreen();

        // No default answers with an error, and the name is then not to be read.
        if (ColorProfileGetDisplayDefault(CurrentUser, adapter, source, IccProfile, StandardDisplayColorMode, out var name) < 0 || name == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUni(name);
        }
        finally
        {
            LocalFree(name);
        }
    }

    public string Install(string path)
    {
        if (!InstallColorProfileW(null, path))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows did not install the color profile {path}.");

        return Path.GetFileName(path);
    }

    public void SetDefault(string name)
    {
        var (adapter, source) = InternalScreen();
        Check(ColorProfileAddDisplayAssociation(CurrentUser, name, adapter, source, true, false), "associate the color profile with the screen");
        Check(ColorProfileSetDisplayDefaultAssociation(CurrentUser, name, IccProfile, StandardDisplayColorMode, adapter, source), "make the color profile the screen's default");
    }

    public void Remove(string name)
    {
        var (adapter, source) = InternalScreen();
        ColorProfileRemoveDisplayAssociation(CurrentUser, name, adapter, source, false);

        // Still in use elsewhere (another user, another screen): Windows keeps it, which is fine.
        UninstallColorProfileW(null, name, true);
    }

    // The laptop panel's adapter and source, as the display configuration names them.
    private static (Luid Adapter, uint Source) InternalScreen()
    {
        const uint OnlyActivePaths = 0x2;
        const uint Internal = 0x80000000;

        if (GetDisplayConfigBufferSizes(OnlyActivePaths, out var pathCount, out var modeCount) == 0)
        {
            var paths = new PathInfo[pathCount];
            var modes = new byte[modeCount * 64];

            if (QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) == 0)
            {
                foreach (var path in paths.AsSpan(0, (int)pathCount))
                {
                    if (path.OutputTechnology == Internal)
                        return (path.SourceAdapter, path.SourceId);
                }
            }
        }

        throw new InvalidOperationException("The laptop screen was not found.");
    }

    // The panel as a monitor device ("MONITOR\CSW150F\{...}\0000"), the name Windows' per-user switch takes.
    private static string MonitorId()
    {
        var (adapter, source) = InternalScreen();
        var request = new SourceName
        {
            Type = 1, // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
            Size = Marshal.SizeOf<SourceName>(),
            Adapter = adapter,
            Id = source
        };

        if (DisplayConfigGetDeviceInfo(ref request) != 0)
            throw new InvalidOperationException("The laptop screen's device name was not found.");

        var monitor = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };

        if (!EnumDisplayDevices(request.GdiDeviceName, 0, ref monitor, 0) || string.IsNullOrEmpty(monitor.DeviceId))
            throw new InvalidOperationException("The laptop screen's monitor was not found.");

        return monitor.DeviceId;
    }

    private static void Check(int result, string what)
    {
        if (result < 0)
            throw new Win32Exception(result, $"Windows could not {what} (0x{result:X8}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public Luid SourceAdapter;
        public uint SourceId;
        public uint SourceModeIndex;
        public uint SourceStatus;
        public Luid TargetAdapter;
        public uint TargetId;
        public uint TargetModeIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatus;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public int Type;
        public int Size;
        public Luid Adapter;
        public uint Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string GdiDeviceName;
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
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] byte[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceName request);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InstallColorProfileW(string? machine, string profile);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UninstallColorProfileW(string? machine, string profile, [MarshalAs(UnmanagedType.Bool)] bool delete);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WcsSetUsePerUserProfiles(string device, uint deviceClass, [MarshalAs(UnmanagedType.Bool)] bool usePerUser);

    [DllImport("mscms.dll")]
    private static extern int ColorProfileGetDisplayUserScope(Luid adapter, uint source, out int scope);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode)]
    private static extern int ColorProfileAddDisplayAssociation(int scope, string profile, Luid adapter, uint source,
        [MarshalAs(UnmanagedType.Bool)] bool setAsDefault, [MarshalAs(UnmanagedType.Bool)] bool advancedColor);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode)]
    private static extern int ColorProfileRemoveDisplayAssociation(int scope, string profile, Luid adapter, uint source,
        [MarshalAs(UnmanagedType.Bool)] bool advancedColor);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode)]
    private static extern int ColorProfileSetDisplayDefaultAssociation(int scope, string profile, int type, int subtype, Luid adapter, uint source);

    [DllImport("mscms.dll")]
    private static extern int ColorProfileGetDisplayDefault(int scope, Luid adapter, uint source, int type, int subtype, out IntPtr profile);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
