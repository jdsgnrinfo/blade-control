namespace BladeControl.Core.Services;

/// <summary>What a scan found, or a clean removed.</summary>
internal sealed record TempFilesResult(int Files, long Bytes, int Skipped = 0);

/// <summary>
/// Clears folders of throwaway files: the user's temporary folder or the
/// graphics driver's shader caches, everything in them, or only files over an
/// age (Windows Update's downloads). Only files Windows lets go of are
/// removed, so one a running program holds open is left where it is. Links and
/// junctions are never followed, so nothing outside the folders is ever
/// touched, and subfolders named to be kept are left whole. The folders
/// themselves always stay.
/// </summary>
internal sealed class TempCleaner
{
    /// <summary>Where only old files go: files newer than this are left alone.</summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(1);

    /// <summary>
    /// In the temporary folder, where single-file .NET apps (Blade Control's
    /// portable build among them) unpack themselves to run: a file not yet
    /// loaded is not held open, and taking it away would break the app later.
    /// </summary>
    public const string DotNetUnpacked = ".net";

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Never into a link or junction, which may point anywhere.
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private readonly IReadOnlyList<string> _folders;
    private readonly TimeSpan _minimumAge;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyList<string> _kept;

    /// <summary>One folder, cleared of files over a day old.</summary>
    public TempCleaner(string folder, TimeProvider? clock = null)
        : this([folder], MinimumAge, clock)
    {
    }

    /// <param name="minimumAge">Files newer than this are left alone; zero clears every file that is not in use.</param>
    /// <param name="keep">Names of subfolders, right under each folder, left whole.</param>
    public TempCleaner(IReadOnlyList<string> folders, TimeSpan minimumAge, TimeProvider? clock = null, IReadOnlyList<string>? keep = null)
    {
        _folders = folders;
        _minimumAge = minimumAge;
        _clock = clock ?? TimeProvider.System;
        _kept = [.. folders.SelectMany(folder => (keep ?? []).Select(name => Path.Combine(folder, name) + Path.DirectorySeparatorChar))];
    }

    /// <summary>
    /// The user's temporary folder, %TEMP%: every file no program holds open,
    /// however new, since a session leaves its leftovers long before a day is
    /// up. What .NET apps unpacked there to run stays.
    /// </summary>
    public static TempCleaner ForCurrentUser() =>
        new([Path.GetTempPath()], TimeSpan.Zero, keep: [DotNetUnpacked]);

    private bool IsKept(string path) =>
        _kept.Any(kept => path.StartsWith(kept, StringComparison.OrdinalIgnoreCase)
            || (path + Path.DirectorySeparatorChar).Equals(kept, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The NVIDIA driver's shader caches for the current user, DirectX and
    /// OpenGL, where current and older drivers keep them. The driver builds
    /// them again as games need them.
    /// </summary>
    public static TempCleaner ForNvidiaShaderCache()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localLow = Path.Combine(Path.GetDirectoryName(local)!, "LocalLow");

        return new TempCleaner(
        [
            Path.Combine(local, "NVIDIA", "DXCache"),
            Path.Combine(local, "NVIDIA", "GLCache"),
            Path.Combine(localLow, "NVIDIA", "PerDriverVersion", "DXCache"),
            Path.Combine(localLow, "NVIDIA", "PerDriverVersion", "GLCache")
        ], TimeSpan.Zero);
    }

    /// <summary>True when at least one of the folders is there.</summary>
    public bool AnyFolderExists => _folders.Any(Directory.Exists);

    /// <summary>Counts what a clean would try to remove.</summary>
    public TempFilesResult Scan()
    {
        var files = 0;
        var bytes = 0L;

        foreach (var file in OldFiles())
        {
            files++;
            bytes += file.Length;
        }

        return new TempFilesResult(files, bytes);
    }

    /// <summary>Removes the old files it can, then the folders left empty.</summary>
    public TempFilesResult Clean()
    {
        var files = 0;
        var bytes = 0L;
        var skipped = 0;

        foreach (var file in OldFiles().ToList())
        {
            try
            {
                var length = file.Length;

                if (file.IsReadOnly)
                {
                    skipped++;
                    continue;
                }

                file.Delete();
                files++;
                bytes += length;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // Its program took it away meanwhile: nothing left to do, nothing left behind.
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Held open by a running program, or not the user's: it stays.
                skipped++;
            }
        }

        foreach (var folder in _folders)
            RemoveEmptyFolders(folder);

        return new TempFilesResult(files, bytes, skipped);
    }

    private IEnumerable<FileInfo> OldFiles()
    {
        var cutoff = _clock.GetUtcNow().UtcDateTime - _minimumAge;

        foreach (var folder in _folders)
        {
            if (!Directory.Exists(folder))
                continue;

            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Walk))
            {
                if (IsKept(file.FullName))
                    continue;

                if (_minimumAge <= TimeSpan.Zero || (file.LastWriteTimeUtc < cutoff && file.CreationTimeUtc < cutoff))
                    yield return file;
            }
        }
    }

    // Deepest first, so a folder emptied by its children's removal goes too.
    // Only folders older than the minimum age: a program may have just made one to use.
    private void RemoveEmptyFolders(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        var cutoff = _clock.GetUtcNow().UtcDateTime - _minimumAge;

        var folders = new DirectoryInfo(folder)
            .EnumerateDirectories("*", Walk)
            .Where(each => !IsKept(each.FullName))
            .Where(each => _minimumAge <= TimeSpan.Zero || each.CreationTimeUtc < cutoff)
            .OrderByDescending(each => each.FullName.Length)
            .ToList();

        foreach (var each in folders)
        {
            try
            {
                if (!each.EnumerateFileSystemInfos().Any())
                    each.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
