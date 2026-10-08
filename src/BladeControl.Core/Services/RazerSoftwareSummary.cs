using System.Text;
using BladeControl.Core.Localization;

namespace BladeControl.Core.Services;

/// <summary>
/// The plain-language breakdown behind the number in the Razer row, shown when
/// the number is hovered: what is counted, what starts at login, and when it
/// was last checked (so a stale number is easy to spot).
/// </summary>
internal static class RazerSoftwareSummary
{
    public static string Describe(RazerSoftwareStatus status, DateTime checkedAt)
    {
        var text = new StringBuilder();

        text.AppendLine(L.F("{0} running = {1} services + {2} Razer programs", status.Running, status.Services.Running, status.RunningApps.Count));
        text.AppendLine();
        text.AppendLine(L.F("Services: {0} of {1} running", status.Services.Running, status.Services.Total));

        var notDisabled = status.ServicesToStop.Count;

        if (notDisabled > 0 && status.Services.Running == 0)
            text.AppendLine(L.F("  ({0} not yet disabled)", notDisabled));

        if (status.RunningApps.Count == 0)
        {
            text.AppendLine(L.T("Programs: none running"));
        }
        else
        {
            text.AppendLine(L.T("Programs running:"));

            foreach (var name in status.RunningApps)
                text.AppendLine($"  {name}");
        }

        text.AppendLine(LoginLine(status));
        text.AppendLine();
        text.Append(L.F("Checked at {0}", checkedAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));

        return text.ToString();
    }

    private static string LoginLine(RazerSoftwareStatus status)
    {
        if (status.LoginEntries.Count == 0)
            return L.T("Start at login: no Razer entry found");

        var enabled = status.LoginEntries.Where(entry => entry.IsEnabled).Select(entry => entry.Name).ToList();

        return enabled.Count > 0
            ? L.F("Start at login: ON ({0})", string.Join(", ", enabled))
            : L.T("Start at login: off");
    }
}
