using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// The privileged half of Optimize's hibernation switch: turning hibernation
/// (and with it the hibernation file and fast startup) off or on needs
/// administrator rights, so the window relaunches this same exe elevated (one
/// UAC prompt) with <c>--hibernation off</c> or <c>--hibernation on</c>. That
/// process makes the change, sets its exit code and quits; it never opens a window.
/// </summary>
internal static class HibernationCommand
{
    public const string Switch = "--hibernation";

    public const int Success = 0;
    public const int Failed = 1;

    public static string[] Arguments(bool enabled) => [Switch, enabled ? "on" : "off"];

    /// <summary>
    /// Runs the command if <paramref name="args"/> is it, and returns its exit
    /// code; null otherwise, so normal startup carries on.
    /// </summary>
    /// <param name="setEnabled">Turns hibernation on (true) or off; false when Windows refused.</param>
    public static int? TryRun(string[] args, Func<bool, bool> setEnabled)
    {
        if (args is not [Switch, var state] || state is not ("on" or "off"))
            return null;

        var enabled = state == "on";

        try
        {
            if (setEnabled(enabled))
            {
                AppLog.Info($"Hibernation turned {state} (elevated).");
                return Success;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"The elevated hibernation command ({state}) failed.", exception);
        }

        return Failed;
    }
}
