using System.ServiceProcess;
using BladeControl.Core.Localization;
using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class SettingsTransferTests
{
    private static readonly DateTime When = new(2026, 10, 12, 9, 30, 0);

    private static readonly AppSettings Chosen = new(
        DisplayMode: "Auto",
        PluggedInProfile: new PowerProfile(PerformanceMode.Custom, CpuBoost.Boost, GpuBoost.Medium),
        OnBatteryProfile: new PowerProfile(PerformanceMode.Silent),
        AutoSwitchProfiles: false,
        Language: "es",
        ColorProfile: "Warm",
        LightsOffWhenIdle: true,
        LightsOffIdleMinutes: 5,
        AppProfiles:
        [
            new AppProfile("Forza Horizon 5", @"D:\Games\ForzaHorizon5", Source: AppSource.Steam, Mode: PerformanceMode.Balanced, RefreshHz: 144),
            new AppProfile("Blender", @"C:\Program Files\Blender\blender.exe", Mode: PerformanceMode.Custom, Cpu: CpuBoost.High, Gpu: GpuBoost.High)
        ],
        // Only meaningful on the PC that wrote them.
        RazerServiceStartModes: new() { ["Razer Game Manager Service"] = ServiceStartMode.Manual },
        RazerLoginApprovals: new() { ["Razer Synapse"] = "02" },
        CpuBoostBeforeSilent: new SavedPlanValue(Guid.NewGuid(), 2, 1),
        PlanBeforeIdle: Guid.NewGuid(),
        WindowScale: 1.25,
        Window: new WindowPlacement(10, 20, 1200, 800, false));

    private static ImportedSettings Import(string text, AppSettings current, Func<string, bool>? exists = null) =>
        SettingsTransfer.Import(text, current, exists ?? (_ => true), out _)!;

    [Fact]
    public void Export_ThenImport_BringsBackWhatTheUserChose()
    {
        var text = SettingsTransfer.Export(Chosen, "0.1.1", When);

        var imported = Import(text, new AppSettings()).Settings;

        Assert.Equal(Chosen.DisplayMode, imported.DisplayMode);
        Assert.Equal(Chosen.PluggedInProfile, imported.PluggedInProfile);
        Assert.Equal(Chosen.OnBatteryProfile, imported.OnBatteryProfile);
        Assert.False(imported.AutoSwitchProfiles);
        Assert.Equal("es", imported.Language);
        Assert.Equal("Warm", imported.ColorProfile);
        Assert.True(imported.LightsOffWhenIdle);
        Assert.Equal(5, imported.LightsOffIdleMinutes);
        Assert.Equal(Chosen.AppProfiles, imported.AppProfiles);
    }

    [Fact]
    public void Export_LeavesOutWhatOnlyMeansSomethingOnThisPc()
    {
        var text = SettingsTransfer.Export(Chosen, "0.1.1", When);

        Assert.DoesNotContain("Razer Game Manager Service", text);
        Assert.DoesNotContain("Razer Synapse", text);
        Assert.DoesNotContain(nameof(AppSettings.CpuBoostBeforeSilent), text);
        Assert.DoesNotContain(nameof(AppSettings.PlanBeforeIdle), text);
        Assert.DoesNotContain(nameof(AppSettings.WindowScale), text);
        Assert.DoesNotContain("\"Window\"", text);
        Assert.Contains(SettingsTransfer.Format, text);
        Assert.Contains("0.1.1", text);
    }

    [Fact]
    public void Import_KeepsThisPcsOwnRecords()
    {
        var text = SettingsTransfer.Export(new AppSettings(Language: "en"), "0.1.1", When);

        var imported = Import(text, Chosen).Settings;

        Assert.Equal("en", imported.Language);
        Assert.Same(Chosen.RazerServiceStartModes, imported.RazerServiceStartModes);
        Assert.Same(Chosen.RazerLoginApprovals, imported.RazerLoginApprovals);
        Assert.Equal(Chosen.CpuBoostBeforeSilent, imported.CpuBoostBeforeSilent);
        Assert.Equal(Chosen.PlanBeforeIdle, imported.PlanBeforeIdle);
        Assert.Equal(Chosen.WindowScale, imported.WindowScale);
        Assert.Equal(Chosen.Window, imported.Window);
        // The file had no app profiles: none are kept from before.
        Assert.Null(imported.AppProfiles);
    }

    [Fact]
    public void Import_CountsTheAppsNotOnThisPc()
    {
        var text = SettingsTransfer.Export(Chosen, "0.1.1", When);

        var imported = Import(text, new AppSettings(), path => path.EndsWith("blender.exe"));

        Assert.Equal(2, imported.AppProfiles);
        Assert.Equal(1, imported.MissingApps);
        Assert.Equal("0.1.1", imported.AppVersion);
    }

    [Fact]
    public void Import_DropsAppProfilesWithNoNameOrProgram()
    {
        var text = $$"""
            { "Format": "{{SettingsTransfer.Format}}", "Version": 1,
              "Settings": { "AppProfiles": [ { "Name": "Game", "Path": "C:\\game.exe" }, { "Name": "", "Path": "C:\\x.exe" }, { "Name": "No path" } ] } }
            """;

        var imported = Import(text, new AppSettings());

        Assert.Equal("Game", Assert.Single(imported.Settings.AppProfiles!).Name);
        Assert.Equal(1, imported.AppProfiles);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{ \"DisplayMode\": \"Auto\" }")]                                   // a plain settings.json
    [InlineData("{ \"Format\": \"Something else\", \"Version\": 1, \"Settings\": {} }")]
    [InlineData("{ \"Format\": \"Blade Control settings\", \"Version\": 1 }")]
    public void Import_RefusesWhatIsNotASettingsFile(string text)
    {
        Assert.Null(SettingsTransfer.Import(text, new AppSettings(), _ => true, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Import_RefusesAFileFromANewerVersion()
    {
        var text = $$"""{ "Format": "{{SettingsTransfer.Format}}", "Version": 2, "Settings": {} }""";

        Assert.Null(SettingsTransfer.Import(text, new AppSettings(), _ => true, out var error));
        Assert.Equal(L.T("This file was made by a newer Blade Control. Update Blade Control and try again."), error);
    }
}
