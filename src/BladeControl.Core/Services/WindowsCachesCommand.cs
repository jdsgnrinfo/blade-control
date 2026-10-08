using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// The privileged half of Optimize's Temporary files: Windows Update's
/// downloads and Delivery Optimization's cache belong to Windows, so clearing
/// them needs administrator rights. The window relaunches this same exe
/// elevated (one UAC prompt) with <c>--clean-windows-caches</c>; that process
/// clears them, sets its exit code and quits. It never opens a window.
/// </summary>
internal static class WindowsCachesCommand
{
    public const string Switch = "--clean-windows-caches";

    public const int Success = 0;
    public const int Failed = 1;

    public static string[] Arguments() => [Switch];

    /// <summary>
    /// Runs the command if <paramref name="args"/> is it, and returns its exit
    /// code; null otherwise, so normal startup carries on.
    /// </summary>
    public static int? TryRun(string[] args, Func<bool> clean)
    {
        if (args is not [Switch])
            return null;

        try
        {
            if (clean())
            {
                AppLog.Info("Windows' caches cleared (elevated).");
                return Success;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("The elevated Windows caches command failed.", exception);
        }

        return Failed;
    }
}
