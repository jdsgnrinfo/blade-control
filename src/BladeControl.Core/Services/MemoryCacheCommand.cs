using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// The privileged half of "Free up memory": clearing Windows' memory cache
/// needs administrator rights, so the Optimize window relaunches this same
/// exe elevated (one UAC prompt) with <c>--clear-memory-cache</c>. That
/// process clears the cache, sets its exit code and quits; it never opens a window.
/// </summary>
internal static class MemoryCacheCommand
{
    public const string Switch = "--clear-memory-cache";

    public const int Success = 0;
    public const int Failed = 1;

    public static string[] Arguments() => [Switch];

    /// <summary>
    /// Runs the command if <paramref name="args"/> is it, and returns its exit
    /// code; null otherwise, so normal startup carries on.
    /// </summary>
    public static int? TryRun(string[] args, Func<bool> purge)
    {
        if (args is not [Switch])
            return null;

        try
        {
            if (purge())
            {
                AppLog.Info("Memory cache cleared (elevated).");
                return Success;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("The elevated memory cache command failed.", exception);
        }

        return Failed;
    }
}
