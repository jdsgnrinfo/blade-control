using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BladeControl.Core.Platform;

/// <summary>Windows' visual effects battery mode turns off, read and set as the user's own settings.</summary>
internal interface IVisualEffects
{
    /// <summary>Personalization's "Transparency effects".</summary>
    bool Transparency { get; set; }

    /// <summary>"Show shadows under windows".</summary>
    bool WindowShadows { get; set; }

    /// <summary>"Show shadows under mouse pointer".</summary>
    bool PointerShadow { get; set; }

    /// <summary>"Show window contents while dragging".</summary>
    bool DragWindowContents { get; set; }
}

/// <summary>
/// The effects through Windows' own settings: transparency in the user's
/// personalization, the others through SystemParametersInfo, saved to the
/// profile and announced to every window, as the Settings app does. No
/// administrator rights needed.
/// </summary>
internal sealed class WindowsVisualEffects : IVisualEffects
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const uint SpiGetDragFullWindows = 0x0026;
    private const uint SpiSetDragFullWindows = 0x0025;
    private const uint SpiGetCursorShadow = 0x101A;
    private const uint SpiSetCursorShadow = 0x101B;
    private const uint SpiGetDropShadow = 0x1024;
    private const uint SpiSetDropShadow = 0x1025;
    private const uint UpdateProfileAndTell = 0x01 | 0x02;

    public bool Transparency
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
            key.SetValue("EnableTransparency", value ? 1 : 0, RegistryValueKind.DWord);

            // The taskbar, Start and the apps redraw when told the colors changed.
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 1000, out _);
        }
    }

    public bool WindowShadows
    {
        get => GetFlag(SpiGetDropShadow);
        set => SetFlagInPointer(SpiSetDropShadow, value);
    }

    public bool PointerShadow
    {
        get => GetFlag(SpiGetCursorShadow);
        set => SetFlagInPointer(SpiSetCursorShadow, value);
    }

    public bool DragWindowContents
    {
        get => GetFlag(SpiGetDragFullWindows);
        set
        {
            // This one takes its value in the first parameter.
            if (!SystemParametersInfo(SpiSetDragFullWindows, value ? 1u : 0u, IntPtr.Zero, UpdateProfileAndTell))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static bool GetFlag(uint action)
    {
        if (!SystemParametersInfo(action, 0, out int value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return value != 0;
    }

    private static void SetFlagInPointer(uint action, bool value)
    {
        if (!SystemParametersInfo(action, 0, new IntPtr(value ? 1 : 0), UpdateProfileAndTell))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, out int pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
