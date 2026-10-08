using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace BladeControl.Services;

/// <summary>
/// Gives memory back while Blade Control waits in the tray. Nothing is lost:
/// a page moved out of RAM is read back the moment it is needed, which in
/// the tray is rarely.
/// </summary>
internal static class BackgroundMemory
{
    /// <summary>
    /// Once the window has closed: a full, compacting collection hands the
    /// pages, pictures and drawing surfaces it held back to Windows, then the
    /// rest is moved out of RAM.
    /// </summary>
    public static void Release()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Trim();
    }

    /// <summary>Moves what the app is not using right now out of RAM (EmptyWorkingSet).</summary>
    public static void Trim()
    {
        using var self = Process.GetCurrentProcess();
        EmptyWorkingSet(self.Handle);
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
