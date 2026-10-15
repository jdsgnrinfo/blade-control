using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// Puts a downloaded version in the app's own folder while the app runs.
/// Windows will not overwrite a program that is running, or the libraries it
/// has loaded, but it lets them be renamed: each file is renamed to ".old",
/// then the new one is copied in its place. The next start deletes the ".old"
/// files. If anything fails half way, the files already swapped go back, so
/// the folder is never left with half a version.
/// </summary>
internal static class UpdateInstaller
{
    public const string OldSuffix = ".old";
    private const string ExeName = "BladeControl.exe";

    /// <summary>
    /// Whether this copy can update itself: the portable build, one exe beside
    /// its native libraries (a build from source has BladeControl.dll beside
    /// it, and is updated from source), in a folder it may write to.
    /// </summary>
    public static bool CanInstallInto(string appFolder)
    {
        if (!File.Exists(Path.Combine(appFolder, ExeName)) || File.Exists(Path.Combine(appFolder, "BladeControl.dll")))
            return false;

        try
        {
            var probe = Path.Combine(appFolder, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The folder holding BladeControl.exe in an unpacked zip: its top or one folder down.</summary>
    public static string? FindAppFolder(string unpacked)
    {
        if (File.Exists(Path.Combine(unpacked, ExeName)))
            return unpacked;

        return Directory.GetDirectories(unpacked).FirstOrDefault(folder => File.Exists(Path.Combine(folder, ExeName)));
    }

    /// <summary>Copies every file of <paramref name="newFolder"/> into <paramref name="appFolder"/>, keeping the replaced ones as ".old".</summary>
    public static void Install(string newFolder, string appFolder)
    {
        var swapped = new List<(string Target, bool HadOld)>();

        try
        {
            foreach (var source in Directory.GetFiles(newFolder, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(appFolder, Path.GetRelativePath(newFolder, source));
                var old = target + OldSuffix;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(old))
                    File.Delete(old);

                var hadOld = File.Exists(target);

                if (hadOld)
                    File.Move(target, old);

                swapped.Add((target, hadOld));
                File.Copy(source, target);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not put the update in place; putting the old files back.", exception);
            Undo(swapped);
            throw;
        }

        AppLog.Info($"Update put in place: {swapped.Count} files in {appFolder}.");
    }

    /// <summary>At startup: deletes the files a previous update left as ".old". Those still in use stay for the next time.</summary>
    public static void CleanUp(string appFolder)
    {
        try
        {
            foreach (var old in Directory.GetFiles(appFolder, "*" + OldSuffix, SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Still loaded by a copy that is closing; next start.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not look for the files an update left.", exception);
        }
    }

    private static void Undo(List<(string Target, bool HadOld)> swapped)
    {
        foreach (var (target, hadOld) in Enumerable.Reverse(swapped))
        {
            try
            {
                if (File.Exists(target))
                    File.Delete(target);

                if (hadOld)
                    File.Move(target + OldSuffix, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Error($"Could not put {target} back.", exception);
            }
        }
    }
}
