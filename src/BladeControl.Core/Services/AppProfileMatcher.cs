using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>A program running now: its full path and when it started.</summary>
internal sealed record RunningProgram(int Id, string Path, DateTime Started);

/// <summary>
/// Which app profile should be on, given what is running: of the profiles
/// switched on, the one whose app was started last. Closing it hands over to
/// the one started before, if it is still running.
/// </summary>
internal static class AppProfileMatcher
{
    /// <summary>The profile to apply now, or null when none of their apps is running.</summary>
    public static AppProfile? Find(IEnumerable<RunningProgram> running, IReadOnlyList<AppProfile> profiles)
    {
        var enabled = profiles.Where(profile => profile.Enabled && profile.ChangesAnything).ToList();

        if (enabled.Count == 0)
            return null;

        AppProfile? found = null;
        var latest = DateTime.MinValue;

        foreach (var program in running)
        {
            if (program.Started < latest)
                continue;

            foreach (var profile in enabled)
            {
                if (!Matches(profile.Path, program.Path))
                    continue;

                found = profile;
                latest = program.Started;
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// Whether a program is the profile's: the same .exe, or anything run from
    /// inside the profile's folder (a game found in a launcher's library).
    /// </summary>
    public static bool Matches(string profilePath, string programPath)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || string.IsNullOrWhiteSpace(programPath))
            return false;

        if (IsProgram(profilePath))
            return string.Equals(Normalize(profilePath), Normalize(programPath), StringComparison.OrdinalIgnoreCase);

        var folder = Normalize(profilePath).TrimEnd('\\') + "\\";
        return Normalize(programPath).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A path to a program (an .exe), as opposed to a folder.</summary>
    public static bool IsProgram(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('/', '\\').Trim();
}
