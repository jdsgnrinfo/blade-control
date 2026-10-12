using System.Runtime.InteropServices;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Services;

namespace BladeControl.Services;

/// <summary>
/// Game mode's hold on the keyboard: a low-level keyboard hook that keeps the
/// keys <see cref="GameModeKeys"/> picks from Windows. It runs on a thread of
/// its own with its own message loop, so a busy window never makes Windows
/// drop it for answering too slowly. Nothing is recorded or sent anywhere:
/// each key is only looked at to decide whether it passes.
/// </summary>
internal sealed class GameModeHook : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmQuit = 0x0012;
    private const uint FlagAltDown = 0x20;

    private readonly GameModeKeys _keys = new();
    private readonly Lock _sync = new();
    private HookProc? _proc;
    private Thread? _thread;
    private uint _threadId;

    /// <summary>Whether the hook is in place now.</summary>
    public bool IsOn
    {
        get
        {
            lock (_sync)
                return _thread is not null;
        }
    }

    /// <summary>What is blocked besides the Windows keys; takes effect on the next key.</summary>
    public GameModeOptions Options
    {
        get => _keys.Options;
        set => _keys.Options = value;
    }

    /// <summary>Puts the hook in place, or takes it away.</summary>
    public void Set(bool on)
    {
        lock (_sync)
        {
            if (on == (_thread is not null))
                return;

            if (on)
                Start();
            else
                Stop();
        }
    }

    public void Dispose() => Set(false);

    private void Start()
    {
        using var ready = new ManualResetEventSlim();
        _proc = OnKey;

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var hook = SetWindowsHookEx(WhKeyboardLowLevel, _proc, GetModuleHandle(null), 0);
            ready.Set();

            if (hook == IntPtr.Zero)
            {
                AppLog.Error($"Game mode: the keyboard hook could not be set (error {Marshal.GetLastWin32Error()}).");
                return;
            }

            AppLog.Info("Game mode on.");

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            UnhookWindowsHookEx(hook);
            AppLog.Info("Game mode off.");
        })
        {
            IsBackground = true,
            Name = "Game mode keyboard hook",
            Priority = ThreadPriority.AboveNormal
        };

        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void Stop()
    {
        if (_thread is null)
            return;

        PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _proc = null;
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KeyboardInput>(data);
            var down = message == WmKeyDown || message == WmSysKeyDown;

            if (_keys.ShouldBlock((int)key.VirtualKey, down, (key.Flags & FlagAltDown) != 0))
                return 1;
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? module);
}
