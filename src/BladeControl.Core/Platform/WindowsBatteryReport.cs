using System.Diagnostics;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Platform;

/// <summary>
/// Asks Windows for its battery report (<c>powercfg /batteryreport /xml</c>):
/// the record it keeps of the charge, the charger and sleep over the last
/// days, kept whether or not Blade Control was running. It needs no
/// administrator rights and takes a fraction of a second. The file goes to
/// the temp folder and is deleted once read.
/// </summary>
internal static class WindowsBatteryReport
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>The report's XML, or null when powercfg could not make it.</summary>
    public static string? Read()
    {
        var path = Path.Combine(Path.GetTempPath(), $"BladeControl-battery-{Guid.NewGuid():N}.xml");

        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("/batteryreport");
            start.ArgumentList.Add("/xml");
            start.ArgumentList.Add("/output");
            start.ArgumentList.Add(path);

            using var process = Process.Start(start);

            if (process is null)
                return null;

            // Read both so a full pipe cannot hold it up.
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                AppLog.Info("powercfg took too long to write the battery report.");
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(path))
            {
                AppLog.Info($"powercfg did not write the battery report (exit {process.ExitCode}): {errors.Result.Trim()}");
                return null;
            }

            return File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read Windows' battery report.", exception);
            return null;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Left in temp for Windows to clear.
            }
        }
    }
}
