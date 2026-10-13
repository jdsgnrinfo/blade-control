using Microsoft.Win32;

namespace BladeControl.Core.Platform;

/// <summary>The BIOS version and date as Windows records them at startup, for the log.</summary>
internal static class BiosInfo
{
    /// <summary>"1.07 (12/05/2025)", or "unknown" when Windows does not say.</summary>
    public static string Describe()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var version = key?.GetValue("BIOSVersion") as string;
            var date = key?.GetValue("BIOSReleaseDate") as string;

            return string.IsNullOrWhiteSpace(version)
                ? "unknown"
                : string.IsNullOrWhiteSpace(date) ? version.Trim() : $"{version.Trim()} ({date.Trim()})";
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "unknown";
        }
    }
}
