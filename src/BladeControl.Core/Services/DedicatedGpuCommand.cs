using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// The privileged half of Display's integrated-graphics-only switch: turning
/// the dedicated GPU off or on, as Device Manager does, needs administrator
/// rights, so the window relaunches this same exe elevated (one UAC prompt)
/// with <c>--dedicated-gpu off</c> or <c>--dedicated-gpu on</c>. That process
/// finds the card itself, makes the change, sets its exit code and quits; it
/// never opens a window.
/// </summary>
internal static class DedicatedGpuCommand
{
    public const string Switch = "--dedicated-gpu";

    public const int Success = 0;
    public const int Failed = 1;
    public const int RestartNeeded = 2;
    public const int NoDevice = 3;

    public static string[] Arguments(bool enabled) => [Switch, enabled ? "on" : "off"];

    /// <summary>
    /// Runs the command if <paramref name="args"/> is it, and returns its exit
    /// code; null otherwise, so normal startup carries on.
    /// </summary>
    /// <param name="find">The card's device instance id, or null when there is none.</param>
    /// <param name="setEnabled">Turns it on or off; returns Windows' exit code (0 done, 3010 after a restart).</param>
    public static int? TryRun(string[] args, Func<string?> find, Func<string, bool, int> setEnabled)
    {
        if (args is not [Switch, var state] || state is not ("on" or "off"))
            return null;

        var enabled = state == "on";

        try
        {
            if (find() is not { } id)
            {
                AppLog.Info("No dedicated GPU to turn on or off.");
                return NoDevice;
            }

            var code = setEnabled(id, enabled);
            AppLog.Info($"Dedicated GPU turned {state} (elevated): Windows answered {code}.");

            return code switch
            {
                0 => Success,
                Platform.DedicatedGpuDevice.RestartNeeded => RestartNeeded,
                _ => Failed
            };
        }
        catch (Exception exception)
        {
            AppLog.Error($"The elevated dedicated GPU command ({state}) failed.", exception);
            return Failed;
        }
    }
}
