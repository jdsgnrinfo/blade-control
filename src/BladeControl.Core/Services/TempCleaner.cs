namespace BladeControl.Core.Services;

/// <summary>What a scan found, or a clean removed.</summary>
internal sealed record TempFilesResult(int Files, long Bytes, int Skipped = 0);

/// <summary>
/// Clears folders of throwaway files: the user's temporary folder (only files
/// older than a day, so nothing a program set down a moment ago goes) or the
/// graphics driver's shader caches (everything, as the driver rebuilds them).
/// Only files Windows lets go of are removed, so one a running program holds
/// open is left where it is. Links and junctions are never followed, so
/// nothing outside the folders is ever touched. The folders themselves always stay.
/// </summary>
internal sealed class TempCleaner
{
    /// <summary>In the temporary folder, files newer than this are left alone.</summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(1);

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

    /// <summary>One temporary folder, cleared of files over a day old.</summary>
    public TempCleaner(string folder, TimeProvider? clock = null)
        : this([folder], MinimumAge, clock)
    {
    }

    /// <param name="minimumAge">Files newer than this are left alone; zero clears every file that is not in use.</param>
    public TempCleaner(IReadOnlyList<string> folders, TimeSpan minimumAge, TimeProvider? clock = null)
    {
        _folders = folders;
        _minimumAge = minimumAge;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The user's temporary folder, %TEMP%.</summary>
    public static TempCleaner ForCurrentUser() => new(Path.GetTempPath());

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
