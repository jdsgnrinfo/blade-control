using System.Management;

namespace BladeControl.Core.Platform;

/// <summary>The laptop screen's brightness, 0 to 100.</summary>
internal interface IScreenBrightness
{
    /// <summary>The brightness now, or null when the screen does not say (an external monitor only).</summary>
    int? Read();

    void Set(int percent);
}

/// <summary>
/// The built-in screen's brightness through WMI, the same control as the
/// brightness slider in Windows. External monitors are not reached this way,
/// and need no administrator rights to change.
/// </summary>
internal sealed class WindowsScreenBrightness : IScreenBrightness
{
    private static readonly ManagementScope Scope = new(@"root\wmi");

    public int? Read()
    {
        using var searcher = new ManagementObjectSearcher(Scope, new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE"));

        foreach (var screen in searcher.Get().Cast<ManagementObject>())
        {
            using (screen)
                return Convert.ToInt32(screen["CurrentBrightness"]);
        }

        return null;
    }

    public void Set(int percent)
    {
        using var searcher = new ManagementObjectSearcher(Scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE"));

        foreach (var screen in searcher.Get().Cast<ManagementObject>())
        {
            using (screen)
                screen.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)Math.Clamp(percent, 0, 100)]);
        }
    }
}
