using Microsoft.Win32;

namespace BladeControl.Core.Hardware;

/// <summary>The laptop's panel: what its EDID says, and how Windows drives it.</summary>
internal sealed record DisplayPanel(Edid? Edid, bool? HdrSupported, bool? HdrOn, int? BitsPerColor);

/// <summary>
/// Reads the laptop's own panel: Windows' path to it, then its EDID, which
/// Windows keeps in the registry under the monitor's device
/// ("SYSTEM\CurrentControlSet\Enum\DISPLAY\BOE0868\...\Device Parameters").
/// Only reads; needs no administrator rights.
/// </summary>
internal static class DisplayPanelReader
{
    public static DisplayPanel? Read()
    {
        if (DisplayTopology.ReadInternalPanel() is not { } panel)
            return null;

        var edid = panel.DevicePath is { } path ? ReadEdid(path) : null;
        return new DisplayPanel(edid, panel.HdrSupported, panel.HdrOn, panel.BitsPerColor ?? edid?.BitsPerColor);
    }

    /// <summary>
    /// The registry key of a monitor's device path, such as
    /// "\\?\DISPLAY#BOE0868#4&amp;2f2e9f3c&amp;0&amp;UID8388688#{e6f07b5f-...}":
    /// "SYSTEM\CurrentControlSet\Enum\DISPLAY\BOE0868\4&amp;2f2e9f3c&amp;0&amp;UID8388688\Device Parameters".
    /// Null when the path is not of that shape.
    /// </summary>
    internal static string? RegistryKeyFor(string devicePath)
    {
        var parts = devicePath.Split('#');

        if (parts.Length < 3 || !parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase))
            return null;

        return $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters";
    }

    private static Edid? ReadEdid(string devicePath)
    {
        if (RegistryKeyFor(devicePath) is not { } keyName)
            return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyName);
            return key?.GetValue("EDID") is byte[] data ? Edid.Parse(data) : null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Diagnostics.AppLog.Error("Could not read the panel's EDID.", exception);
            return null;
        }
    }
}
