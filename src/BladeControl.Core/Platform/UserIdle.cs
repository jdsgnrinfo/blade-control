using System.Runtime.InteropServices;

namespace BladeControl.Core.Platform;

/// <summary>How long since the last key press or mouse move, from Windows' own count.</summary>
internal static class UserIdle
{
    public static TimeSpan Time()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };

        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        // Both counts wrap after 49.7 days; unsigned subtraction keeps the gap right.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
