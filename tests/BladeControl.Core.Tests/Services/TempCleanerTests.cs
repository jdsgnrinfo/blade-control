using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class TempCleanerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BladeControl.Core.Tests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _old = DateTime.UtcNow.AddDays(-3);

    public TempCleanerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        foreach (var file in new DirectoryInfo(_folder).EnumerateFiles("*", SearchOption.AllDirectories))
            file.IsReadOnly = false;

        Directory.Delete(_folder, recursive: true);
    }

    private string Write(string name, int bytes, bool old)
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);

        if (old)
        {
            File.SetCreationTimeUtc(path, _old);
            File.SetLastWriteTimeUtc(path, _old);
        }

        return path;
    }

    [Fact]
    public void Scan_CountsOnlyFilesOverADayOld()
    {
        Write("old.tmp", 100, old: true);
        Write(@"sub\old.log", 50, old: true);
        Write("new.tmp", 999, old: false);

        Assert.Equal(new TempFilesResult(2, 150), new TempCleaner(_folder).Scan());
    }

    [Fact]
    public void Clean_RemovesOldFiles_AndKeepsNewOnes()
    {
        var old = Write("old.tmp", 100, old: true);
        var recent = Write("new.tmp", 10, old: false);

        var result = new TempCleaner(_folder).Clean();

        Assert.Equal(1, result.Files);
        Assert.Equal(100, result.Bytes);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Clean_LeavesAFileAProgramHoldsOpen()
    {
        var held = Write("held.tmp", 10, old: true);

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = new TempCleaner(_folder).Clean();

            Assert.Equal(0, result.Files);
            Assert.Equal(1, result.Skipped);
        }

        Assert.True(File.Exists(held));
    }

    [Fact]
    public void Clean_LeavesReadOnlyFiles()
    {
        var path = Write("locked.tmp", 10, old: true);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var result = new TempCleaner(_folder).Clean();

        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Clean_RemovesOldFoldersLeftEmpty_AndKeepsTheFolderItself()
    {
        Write(@"empty\old.tmp", 10, old: true);
        Directory.SetCreationTimeUtc(Path.Combine(_folder, "empty"), _old);

        new TempCleaner(_folder).Clean();

        Assert.False(Directory.Exists(Path.Combine(_folder, "empty")));
        Assert.True(Directory.Exists(_folder));
    }

    [Fact]
    public void Clean_KeepsANewEmptyFolder()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "just made"));

        new TempCleaner(_folder).Clean();

        Assert.True(Directory.Exists(Path.Combine(_folder, "just made")));
    }

    [Fact]
    public void AMissingFolder_FindsNothing() =>
        Assert.Equal(new TempFilesResult(0, 0), new TempCleaner(Path.Combine(_folder, "missing")).Scan());

    [Fact]
    public void WithNoMinimumAge_EveryFileInEveryFolderGoes_AndTheFoldersStay()
    {
        var first = Path.Combine(_folder, "DXCache");
        var second = Path.Combine(_folder, "GLCache");
        Write(@"DXCache\new.bin", 100, old: false);
        Write(@"GLCache\sub\old.bin", 50, old: true);
        var cleaner = new TempCleaner([first, second, Path.Combine(_folder, "missing")], TimeSpan.Zero);

        Assert.Equal(new TempFilesResult(2, 150), cleaner.Scan());

        var result = cleaner.Clean();

        Assert.Equal(new TempFilesResult(2, 150), result);
        Assert.False(Directory.Exists(Path.Combine(second, "sub")));
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void AnyFolderExists_OnlyWhenOneIsThere()
    {
        Assert.True(new TempCleaner([Path.Combine(_folder, "missing"), _folder], TimeSpan.Zero).AnyFolderExists);
        Assert.False(new TempCleaner([Path.Combine(_folder, "missing")], TimeSpan.Zero).AnyFolderExists);
    }
}
