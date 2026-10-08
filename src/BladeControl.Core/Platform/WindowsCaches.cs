using System.Diagnostics;
using System.Runtime.InteropServices;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>What the Recycle Bin holds, on every drive.</summary>
internal sealed record RecycleBinContents(long Bytes, long Items);

/// <summary>
/// The rest of Temporary files: the Recycle Bin (the user's own, emptied
/// without administrator rights), and Windows Update's downloads and Delivery
/// Optimization's cache (Windows' own, cleared elevated, see WindowsCachesCommand).
/// </summary>
internal static class WindowsCaches
{
    private const uint NoConfirmation = 0x1;
    private const uint NoProgressUi = 0x2;
    private const uint NoSound = 0x4;

    /// <summary>
    /// Windows Update's downloaded updates, over a day old, so an update that
    /// is downloading now is left alone. Windows downloads again what it still needs.
    /// </summary>
    public static TempCleaner WindowsUpdate() =>
        new([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")], TempCleaner.MinimumAge);

    /// <summary>What the Recycle Bin holds; empty when Windows does not say.</summary>
    public static RecycleBinContents RecycleBin()
    {
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? new RecycleBinContents(info.Bytes, info.Items) : new RecycleBinContents(0, 0);
    }

    /// <summary>Empties the Recycle Bin on every drive, with no question or sound; true when it is empty after.</summary>
    public static bool EmptyRecycleBin()
    {
        var result = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);

        // An already empty bin answers E_UNEXPECTED.
        return result == 0 || RecycleBin().Items == 0;
    }

    /// <summary>
    /// The elevated part: Windows Update's old downloads, then Delivery
    /// Optimization's cache through Windows' own command. True when both ran.
    /// </summary>
    public static bool CleanElevated()
    {
        var update = WindowsUpdate().Clean();
        AppLog.Info($"Windows Update downloads: {update.Files} removed ({update.Bytes} bytes), {update.Skipped} in use left.");

        var start = new ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Delete-DeliveryOptimizationCache -Force" })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start);

        if (process is null)
            return false;

        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            AppLog.Info($"Delivery Optimization's cache was not cleared: {error.Trim()}");

        return process.ExitCode == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr window, string? rootPath, uint flags);
}
