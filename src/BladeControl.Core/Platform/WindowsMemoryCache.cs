using System.Management;
using System.Runtime.InteropServices;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Platform;

/// <summary>
/// Windows' memory cache: the "standby" pages it keeps in RAM in case a file
/// or program is wanted again. Clearing it needs administrator rights (the
/// profile-single-process privilege), so <see cref="Purge"/> runs only in the
/// app's elevated helper; reading its size needs none.
/// </summary>
internal static class WindowsMemoryCache
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;

    private const string ProfileSingleProcessPrivilege = "SeProfileSingleProcessPrivilege";
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint PrivilegeEnabled = 0x00000002;

    /// <summary>
    /// Writes changed pages out, then empties the standby list. Nothing is
    /// lost: what was cached is read from disk again if it is wanted. True
    /// when Windows accepted both.
    /// </summary>
    public static bool Purge()
    {
        if (!EnablePrivilege(ProfileSingleProcessPrivilege))
        {
            AppLog.Error("Could not enable the privilege to clear the memory cache.");
            return false;
        }

        var flushed = SetMemoryList(MemoryFlushModifiedList);
        var purged = SetMemoryList(MemoryPurgeStandbyList);

        if (flushed != 0 || purged != 0)
            AppLog.Error($"Windows refused to clear the memory cache (flush 0x{flushed:X8}, purge 0x{purged:X8}).");

        return flushed == 0 && purged == 0;
    }

    /// <summary>The memory cache's size in bytes, or null when Windows does not say.</summary>
    public static long? CachedBytes()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT StandbyCacheCoreBytes, StandbyCacheNormalPriorityBytes, StandbyCacheReserveBytes FROM Win32_PerfFormattedData_PerfOS_Memory");

            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    return Convert.ToInt64(item["StandbyCacheCoreBytes"]) +
                           Convert.ToInt64(item["StandbyCacheNormalPriorityBytes"]) +
                           Convert.ToInt64(item["StandbyCacheReserveBytes"]);
                }
            }
        }
        catch (Exception exception) when (exception is ManagementException or COMException or InvalidCastException)
        {
            AppLog.Error("Could not read the memory cache's size.", exception);
        }

        return null;
    }

    private static int SetMemoryList(int command)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));

        try
        {
            Marshal.WriteInt32(buffer, command);
            return NtSetSystemInformation(SystemMemoryListInformation, buffer, sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            return false;

        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                return false;

            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };

            // Succeeds even when the privilege is not held; the last error tells.
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) &&
                   Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // Windows lays the LUID at a 4-byte boundary, straight after the count.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int informationClass, IntPtr information, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges newState, int length, IntPtr previous, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
