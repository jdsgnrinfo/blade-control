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
    public const string ProjectUrl = "https://github.com/jdsgnrinfo/blade-control";

    public static void OpenProjectPage() => Open(ProjectUrl);

    /// <summary>
    /// A new issue on GitHub from the bug report form, with the fields it knows
    /// filled in (form field id to value: version, laptop, windows, bios). Only
    /// what the user sees and can change before sending; nothing personal.
    /// </summary>
    internal static string BugReportUrl(IEnumerable<KeyValuePair<string, string?>> fields)
    {
        var query = string.Join("&", fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value!)}"));

        return $"{ProjectUrl}/issues/new?template=bug_report.yml" + (query.Length > 0 ? "&" + query : string.Empty);
    }

    public static void OpenBugReport(IEnumerable<KeyValuePair<string, string?>> fields) => Open(BugReportUrl(fields));

    /// <summary>A release's page; anything not under this project's releases opens the list of releases instead.</summary>
    public static void OpenReleasePage(string? url) =>
        Open(url is not null && url.StartsWith(ProjectUrl + "/releases", StringComparison.OrdinalIgnoreCase) ? url : ProjectUrl + "/releases");

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
