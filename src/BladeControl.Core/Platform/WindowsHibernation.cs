using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Platform;

/// <summary>
/// Windows' hibernation: whether it is on, how much disk its file takes, and
/// turning it off or on (with administrator rights, see HibernationCommand).
/// Off also turns off fast startup, which uses the same file.
/// </summary>
internal static class WindowsHibernation
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";

    /// <summary>True when hibernation is on; null when Windows does not say.</summary>
    public static bool? IsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PowerKey);
            return key?.GetValue("HibernateEnabled") is int value ? value != 0 : null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Error("Could not read whether hibernation is on.", exception);
            return null;
        }
    }

    /// <summary>The size of hiberfil.sys on the Windows drive; null when there is none.</summary>
    public static long? FileBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var file = new FileInfo(Path.Combine(root, "hiberfil.sys"));
            return file.Exists ? file.Length : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Turns hibernation on or off with Windows' own powercfg. Needs administrator rights.</summary>
    public static bool SetEnabled(bool enabled)
    {
        var start = new ProcessStartInfo("powercfg.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        start.ArgumentList.Add("/hibernate");
        start.ArgumentList.Add(enabled ? "on" : "off");

        using var process = Process.Start(start) ?? throw new Win32Exception("Could not start powercfg.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
