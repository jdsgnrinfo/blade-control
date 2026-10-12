using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BladeControl.Core.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);
    }

    [Fact]
    public void Load_WithNoFile_ReturnsEmptySettings()
    {
        Assert.Equal(new AppSettings(), new SettingsService(_directory).Load());
    }

    [Fact]
    public void Load_AnUnreadableFile_KeepsACopyBesideIt_BeforeTheDefaultsCanReplaceIt()
    {
        WriteSettings("{ \"Language\": \"en\", broken");

        Assert.Equal(new AppSettings(), new SettingsService(_directory).Load());

        var copy = Assert.Single(Directory.GetFiles(_directory, "settings.json.unreadable-*"));
        Assert.Equal("{ \"Language\": \"en\", broken", File.ReadAllText(copy));
    }

    [Fact]
    public void Load_ReadsAnAppProfilesGameMode_ByName_AndAsTheTrueOrFalseOfAnEarlierVersion()
    {
        WriteSettings("""
            { "Language": "en", "AppProfiles": [
                { "Name": "Old on", "Path": "C:\\a.exe", "GameMode": true },
                { "Name": "Old off", "Path": "C:\\b.exe", "GameMode": false },
                { "Name": "New", "Path": "C:\\c.exe", "GameMode": "WindowsKeyAndAltTab" },
                { "Name": "Unknown", "Path": "C:\\d.exe", "GameMode": "Everything" } ] }
            """);

        var settings = new SettingsService(_directory).Load();

        Assert.Equal("en", settings.Language);
        Assert.Equal(
            [GameModeBlock.WindowsKey, null, GameModeBlock.WindowsKeyAndAltTab, null],
            settings.AppProfiles!.Select(profile => profile.GameMode));
    }

    [Fact]
    public void Save_ThenLoad_KeepsAnAppProfilesGameMode()
    {
        var service = new SettingsService(_directory);
        service.Save(new AppSettings(AppProfiles: [new AppProfile("Game", @"C:\game.exe", GameMode: GameModeBlock.WindowsKeyAltTabAndAltF4)]));

        Assert.Equal(GameModeBlock.WindowsKeyAltTabAndAltF4, Assert.Single(service.Load().AppProfiles!).GameMode);
        Assert.Contains("\"GameMode\": \"WindowsKeyAltTabAndAltF4\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsEverything()
    {
        var service = new SettingsService(_directory);
        var settings = new AppSettings(
            DisplayMode: "Auto",
            BatteryChargeLimit: 80,
            PluggedInProfile: new PowerProfile(PerformanceMode.Custom, CpuBoost.High, GpuBoost.Medium),
            OnBatteryProfile: new PowerProfile(PerformanceMode.Balanced));

        service.Save(settings);

        Assert.Equal(settings, service.Load());
    }

    [Fact]
    public void Load_FromAFileWrittenBeforeTheSettingsWindow_TurnsBothOptionsOn()
    {
        WriteSettings("""{ "DisplayMode": "Auto", "BatteryChargeLimit": 80 }""");

        var settings = new SettingsService(_directory).Load();

        Assert.True(settings.AutoSwitchProfiles);
        Assert.True(settings.HideWhenClickedAway);
    }

    [Fact]
    public void Load_FromAnOlderFile_LeavesClosingGpuAppsOff_WithNoExtraNeverCloseNames()
    {
        WriteSettings("""{ "DisplayMode": "Auto", "AutoSwitchProfiles": false }""");

        var settings = new SettingsService(_directory).Load();

        Assert.False(settings.CloseGpuAppsOnUnplug);
        Assert.Null(settings.NeverCloseApps);
    }

    [Fact]
    public void Save_ThenLoad_KeepsTheGpuAppOptions()
    {
        var service = new SettingsService(_directory);

        service.Save(new AppSettings(CloseGpuAppsOnUnplug: true, NeverCloseApps: ["blender", "gimp"]));
        var settings = service.Load();

        Assert.True(settings.CloseGpuAppsOnUnplug);
        Assert.Equal(["blender", "gimp"], Assert.IsType<string[]>(settings.NeverCloseApps));
    }

    [Fact]
    public void Load_FromAnOlderFile_LeavesAlwaysOnTopOff()
    {
        WriteSettings("""{ "DisplayMode": "Auto", "HideWhenClickedAway": false }""");

        var settings = new SettingsService(_directory).Load();

        Assert.False(settings.AlwaysOnTop);
    }

    [Fact]
    public void Load_FromAnOlderFile_LeavesTheModeShortcutsOn()
    {
        WriteSettings("""{ "DisplayMode": "Auto" }""");

        Assert.True(new SettingsService(_directory).Load().ProfileShortcuts);
    }

    [Fact]
    public void Save_ThenLoad_KeepsTheModeShortcutsOff()
    {
        var service = new SettingsService(_directory);

        service.Save(new AppSettings(ProfileShortcuts: false));

        Assert.False(service.Load().ProfileShortcuts);
    }

    [Fact]
    public void Load_FromAnOlderFile_HasNothingSavedForSilent()
    {
        WriteSettings("""{ "DisplayMode": "Auto" }""");

        var settings = new SettingsService(_directory).Load();

        Assert.Null(settings.CpuBoostBeforeSilent);
        Assert.Null(settings.CpuEfficiencyBeforeSilent);
    }

    [Fact]
    public void Load_AFileWithTheOldSilentSwitches_StillLoads_AndKeepsWhatWasSaved()
    {
        WriteSettings("""
            {
              "SilentWithoutTurbo": false,
              "SilentEfficiency": false,
              "CpuEfficiencyBeforeSilent": { "Scheme": "381b4222-f694-41f0-9685-ff5bb260df2e", "PluggedIn": 30, "OnBattery": 50 }
            }
            """);

        var settings = new SettingsService(_directory).Load();

        Assert.Equal(new SavedPlanValue(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), 30, 50), settings.CpuEfficiencyBeforeSilent);
    }

    [Fact]
    public void Save_ThenLoad_KeepsTheBoostSilentReplaced()
    {
        var service = new SettingsService(_directory);
        var saved = new SavedPlanValue(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), 2, 1);

        service.Save(new AppSettings(CpuBoostBeforeSilent: saved));

        Assert.Equal(saved, service.Load().CpuBoostBeforeSilent);
    }

    [Fact]
    public void Load_FromAnOlderFile_LeavesTheWindowSizeAutomatic()
    {
        WriteSettings("""{ "DisplayMode": "Auto" }""");

        Assert.Null(new SettingsService(_directory).Load().WindowScale);
    }

    [Fact]
    public void Save_ThenLoad_KeepsTheWindowSize()
    {
        var service = new SettingsService(_directory);

        service.Save(new AppSettings(WindowScale: 1.5));

        Assert.Equal(1.5, service.Load().WindowScale);
    }

    [Fact]
    public void Save_ThenLoad_KeepsAlwaysOnTop()
    {
        var service = new SettingsService(_directory);

        service.Save(new AppSettings(AlwaysOnTop: true));
        var settings = service.Load();

        Assert.True(settings.AlwaysOnTop);
    }

    [Fact]
    public void Save_ThenLoad_KeepsTurnedOffOptions()
    {
        var service = new SettingsService(_directory);

        service.Save(new AppSettings(AutoSwitchProfiles: false, HideWhenClickedAway: false));
        var settings = service.Load();

        Assert.False(settings.AutoSwitchProfiles);
        Assert.False(settings.HideWhenClickedAway);
    }

    [Fact]
    public void Save_CreatesTheSettingsFolderWhenItDoesNotExist()
    {
        new SettingsService(_directory).Save(new AppSettings(DisplayMode: "60 Hz"));

        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        // Saving writes a temp file and swaps it in, so a crash mid-save
        // cannot leave a half-written settings.json.
        new SettingsService(_directory).Save(new AppSettings(DisplayMode: "Auto"));

        Assert.Equal([SettingsPath], Directory.GetFiles(_directory));
    }

    [Fact]
    public void Save_WritesEnumsAsReadableNamesAndOmitsEmptyValues()
    {
        new SettingsService(_directory).Save(new AppSettings(
            PluggedInProfile: new PowerProfile(PerformanceMode.Custom, CpuBoost.Medium, null)));

        var json = File.ReadAllText(SettingsPath);

        Assert.Contains("\"Mode\": \"Custom\"", json);
        Assert.Contains("\"Cpu\": \"Medium\"", json);
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("\"Gpu\":", json); // The empty boost field, not any setting whose name contains "Gpu".
    }

    [Fact]
    public void Load_MigratesTheSingleModeFromBeforeProfilesIntoThePluggedInProfile()
    {
        // The exact shape an earlier build wrote to disk.
        WriteSettings("""
            {
              "DisplayMode": "Auto",
              "BatteryChargeLimit": 100,
              "PerformanceMode": "Custom",
              "CustomCpuBoost": null,
              "CustomGpuBoost": null
            }
            """);

        var settings = new SettingsService(_directory).Load();

        Assert.Equal(new PowerProfile(PerformanceMode.Custom), settings.PluggedInProfile);
        Assert.Null(settings.OnBatteryProfile);   // falls back to the default at run time
        Assert.Equal("Auto", settings.DisplayMode);
        Assert.Equal(100, settings.BatteryChargeLimit);
    }

    [Fact]
    public void Load_MigratesEarlierBoostLevelsToo()
    {
        WriteSettings("""
            { "PerformanceMode": "Custom", "CustomCpuBoost": "High", "CustomGpuBoost": "Medium" }
            """);

        var settings = new SettingsService(_directory).Load();

        Assert.Equal(new PowerProfile(PerformanceMode.Custom, CpuBoost.High, GpuBoost.Medium), settings.PluggedInProfile);
    }

    [Fact]
    public void Load_ClearsTheOldFieldsAfterMigrating_SoTheyAreNotWrittenAgain()
    {
        WriteSettings("""{ "PerformanceMode": "Silent" }""");
        var service = new SettingsService(_directory);

        service.Save(service.Load());

        var json = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("PerformanceMode", json);
        Assert.Contains("\"Mode\": \"Silent\"", json);
    }

    [Fact]
    public void Load_DoesNotLetOldFieldsOverwriteAProfileThatAlreadyExists()
    {
        WriteSettings("""
            {
              "PerformanceMode": "Silent",
              "PluggedInProfile": { "Mode": "Custom", "Cpu": "High" }
            }
            """);

        var settings = new SettingsService(_directory).Load();

        Assert.Equal(new PowerProfile(PerformanceMode.Custom, CpuBoost.High), settings.PluggedInProfile);
    }

    [Fact]
    public void Load_IgnoresOldValuesItDoesNotRecognise()
    {
        WriteSettings("""{ "PerformanceMode": "Turbo", "CustomCpuBoost": "Ludicrous" }""");

        var settings = new SettingsService(_directory).Load();

        Assert.Equal(new PowerProfile(), settings.PluggedInProfile);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheRecordedRazerServiceStartupTypes()
    {
        var service = new SettingsService(_directory);
        var modes = new Dictionary<string, System.ServiceProcess.ServiceStartMode>
        {
            ["Razer Chroma SDK Server"] = System.ServiceProcess.ServiceStartMode.Automatic,
            ["Razer Elevation Service"] = System.ServiceProcess.ServiceStartMode.Manual
        };

        service.Save(new AppSettings(RazerServiceStartModes: modes));

        var loaded = service.Load();
        Assert.Equal(modes, loaded.RazerServiceStartModes);
    }

    [Fact]
    public void Save_WritesTheRazerServiceStartupTypesAsReadableNames()
    {
        new SettingsService(_directory).Save(new AppSettings(
            RazerServiceStartModes: new Dictionary<string, System.ServiceProcess.ServiceStartMode>
            {
                ["Razer Elevation Service"] = System.ServiceProcess.ServiceStartMode.Manual
            }));

        Assert.Contains("\"Razer Elevation Service\": \"Manual\"", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"DisplayMode\": ")]
    [InlineData("[]")]
    public void Load_FallsBackToEmptySettingsForAFileItCannotRead(string contents)
    {
        WriteSettings(contents);

        Assert.Equal(new AppSettings(), new SettingsService(_directory).Load());
    }

    [Fact]
    public void Load_FallsBackToEmptySettingsWhenAProfileNamesAModeThisBuildDoesNotKnow()
    {
        // Documents current behavior: one unrecognised value in the new format
        // (say, from a newer build) discards the whole file instead of just
        // that field. It does not crash, and the user's choices are re-made.
        WriteSettings("""
            {
              "DisplayMode": "Auto",
              "PluggedInProfile": { "Mode": "Hyperboost" }
            }
            """);

        Assert.Equal(new AppSettings(), new SettingsService(_directory).Load());
    }
}
