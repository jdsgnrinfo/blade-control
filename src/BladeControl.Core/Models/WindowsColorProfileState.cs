namespace BladeControl.Core.Models;

/// <summary>
/// The app's color profile as installed in Windows: its file name there, and
/// how the screen was set before, to put back when Standard is picked.
/// </summary>
/// <param name="Installed">The profile's name in Windows' color folder ("Blade Control Warm.icm").</param>
/// <param name="UsedMySettingsBefore">Whether the screen already used this user's own profiles.</param>
/// <param name="DefaultBefore">The screen's default profile for this user before, if it had one.</param>
internal sealed record WindowsColorProfileState(string Installed, bool UsedMySettingsBefore, string? DefaultBefore);
