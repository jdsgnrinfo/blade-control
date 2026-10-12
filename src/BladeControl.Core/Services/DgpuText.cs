using System.Text;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>Plain-language text for freeing up the dedicated GPU: the question, and what to say when nothing was closed.</summary>
internal static class DgpuText
{
    /// <summary>The question, listing exactly what "yes" will ask to close.</summary>
    public static string BuildConfirmation(IReadOnlyList<DgpuApp> apps)
    {
        var text = new StringBuilder();

        text.AppendLine(L.T("These apps are keeping the dedicated GPU awake, which drains the battery:"));
        text.AppendLine();

        foreach (var group in apps.GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"  \u2022 {Describe(group.Key, group.ToList())}");

        text.AppendLine();
        text.Append(L.T("Ask them to close? Each one can still ask you to save your work first."));

        return text.ToString();
    }

    /// <summary>
    /// What to tell the user after they asked to free up the GPU themselves, or
    /// null when the outcome speaks for itself (apps closing, or "not now").
    /// </summary>
    public static string? DescribeOutcome(DgpuFreeUpOutcome outcome) => outcome switch
    {
        DgpuFreeUpOutcome.NoDedicatedGpu =>
            L.T("No dedicated GPU was found, so there is nothing to free up."),
        DgpuFreeUpOutcome.ExternalDisplay =>
            L.T("An external display is connected (or Windows could not say). It is driven by the dedicated GPU, so the GPU stays on whatever is closed. Disconnect it and try again."),
        DgpuFreeUpOutcome.NothingToClose =>
            L.T("No apps that can be closed are using the dedicated GPU. Windows, drivers, terminals, editors and background helpers are left alone."),
        DgpuFreeUpOutcome.ConditionsChanged =>
            L.T("Things changed while you were deciding, so nothing was closed. Try again."),
        DgpuFreeUpOutcome.Busy =>
            L.T("Already checking the dedicated GPU."),
        DgpuFreeUpOutcome.Failed =>
            L.T("Could not check the dedicated GPU. Details are in the log."),
        _ => null
    };

    /// <summary>The Graphics card's live line: whether the GPU is awake now. Empty when Windows will not say.</summary>
    public static string StateLine(bool? awake) => awake switch
    {
        true => L.T("Awake now"),
        false => L.T("Asleep now: it uses almost no power"),
        _ => string.Empty
    };

    /// <summary>
    /// The apps using the GPU, by name, those holding the most video memory
    /// first. Only the core of Windows is left out: terminals, editors and
    /// helpers that are never closed still keep the GPU awake, so they are
    /// named. At most three by name.
    /// </summary>
    public static string UsersLine(IReadOnlyList<DgpuApp> apps)
    {
        var names = apps
            .Where(app => !DgpuAppSelector.IsPartOfWindows(app.Name))
            .GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(app => app.DedicatedBytes))
            .Select(group => group.Key)
            .ToList();

        return names.Count switch
        {
            0 => L.T("No app is using it."),
            <= MaxNamed => L.F("Used by: {0}", string.Join(", ", names)),
            _ => L.F("Used by: {0} and {1} more", string.Join(", ", names.Take(MaxNamed)), names.Count - MaxNamed)
        };
    }

    private const int MaxNamed = 3;

    private static string Describe(string name, IReadOnlyList<DgpuApp> processes)
    {
        var megabytes = (long)Math.Round(processes.Sum(app => app.DedicatedBytes) / (1024.0 * 1024.0));
        var count = processes.Count > 1 ? L.F("{0} instances, ", processes.Count) : string.Empty;

        return $"{name} ({count}{megabytes} MB)";
    }
}
