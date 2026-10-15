using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class AppUpdatesTests
{
    private const string Download = "https://github.com/jdsgnrinfo/blade-control/releases/download/v0.1.5/";

    private static string Release(string tag = "v0.1.5", string assets = "", bool draft = false, bool prerelease = false) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/jdsgnrinfo/blade-control/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "body": "  What changed  ",
          "assets": [{{assets}}]
        }
        """;

    private static string Asset(string name, string url, long size = 100) =>
        $$"""{ "name": "{{name}}", "browser_download_url": "{{url}}", "size": {{size}} }""";

    private static readonly string ZipAndChecksum =
        Asset("BladeControl-v0.1.5-win-x64.zip", Download + "BladeControl-v0.1.5-win-x64.zip", 75_000_000) + "," +
        Asset("BladeControl-v0.1.5-win-x64.zip.sha256", Download + "BladeControl-v0.1.5-win-x64.zip.sha256");

    [Fact]
    public void AReleaseWithItsZipAndChecksum_CanBeInstalled()
    {
        var release = ReleaseParser.Parse(Release(assets: ZipAndChecksum))!;

        Assert.Equal(new Version(0, 1, 5), release.Version);
        Assert.Equal("What changed", release.Notes);
        Assert.Equal(Download + "BladeControl-v0.1.5-win-x64.zip", release.ZipUrl);
        Assert.Equal(Download + "BladeControl-v0.1.5-win-x64.zip.sha256", release.ChecksumUrl);
        Assert.Equal(75_000_000, release.ZipBytes);
        Assert.True(release.CanInstall);
    }

    [Fact]
    public void WithoutTheChecksum_ItGoesThroughThePage()
    {
        var release = ReleaseParser.Parse(Release(assets: Asset("BladeControl-v0.1.5-win-x64.zip", Download + "BladeControl-v0.1.5-win-x64.zip")))!;

        Assert.False(release.CanInstall);
        Assert.StartsWith("https://github.com/jdsgnrinfo/blade-control/releases", release.PageUrl);
    }

    [Fact]
    public void FilesFromAnywhereElse_AreIgnored()
    {
        var elsewhere = Asset("BladeControl-v0.1.5-win-x64.zip", "https://example.com/BladeControl-v0.1.5-win-x64.zip") + "," +
            Asset("BladeControl-v0.1.5-win-x64.zip.sha256", "https://example.com/BladeControl-v0.1.5-win-x64.zip.sha256");

        Assert.False(ReleaseParser.Parse(Release(assets: elsewhere))!.CanInstall);
    }

    [Fact]
    public void OtherZips_AreNotTakenForTheApp() =>
        Assert.Null(ReleaseParser.Parse(Release(assets: Asset("Source.zip", Download + "Source.zip")))!.ZipUrl);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DraftsAndPreReleases_AreNotOffered(bool draft, bool prerelease) =>
        Assert.Null(ReleaseParser.Parse(Release(draft: draft, prerelease: prerelease)));

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "tag_name": "latest" }""")]
    public void AnswersThatAreNotAReleaseGiveNothing(string json) =>
        Assert.Null(ReleaseParser.Parse(json));

    [Theory]
    [InlineData("v0.1.5", "0.1.5")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("V1.0", "1.0.0")]
    public void TagsReadAsVersions(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), ReleaseParser.ParseVersion(tag));

    [Theory]
    [InlineData("0.1.5", "0.1.4.0", true)]
    [InlineData("0.1.4", "0.1.4.0", false)]
    [InlineData("0.1.3", "0.1.4.0", false)]
    [InlineData("0.2.0", "0.1.9.0", true)]
    public void OnlyALaterVersionIsNewer(string release, string current, bool newer) =>
        Assert.Equal(newer, ReleaseParser.IsNewer(Version.Parse(release), Version.Parse(current)));

    [Fact]
    public void TheChecksumIsReadWithOrWithoutTheFileName()
    {
        var hash = new string('a', 64);

        Assert.Equal(hash.ToUpperInvariant(), ReleaseParser.ParseChecksum($"{hash}  BladeControl-v0.1.5-win-x64.zip"));
        Assert.Equal(hash.ToUpperInvariant(), ReleaseParser.ParseChecksum(hash + "\r\n"));
        Assert.Null(ReleaseParser.ParseChecksum("not a checksum"));
    }
}

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bc-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _app;
    private readonly string _update;

    public UpdateInstallerTests()
    {
        _app = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        _update = Directory.CreateDirectory(Path.Combine(_root, "files", "BladeControl")).FullName;
        File.WriteAllText(Path.Combine(_app, "BladeControl.exe"), "old exe");
        File.WriteAllText(Path.Combine(_app, "wpfgfx_cor3.dll"), "old dll");
        File.WriteAllText(Path.Combine(_update, "BladeControl.exe"), "new exe");
        File.WriteAllText(Path.Combine(_update, "wpfgfx_cor3.dll"), "new dll");
        File.WriteAllText(Path.Combine(_update, "LICENSE.txt"), "license");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TheNewFilesTakeThePlaceOfTheOld_WhichAreKeptAsOld()
    {
        UpdateInstaller.Install(_update, _app);

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(_app, "BladeControl.exe")));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(_app, "BladeControl.exe.old")));
        Assert.Equal("license", File.ReadAllText(Path.Combine(_app, "LICENSE.txt")));
        Assert.False(File.Exists(Path.Combine(_app, "LICENSE.txt.old")));
    }

    [Fact]
    public void TheOldFilesGo_AtTheNextStart()
    {
        UpdateInstaller.Install(_update, _app);
        UpdateInstaller.CleanUp(_app);

        Assert.Empty(Directory.GetFiles(_app, "*.old"));
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(_app, "BladeControl.exe")));
    }

    [Fact]
    public void AFailureHalfWay_PutsEveryFileBack()
    {
        // A file that cannot be replaced: the copy over it fails while it is held open.
        File.WriteAllText(Path.Combine(_app, "LICENSE.txt"), "old license");
        using (File.Open(Path.Combine(_app, "LICENSE.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => UpdateInstaller.Install(_update, _app));

        Assert.Equal("old exe", File.ReadAllText(Path.Combine(_app, "BladeControl.exe")));
        Assert.Equal("old dll", File.ReadAllText(Path.Combine(_app, "wpfgfx_cor3.dll")));
        Assert.Equal("old license", File.ReadAllText(Path.Combine(_app, "LICENSE.txt")));
        Assert.Empty(Directory.GetFiles(_app, "*.old"));
    }

    [Fact]
    public void TheAppIsFoundAtTheZipsTopOrOneFolderDown()
    {
        Assert.Equal(_update, UpdateInstaller.FindAppFolder(Path.Combine(_root, "files")));
        Assert.Equal(_update, UpdateInstaller.FindAppFolder(_update));
        Assert.Null(UpdateInstaller.FindAppFolder(Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName));
    }

    [Fact]
    public void APortableFolder_CanBeUpdated_ButNotABuildFromSource()
    {
        Assert.True(UpdateInstaller.CanInstallInto(_app));

        File.WriteAllText(Path.Combine(_app, "BladeControl.dll"), "build");
        Assert.False(UpdateInstaller.CanInstallInto(_app));
    }
}
