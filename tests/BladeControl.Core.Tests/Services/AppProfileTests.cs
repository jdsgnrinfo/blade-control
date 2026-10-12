using System.Text;
using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class AppProfileTests
{
    private static readonly DateTime Morning = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

    private static RunningProgram Running(string path, int minutesLater = 0) =>
        new(1000 + minutesLater, path, Morning.AddMinutes(minutesLater));

    // ===== Which profile is on =====

    [Fact]
    public void AProgramProfile_MatchesThatProgramOnly()
    {
        Assert.True(AppProfileMatcher.Matches(@"C:\Games\Elden Ring\eldenring.exe", @"c:\games\elden ring\ELDENRING.EXE"));
        Assert.False(AppProfileMatcher.Matches(@"C:\Games\Elden Ring\eldenring.exe", @"C:\Games\Elden Ring\start_protected_game.exe"));
    }

    [Fact]
    public void AFolderProfile_MatchesAnythingRunFromInsideIt()
    {
        const string folder = @"C:\Program Files (x86)\Steam\steamapps\common\Hogwarts Legacy";

        Assert.True(AppProfileMatcher.Matches(folder, folder + @"\Phoenix\Binaries\Win64\HogwartsLegacy.exe"));
        Assert.True(AppProfileMatcher.Matches(folder + @"\", folder + @"\HogwartsLegacy.exe"));
        Assert.False(AppProfileMatcher.Matches(folder, @"C:\Program Files (x86)\Steam\steamapps\common\Hogwarts Legacy 2\game.exe"));
        Assert.False(AppProfileMatcher.Matches(folder, @"C:\Program Files (x86)\Steam\steam.exe"));
    }

    [Fact]
    public void NoProfile_WhenNoneOfTheirAppsRuns()
    {
        var profiles = new[] { new AppProfile("Game", @"C:\Games\game.exe", Mode: PerformanceMode.Custom) };

        Assert.Null(AppProfileMatcher.Find([Running(@"C:\Windows\explorer.exe")], profiles));
    }

    [Fact]
    public void WithTwoRunning_TheAppStartedLastWins()
    {
        var first = new AppProfile("First", @"C:\Games\first.exe", Mode: PerformanceMode.Custom);
        var second = new AppProfile("Second", @"C:\Games\second.exe", Mode: PerformanceMode.Silent);
        AppProfile[] profiles = [first, second];

        Assert.Equal(second, AppProfileMatcher.Find([Running(@"C:\Games\first.exe", 0), Running(@"C:\Games\second.exe", 5)], profiles));
        Assert.Equal(first, AppProfileMatcher.Find([Running(@"C:\Games\second.exe", 0), Running(@"C:\Games\first.exe", 5)], profiles));
    }

    [Fact]
    public void AProfileSwitchedOff_OrChangingNothing_IsNeverOn()
    {
        AppProfile[] profiles =
        [
            new("Paused", @"C:\Games\paused.exe", Enabled: false, Mode: PerformanceMode.Custom),
            new("Empty", @"C:\Games\empty.exe")
        ];

        Assert.Null(AppProfileMatcher.Find([Running(@"C:\Games\paused.exe"), Running(@"C:\Games\empty.exe")], profiles));
    }

    [Fact]
    public void CustomWithoutLevels_UsesHighForBoth_AndOtherModesCarryNone()
    {
        Assert.Equal(new PowerProfile(PerformanceMode.Custom, CpuBoost.High, GpuBoost.High),
            new AppProfile("Game", "game.exe", Mode: PerformanceMode.Custom).Performance);
        Assert.Equal(new PowerProfile(PerformanceMode.Silent),
            new AppProfile("Game", "game.exe", Mode: PerformanceMode.Silent, Cpu: CpuBoost.Boost).Performance);
        Assert.Null(new AppProfile("Game", "game.exe", RefreshHz: 165).Performance);
    }

    [Fact]
    public void TheSummary_SaysWhatTheProfileChanges()
    {
        var profile = new AppProfile("Game", "game.exe", Mode: PerformanceMode.Custom, MaxFan: true, RefreshHz: 165,
            ColorProfile: "Cool", Keyboard: KeyboardEffect.Off, KeyboardBrightness: 50);

        Assert.Equal("Custom · Max fan · 165 Hz · Neutral · Keyboard off", AppProfileText.Summary(profile));
        Assert.Equal("No changes yet", AppProfileText.Summary(new AppProfile("Game", "game.exe")));
        Assert.Equal("Keyboard: Spectrum · Keyboard 75%",
            AppProfileText.Summary(new AppProfile("Game", "game.exe", Keyboard: KeyboardEffect.Spectrum, KeyboardBrightness: 75)));
    }

    [Fact]
    public void OnlyStaticAndBreathing_TakeAColor()
    {
        Assert.True(AppProfile.TakesColor(KeyboardEffect.StaticGreen));
        Assert.True(AppProfile.TakesColor(KeyboardEffect.Breathing));
        Assert.False(AppProfile.TakesColor(KeyboardEffect.Spectrum));
        Assert.False(AppProfile.TakesColor(KeyboardEffect.Wave));
        Assert.False(AppProfile.TakesColor(null));
    }

    [Fact]
    public void AProfileWithAKeyboardColor_IsSavedAndReadBackTheSame()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BladeControlTests", Guid.NewGuid().ToString("N"));
        var service = new SettingsService(folder);
        var profile = new AppProfile("Game", @"C:\Games\game.exe", Keyboard: KeyboardEffect.Breathing, KeyboardColor: new RgbColor(0xFF, 0x80, 0x00));

        try
        {
            service.Save(new AppSettings(AppProfiles: [profile]));

            Assert.Equal(profile, Assert.Single(service.Load().AppProfiles!));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ===== Reading the launchers =====

    [Fact]
    public void Steam_ListsEveryLibraryFolder()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameLibrary.SteamLibraries(vdf));
    }

    [Fact]
    public void Steam_AManifestGivesTheGamesNameAndFolder()
    {
        const string acf = """
            "AppState"
            {
            	"appid"		"1222680"
            	"name"		"Need for Speed™ Heat "
            	"installdir"		"Need for Speed Heat"
            }
            """;

        var game = GameLibrary.SteamGame(acf, @"D:\SteamLibrary");

        Assert.NotNull(game);
        Assert.Equal("Need for Speed Heat", game.Name);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Need for Speed Heat", game.Folder);
        Assert.Equal(AppSource.Steam, game.Source);
    }

    [Fact]
    public void Steam_ItsOwnToolsAreNotGames()
    {
        const string acf = """
            "AppState"
            {
            	"appid"		"228980"
            	"name"		"Steamworks Common Redistributables"
            	"installdir"		"Steamworks Shared"
            }
            """;

        Assert.Null(GameLibrary.SteamGame(acf, @"C:\Steam"));
    }

    [Fact]
    public void Epic_AManifestGivesTheGameAndItsProgram()
    {
        const string json = """
            {
              "DisplayName": "Fortnite",
              "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite",
              "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe",
              "AppCategories": [ "public", "games", "applications" ]
            }
            """;

        var game = GameLibrary.EpicGame(json);

        Assert.NotNull(game);
        Assert.Equal("Fortnite", game.Name);
        Assert.Equal(@"C:\Program Files\Epic Games\Fortnite", game.Folder);
        Assert.Equal(@"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe", game.Program);
    }

    [Fact]
    public void Epic_TheEngineAndPluginsAreNotGames()
    {
        const string json = """
            { "DisplayName": "Unreal Engine", "InstallLocation": "C:\\UE_5.4", "AppCategories": [ "engines" ] }
            """;

        Assert.Null(GameLibrary.EpicGame(json));
    }

    [Fact]
    public void Xbox_TheGamingRootNamesTheGamesFolder()
    {
        byte[] marker = [.. "RGBX"u8.ToArray(), 1, 0, 0, 0, .. Encoding.Unicode.GetBytes("\\XboxGames\0")];

        Assert.Equal(["XboxGames"], GameLibrary.GamingRootFolders(marker));
        Assert.Equal(["XboxGames"], GameLibrary.GamingRootFolders([1, 2, 3]));
    }

    [Fact]
    public void Xbox_TheConfigGivesTheNameAndProgram_ButNotAResourceName()
    {
        const string config = """
            <Game configVersion="1">
              <ExecutableList><Executable Name="Forza.exe" Id="Game" /></ExecutableList>
              <ShellVisuals DefaultDisplayName="Forza Horizon 5" />
            </Game>
            """;

        Assert.Equal(("Forza Horizon 5", "Forza.exe"), GameLibrary.XboxConfig(config));
        Assert.Null(GameLibrary.XboxConfig("""<ShellVisuals DefaultDisplayName="ms-resource:Name" />""").Name);
    }

    [Fact]
    public void BattleNet_BlizzardsGamesCount_ButNotBattleNetItself()
    {
        var game = GameLibrary.BattleNetGame("Diablo IV", "Blizzard Entertainment", @"C:\Program Files (x86)\Diablo IV\");

        Assert.NotNull(game);
        Assert.Equal(@"C:\Program Files (x86)\Diablo IV", game.Folder);
        Assert.Null(GameLibrary.BattleNetGame("Battle.net", "Blizzard Entertainment", @"C:\Program Files (x86)\Battle.net"));
        Assert.Null(GameLibrary.BattleNetGame("Some Game", "Another Publisher", @"C:\Games\Some"));
    }
}
