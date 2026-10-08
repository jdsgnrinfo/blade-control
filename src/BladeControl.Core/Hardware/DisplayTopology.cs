using System.Runtime.InteropServices;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Hardware;

/// <summary>
/// Tells whether an external display is connected, and which GDI device name
/// is the laptop's own panel, by asking Windows how each active display is
/// wired. On this class of laptop the ports are wired to the dedicated GPU,
/// so an external display keeps it awake no matter which apps are closed;
/// separately, refresh-rate changes must land on the panel, not a monitor.
/// </summary>
internal static class DisplayTopology
{
    private const uint OnlyActivePaths = 0x00000002;
    private const int Success = 0;
    private const int InsufficientBuffer = 122;
    private const int GetSourceName = 1;
    private const int GetTargetName = 2;
    private const int GetAdvancedColorInfo = 9;

    // Sizes of DISPLAYCONFIG_PATH_INFO and DISPLAYCONFIG_MODE_INFO. A path's
    // first 20 bytes are its source info (adapter id low/high, then id), and
    // outputTechnology sits after that, then the target's adapter id (8) and
    // id (4) and mode index (4).
    private const int PathInfoSize = 72;
    private const int ModeInfoSize = 64;
    private const int SourceAdapterIdLowOffset = 0;
    private const int SourceAdapterIdHighOffset = 4;
    private const int SourceIdOffset = 8;
    private const int TargetAdapterIdLowOffset = 20;
    private const int TargetAdapterIdHighOffset = 24;
    private const int TargetIdOffset = 28;
    private const int OutputTechnologyOffset = 36;

    private const uint OutputTechnologyInternal = 0x80000000;
    private const uint OutputTechnologyDisplayPortEmbedded = 11;
    private const uint OutputTechnologyUdiEmbedded = 13;

    /// <summary>
    /// True when at least one active display is not the laptop's own panel,
    /// false when only the panel is active, and null when Windows would not
    /// say. Callers should treat null like true: do nothing risky.
    /// </summary>
    public static bool? HasExternalDisplay()
    {
        try
        {
            return ReadPaths()?.Any(path => !IsInternal(path.OutputTechnology));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Error("Could not query the display configuration.", exception);
            return null;
        }
    }

    /// <summary>
    /// The GDI device name (such as "\\.\DISPLAY1") of the laptop's own
    /// panel, for EnumDisplaySettings/ChangeDisplaySettingsEx, which default
    /// to the primary display when given none. Null if Windows would not say
    /// or no active path is wired as internal (an external-only setup, or an
    /// unrecognized wiring counted as external by <see cref="IsInternal"/>).
    /// </summary>
    public static string? GetInternalDisplayDeviceName()
    {
        try
        {
            var paths = ReadPaths();
            var internalPath = paths?.FirstOrDefault(path => IsInternal(path.OutputTechnology));

            return internalPath is { } path ? ReadSourceDeviceName(path) : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Error("Could not query the display configuration.", exception);
            return null;
        }
    }

    /// <summary>The wiring types that mean the built-in panel. Anything else, including "unknown", counts as external.</summary>
    internal static bool IsInternal(uint outputTechnology) =>
        outputTechnology is OutputTechnologyInternal
            or OutputTechnologyDisplayPortEmbedded
            or OutputTechnologyUdiEmbedded;

    /// <summary>
    /// The laptop's own panel as Windows drives it: its device path (which
    /// leads to its EDID in the registry), whether it can show HDR and has it
    /// on, and the bits per color it is sent. Null when Windows would not say
    /// or no active display is the panel.
    /// </summary>
    public static InternalPanel? ReadInternalPanel()
    {
        try
        {
            if (ReadPaths()?.FirstOrDefault(path => IsInternal(path.OutputTechnology)) is not { } path)
                return null;

            var name = new DisplayConfigTargetDeviceName { Header = TargetHeader(path, GetTargetName, Marshal.SizeOf<DisplayConfigTargetDeviceName>()) };
            var devicePath = DisplayConfigGetDeviceInfo(ref name) == Success && !string.IsNullOrEmpty(name.MonitorDevicePath) ? name.MonitorDevicePath : null;

            var color = new DisplayConfigAdvancedColorInfo { Header = TargetHeader(path, GetAdvancedColorInfo, Marshal.SizeOf<DisplayConfigAdvancedColorInfo>()) };
            var hasColor = DisplayConfigGetDeviceInfo(ref color) == Success;

            return new InternalPanel(
                devicePath,
                hasColor ? (color.Value & 0x1) != 0 : null,
                hasColor ? (color.Value & 0x2) != 0 : null,
                hasColor && color.BitsPerColorChannel > 0 ? (int)color.BitsPerColorChannel : null);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Error("Could not query the display configuration.", exception);
            return null;
        }
    }

    private static DisplayConfigDeviceInfoHeader TargetHeader(PathSource path, int type, int size) => new()
    {
        Type = type,
        Size = size,
        AdapterIdLow = path.TargetAdapterIdLow,
        AdapterIdHigh = path.TargetAdapterIdHigh,
        Id = path.TargetId
    };

    private readonly record struct PathSource(int AdapterIdLow, int AdapterIdHigh, uint Id, uint OutputTechnology, int TargetAdapterIdLow, int TargetAdapterIdHigh, uint TargetId);

    private static List<PathSource>? ReadPaths()
    {
        // The display setup can change between asking for the size and reading
        // it, so retry a few times.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(OnlyActivePaths, out var pathCount, out var modeCount) != Success)
                return null;

            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            var result = QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (result == InsufficientBuffer)
                continue;

            if (result != Success)
                return null;

            var sources = new List<PathSource>();

            for (var index = 0; index < pathCount; index++)
            {
                var offset = index * PathInfoSize;

                sources.Add(new PathSource(
                    AdapterIdLow: BitConverter.ToInt32(paths, offset + SourceAdapterIdLowOffset),
                    AdapterIdHigh: BitConverter.ToInt32(paths, offset + SourceAdapterIdHighOffset),
                    Id: BitConverter.ToUInt32(paths, offset + SourceIdOffset),
                    OutputTechnology: BitConverter.ToUInt32(paths, offset + OutputTechnologyOffset),
                    TargetAdapterIdLow: BitConverter.ToInt32(paths, offset + TargetAdapterIdLowOffset),
                    TargetAdapterIdHigh: BitConverter.ToInt32(paths, offset + TargetAdapterIdHighOffset),
                    TargetId: BitConverter.ToUInt32(paths, offset + TargetIdOffset)));
            }

            return sources;
        }

        return null;
    }

    private static string? ReadSourceDeviceName(PathSource source)
    {
        var request = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetSourceName,
                Size = Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterIdLow = source.AdapterIdLow,
                AdapterIdHigh = source.AdapterIdHigh,
                Id = source.Id
            }
        };

        return DisplayConfigGetDeviceInfo(ref request) == Success ? request.ViewGdiDeviceName : null;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] byte[] paths,
        ref uint modeCount,
        [Out] byte[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigAdvancedColorInfo requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public int Type;
        public int Size;
        public int AdapterIdLow;
        public int AdapterIdHigh;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public int ColorEncoding;
        public uint BitsPerColorChannel;
    }
}

/// <summary>The laptop's own panel as Windows drives it (see <see cref="DisplayTopology.ReadInternalPanel"/>).</summary>
internal sealed record InternalPanel(string? DevicePath, bool? HdrSupported, bool? HdrOn, int? BitsPerColor);
