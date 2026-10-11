using System.Diagnostics;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Platform;

/// <summary>Things the Settings window opens outside the app.</summary>
internal static class ExternalLinks
{
    /// <summary>Razer's support site: pick your laptop there to find its drivers and software.</summary>
    public const string RazerDriversUrl = "https://mysupport.razer.com/";

    public static void OpenRazerDrivers() => Open(RazerDriversUrl);

    /// <summary>This project on GitHub: its code, changes and releases.</summary>
    public const string ProjectUrl = "https://github.com/jdsgnrinfo/razer-helper";

    public static void OpenProjectPage() => Open(ProjectUrl);

    /// <summary>The author's PayPal.me page, for anyone who wants to buy them a coffee.</summary>
    public const string DonationUrl = "https://paypal.me/jotica96";

    public static void OpenDonation() => Open(DonationUrl);

    /// <summary>Windows' Display settings, where a secondary monitor's refresh rate is chosen.</summary>
    public static void OpenDisplaySettings() => Open("ms-settings:display");

    public static void OpenLogFolder() =>
        Open(Path.GetDirectoryName(AppLog.LogFilePath)!);

    // Never throws: a missing browser or folder must not take the app down.
    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not open {target}.", exception);
        }
    }
}
