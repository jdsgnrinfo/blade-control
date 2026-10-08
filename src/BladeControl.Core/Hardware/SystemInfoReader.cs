using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Hardware;

/// <summary>A graphics adapter as Windows lists it. Memory is its dedicated memory, where Windows reports it.</summary>
internal sealed record GpuInfo(string Name, string? VendorId, long? MemoryBytes, bool Integrated);

/// <summary>A physical drive: Windows' number for it, its model, size, and kind ("SSD", "HDD") and bus ("NVMe", "SATA", "USB"), where known.</summary>
internal sealed record DiskInfo(string Id, string Model, long? SizeBytes, string? Kind, string? Bus);

/// <summary>
/// What the laptop is made of and runs, read once: none of it changes while
/// the app runs. A null field is one Windows did not report. DriveDisks
/// gives the drive each letter (such as "C:") is on, by its <see cref="DiskInfo.Id"/>.
/// </summary>
internal sealed record SystemInfo(
    string? Manufacturer,
    string? Model,
    string? BiosVendor,
    string? BiosVersion,
    string? BiosDate,
    string? OsName,
    string? OsVersion,
    int? OsBuild,
    int? OsRevision,
    string? CpuName,
    int? CpuCores,
    int CpuThreads,
    IReadOnlyList<GpuInfo> Gpus,
    string? MemoryType,
    int? MemoryMhz,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyDictionary<string, string> DriveDisks);

/// <summary>The memory in use right now, in bytes.</summary>
internal sealed record MemoryUsage(long TotalBytes, long AvailableBytes);

/// <summary>A drive letter's space, in bytes.</summary>
internal sealed record VolumeUsage(string Name, long TotalBytes, long FreeBytes);

/// <summary>
/// Reads the System information window's figures from the registry and WMI.
/// Every source is optional: one that fails is logged and left out, so the
/// window shows what it can. The fixed figures take a moment (WMI), so they
/// are read once, off the UI thread; memory and drive space are cheap and
/// read on every refresh.
/// </summary>
internal static class SystemInfoReader
{
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string WindowsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string DisplayAdaptersKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private const string IntelVendor = "8086";
    private const string AmdVendor = "1002";

    public static SystemInfo Read()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(BiosKey);
        using var windows = Registry.LocalMachine.OpenSubKey(WindowsKey);
        using var cpu = Registry.LocalMachine.OpenSubKey(CpuKey);

        var (memoryType, memoryMhz) = ReadMemoryModules();

