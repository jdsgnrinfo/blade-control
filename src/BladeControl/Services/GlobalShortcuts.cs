using System.Runtime.InteropServices;
using System.Windows.Interop;
using BladeControl.Core.Models;

namespace BladeControl.Services;

/// <summary>
/// The keyboard shortcuts that work from any program, a game in front
/// included, as in Razer Helper: Fn+Del (which the Blade sends as Insert, it
/// has no Insert key of its own) shows the window or hides it, and
/// Ctrl+Shift+F1, F2 and F3 switch to Silent, Balanced and Custom, in the
/// order of the mode buttons. Windows delivers them to a hidden window of
/// ours, so nothing watches the keyboard. (Fn+1/2/3 cannot be used: the
/// Blade's Fn key never reaches Windows, so Fn+1 looks exactly like 1.)
/// </summary>
internal sealed class GlobalShortcuts : IDisposable
{
    /// <summary>What the window shortcut is called in the interface.</summary>
    public const string WindowShortcutText = "Fn+Del";

    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint KeyInsert = 0x2D;
    private const uint KeyF1 = 0x70;
    private const int WindowId = 1;
    private const int FirstModeId = 100;

    /// <summary>Each mode shortcut: its mode, and its name in the interface.</summary>
    public static readonly (PerformanceMode Mode, string Text)[] Modes =
    [
        (PerformanceMode.Silent, "Ctrl+Shift+F1"),
        (PerformanceMode.Balanced, "Ctrl+Shift+F2"),
        (PerformanceMode.Custom, "Ctrl+Shift+F3")
    ];

    private readonly HwndSource _window;
    private readonly HashSet<int> _registered = [];

    public GlobalShortcuts()
    {
        // A message-only window: never shown, only there to receive the shortcuts.
        _window = new HwndSource(new HwndSourceParameters("BladeControl.Shortcuts")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0
        });
        _window.AddHook(OnMessage);
    }

    /// <summary>Fn+Del was pressed.</summary>
    public event EventHandler? WindowRequested;

    /// <summary>A mode shortcut was pressed, with its mode.</summary>
    public event EventHandler<PerformanceMode>? ModeRequested;

    /// <summary>Starts listening for Fn+Del. False when another program already owns the key.</summary>
    public bool TryRegisterWindow() => Register(WindowId, ModNoRepeat, KeyInsert);

    /// <summary>Starts listening for the mode shortcuts. Returns the ones another program already owns.</summary>
    public IReadOnlyList<string> TryRegisterModes()
    {
        var taken = new List<string>();

        for (var index = 0; index < Modes.Length; index++)
        {
            if (!Register(FirstModeId + index, ModControl | ModShift | ModNoRepeat, KeyF1 + (uint)index))
                taken.Add(Modes[index].Text);
        }

        return taken;
    }

    /// <summary>Stops listening for the mode shortcuts, so another program can have them.</summary>
    public void UnregisterModes()
    {
        for (var index = 0; index < Modes.Length; index++)
            Unregister(FirstModeId + index);
    }

    private bool Register(int id, uint modifiers, uint key)
    {
        if (_registered.Contains(id))
            return true;

        if (!RegisterHotKey(_window.Handle, id, modifiers, key))
            return false;

        _registered.Add(id);
        return true;
    }

    private void Unregister(int id)
    {
        if (_registered.Remove(id))
            UnregisterHotKey(_window.Handle, id);
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey)
            return IntPtr.Zero;

        var id = (int)wParam;

        if (id == WindowId)
            WindowRequested?.Invoke(this, EventArgs.Empty);
        else if (id - FirstModeId is var index and >= 0 && index < Modes.Length)
            ModeRequested?.Invoke(this, Modes[index].Mode);
        else
            return IntPtr.Zero;

        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered)
            UnregisterHotKey(_window.Handle, id);

        _registered.Clear();
        _window.RemoveHook(OnMessage);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
