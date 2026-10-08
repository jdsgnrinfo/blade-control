using System.Diagnostics;
using System.Runtime.InteropServices;
using BladeControl.Core.Hardware;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>
/// The Windows calls behind "Free up memory": the user's programs, the one
/// in front, and EmptyWorkingSet, which only moves a program's memory out of
/// RAM and never stops it. Needs no administrator rights; Windows says no for
/// programs that are not the user's.
/// </summary>
internal sealed class WindowsProcessMemory : IProcessMemory
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint SetQuota = 0x0100;

    public IReadOnlyList<UserProgram> Processes()
    {
        var found = new List<UserProgram>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    found.Add(new UserProgram(process.Id, process.ProcessName, process.SessionId));
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Gone meanwhile, or not ours to look at.
                }
            }
        }

        return found;
    }

    public int ForegroundProcessId()
    {
        var window = GetForegroundWindow();

        if (window == IntPtr.Zero)
            return 0;

        GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    public int CurrentSession() => Process.GetCurrentProcess().SessionId;

    public bool Trim(int processId)
    {
        var handle = OpenProcess(QueryLimitedInformation | SetQuota, false, (uint)processId);

        if (handle == IntPtr.Zero)
            return false;

        try
        {
            return K32EmptyWorkingSet(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public long AvailableBytes() => SystemInfoReader.ReadMemory()?.AvailableBytes ?? 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