        return new SystemInfo(
            Manufacturer: Text(bios, "SystemManufacturer"),
            Model: Text(bios, "SystemProductName"),
            BiosVendor: Text(bios, "BIOSVendor"),
            BiosVersion: Text(bios, "BIOSVersion"),
            BiosDate: Text(bios, "BIOSReleaseDate"),
            OsName: Text(windows, "ProductName"),
            OsVersion: Text(windows, "DisplayVersion"),
            OsBuild: int.TryParse(Text(windows, "CurrentBuild"), out var build) ? build : null,
            OsRevision: windows?.GetValue("UBR") as int?,
            CpuName: Text(cpu, "ProcessorNameString"),
            CpuCores: ReadCpuCores(),
            CpuThreads: Environment.ProcessorCount,
            Gpus: ReadGpus(),
            MemoryType: memoryType,
            MemoryMhz: memoryMhz,
            Disks: ReadDisks(),
            DriveDisks: ReadDriveDisks());
    }

    public static MemoryUsage? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        return GlobalMemoryStatusEx(ref status)
            ? new MemoryUsage((long)status.TotalPhysical, (long)status.AvailablePhysical)
            : null;
    }

    /// <summary>The fixed drives that are ready, such as C:.</summary>
    public static IReadOnlyList<VolumeUsage> ReadVolumes()
    {
        try
        {
            return
            [
                .. DriveInfo.GetDrives()
                    .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                    .Select(drive => new VolumeUsage(drive.Name.TrimEnd('\\'), drive.TotalSize, drive.AvailableFreeSpace))
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Text(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static int? ReadCpuCores()
    {
        var cores = Query("SELECT NumberOfCores FROM Win32_Processor", "CPU cores")
            .Select(row => row["NumberOfCores"] is uint count ? (int)count : 0)
            .Sum();

        return cores > 0 ? cores : null;
    }

    // The adapters Windows has now (WMI), with their dedicated memory from DirectX or the
    // driver's registry key: WMI caps it at 4 GB.
    private static List<GpuInfo> ReadGpus()
    {
        var adapters = Query("SELECT Name, PNPDeviceID FROM Win32_VideoController", "graphics adapters")
            .Select(row => (Name: row["Name"] as string, Device: row["PNPDeviceID"] as string))
            .Where(adapter => !string.IsNullOrWhiteSpace(adapter.Name) && adapter.Device?.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) == true)
            .Select(adapter => (Name: adapter.Name!.Trim(), Vendor: VendorOf(adapter.Device!)))
            .ToList();

        // A card's own memory is the driver's registry figure (6.0 GB; DirectX
        // gives the 5.8 GB Windows lets programs use). Built-in graphics have
        // only DirectX's figure, the 128 MB Task Manager shows: the registry
        // gives the most it may borrow.
        var registry = ReadAdapterMemory();
        var directX = DxgiAdapterMemory.Read();
        var hasOtherVendor = adapters.Any(adapter => adapter.Vendor is not (IntelVendor or AmdVendor));

        return
        [
            .. adapters.Select(adapter =>
            {
                // Intel's are built in; an AMD one is when there is also a discrete card beside it.
                var integrated = adapter.Vendor == IntelVendor || (adapter.Vendor == AmdVendor && hasOtherVendor);
                var (first, second) = integrated ? (directX, registry) : (registry, directX);
                long? bytes = first.TryGetValue(adapter.Name, out var found) || second.TryGetValue(adapter.Name, out found) ? found : null;

                return new GpuInfo(adapter.Name, adapter.Vendor, bytes, integrated);
            })
        ];
    }

    private static string? VendorOf(string deviceId)
    {
        var at = deviceId.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && at + 8 <= deviceId.Length ? deviceId.Substring(at + 4, 4).ToUpperInvariant() : null;
    }

    private static Dictionary<string, long> ReadAdapterMemory()
    {
        var memory = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var adapters = Registry.LocalMachine.OpenSubKey(DisplayAdaptersKey);

            // Adapters are the numbered keys (0000, 0001...); the others, such as
            // Properties, are not adapters and refuse to be opened.
            foreach (var name in (adapters?.GetSubKeyNames() ?? []).Where(name => name.All(char.IsAsciiDigit)))
            {
                using var adapter = adapters!.OpenSubKey(name);

                if (Text(adapter, "DriverDesc") is not { } description)
                    continue;

                var bytes = adapter!.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long value => value,
                    byte[] { Length: 8 } raw => BitConverter.ToInt64(raw),
                    _ => adapter.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int value => (uint)value,
                        byte[] { Length: 4 } raw => BitConverter.ToUInt32(raw),
                        _ => 0L
                    }
                };

                if (bytes > 0)
                    memory[description] = Math.Max(bytes, memory.GetValueOrDefault(description));
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Error("Could not read the graphics adapters' memory.", exception);
        }

        return memory;
    }

    /// <summary>The type and speed of the installed memory modules (the first that says). Asks WMI, so it takes a moment.</summary>
    public static (string? Type, int? Mhz) ReadMemoryModules()
    {
        var modules = Query("SELECT SMBIOSMemoryType, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory", "memory modules");

        var type = modules.Select(row => row["SMBIOSMemoryType"] is uint code ? MemoryTypeName(code) : null).FirstOrDefault(name => name is not null);
        var mhz = modules
            .Select(row => row["ConfiguredClockSpeed"] is uint configured && configured > 0 ? (int)configured : row["Speed"] is uint speed && speed > 0 ? (int)speed : 0)
            .FirstOrDefault(speed => speed > 0);

        return (type, mhz > 0 ? mhz : null);
    }

    // SMBIOS memory types (DMTF DSP0134, table 76).
    private static string? MemoryTypeName(uint code) => code switch
    {
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null
    };

    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    private static List<DiskInfo> ReadDisks() =>
        [
            .. Query("SELECT DeviceId, FriendlyName, Size, MediaType, BusType FROM MSFT_PhysicalDisk", "drives", StorageScope)
                .Where(row => row["FriendlyName"] is string name && !string.IsNullOrWhiteSpace(name))
                .Select(row => new DiskInfo(
                    row["DeviceId"] as string ?? string.Empty,
                    ((string)row["FriendlyName"]).Trim(),
                    row["Size"] is ulong size ? (long)size : null,
                    row["MediaType"] is ushort media ? media switch { 3 => "HDD", 4 => "SSD", _ => null } : null,
                    row["BusType"] is ushort bus ? bus switch { 7 => "USB", 11 => "SATA", 17 => "NVMe", _ => null } : null))
        ];

    // Which drive each letter is on, from the partitions that have one.
    private static Dictionary<string, string> ReadDriveDisks() =>
        Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", "partitions", StorageScope)
            .Where(row => row["DriveLetter"] is char letter && letter != '\0' && row["DiskNumber"] is uint)
            .GroupBy(row => $"{(char)row["DriveLetter"]}:", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ((uint)group.First()["DiskNumber"]).ToString(CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);

    // One WMI query, its rows read into memory. A failure (WMI off, a missing
    // class) is logged and gives no rows.
    private static List<ManagementBaseObject> Query(string query, string what, string scope = @"root\cimv2")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            return [.. searcher.Get().Cast<ManagementBaseObject>()];
        }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException)
        {
            AppLog.Error($"Could not read the {what} from WMI.", exception);
            return [];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
