using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BladeControl.Services;

/// <summary>
/// Tells when the screen turns off and on, from Windows' console display state
/// notice (GUID_CONSOLE_DISPLAY_STATE): the display timeout, sleep and modern
/// standby, and a closed lid all send it. Needs only a hidden message window;
/// no polling.
/// </summary>
internal sealed class DisplayStateWatcher : IDisposable
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private const int DeviceNotifyWindowHandle = 0;
    private static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private readonly HwndSource _window;
    private IntPtr _registration;

    /// <summary>True when the screen came on, false when it went off. Raised on the UI thread.</summary>
    public event EventHandler<bool>? DisplayChanged;

    public DisplayStateWatcher()
    {
        _window = new HwndSource(new HwndSourceParameters("BladeControl display watcher")
        {
            ParentWindow = MessageOnlyParent,
            WindowStyle = 0
        });
        _window.AddHook(WndProc);

        var guid = ConsoleDisplayState;
        _registration = RegisterPowerSettingNotification(_window.Handle, ref guid, DeviceNotifyWindowHandle);
    }

    private IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmPowerBroadcast && wParam.ToInt32() == PbtPowerSettingChange && lParam != IntPtr.Zero)
        {
            // POWERBROADCAST_SETTING: the setting's GUID, the data length, then
            // the data: 0 off, 1 on, 2 dimmed (still on).
            var setting = Marshal.PtrToStructure<Guid>(lParam);

            if (setting == ConsoleDisplayState)
                DisplayChanged?.Invoke(this, Marshal.ReadInt32(lParam, 20) != 0);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registration != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_registration);
            _registration = IntPtr.Zero;
        }

        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}
