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

    /// <summary>Equalizer APO's official page, which the Audio page's equalizer needs installed.</summary>
    public static void OpenEqualizerApoDownload() => Open(EqualizerApo.DownloadUrl);

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
