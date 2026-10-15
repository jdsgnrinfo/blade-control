using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class ColorProfileTests
{
    private const int Levels = ColorProfiles.Levels;

    private static (ushort Red, ushort Green, ushort Blue) At(ushort[] ramp, int level) =>
        (ramp[level], ramp[Levels + level], ramp[2 * Levels + level]);

    [Fact]
    public void Standard_IsTheStraightLine_OnEveryChannel()
    {
        var ramp = ColorProfiles.Ramp(ColorProfile.Standard);

        Assert.Equal(3 * Levels, ramp.Length);
        Assert.Equal(((ushort)0, (ushort)0, (ushort)0), At(ramp, 0));
        Assert.Equal((ushort.MaxValue, ushort.MaxValue, ushort.MaxValue), At(ramp, Levels - 1));
        Assert.Equal(At(ramp, 128).Red, At(ramp, 128).Blue);
    }

    [Fact]
    public void Warm_HasLessBlueThanGreen_AndLessGreenThanRed()
    {
        var (red, green, blue) = At(ColorProfiles.Ramp(ColorProfile.Warm), Levels - 1);

        Assert.Equal(ushort.MaxValue, red);
        Assert.True(green < red && blue < green);
    }

    [Fact]
    public void Cool_KeepsBlue_AndTakesALittleRedAndGreen()
    {
        var (red, green, blue) = At(ColorProfiles.Ramp(ColorProfile.Cool), Levels - 1);

        Assert.Equal(ushort.MaxValue, blue);
        Assert.True(red < green && green < blue);
    }

    [Fact]
    public void Contrast_DarkensShadows_BrightensHighlights_AndKeepsTheEnds()
    {
        var contrast = ColorProfiles.Ramp(ColorProfile.Contrast);
        var standard = ColorProfiles.Ramp(ColorProfile.Standard);

        Assert.True(contrast[50] < standard[50]);
        Assert.True(contrast[205] > standard[205]);
        Assert.Equal(standard[0], contrast[0]);
        Assert.Equal(standard[Levels - 1], contrast[Levels - 1]);
    }

    [Fact]
    public void EveryRamp_RisesStepByStep()
    {
        foreach (var profile in Enum.GetValues<ColorProfile>())
        {
            var ramp = ColorProfiles.Ramp(profile);

            for (var channel = 0; channel < 3; channel++)
            {
                for (var level = 1; level < Levels; level++)
                    Assert.True(ramp[channel * Levels + level] >= ramp[channel * Levels + level - 1], $"{profile} channel {channel} level {level}");
            }
        }
    }

    [Theory]
    [InlineData(null, "Standard")]
    [InlineData("Warm", "Warm")]
    [InlineData("contrast", "Contrast")]
    [InlineData("Sepia", "Standard")]
    [InlineData("7", "Standard")]
    public void Parse_ReadsASavedName_AndFallsBackToStandard(string? setting, string expected) =>
        Assert.Equal(expected, ColorProfiles.Parse(setting).ToString());

    [Fact]
    public void ToSetting_SavesNothingForStandard() =>
        Assert.Null(ColorProfiles.ToSetting(ColorProfile.Standard));

    private sealed class FakeRamps : IGammaRamps
    {
        public Dictionary<string, ushort[]> Screens { get; } = new()
        {
            [@"\\.\DISPLAY1"] = Calibrated(),
            [@"\\.\DISPLAY2"] = ColorProfiles.Ramp(ColorProfile.Standard)
        };

        public bool Refuse { get; set; }

        public IReadOnlyList<string> Displays() => Screens.Keys.ToList();

        public ushort[]? Get(string display) => Screens.TryGetValue(display, out var ramp) ? (ushort[])ramp.Clone() : null;

        public bool Set(string display, ushort[] ramp)
        {
            if (Refuse)
                return false;

            Screens[display] = (ushort[])ramp.Clone();
            return true;
        }

        // A screen with its own calibration loaded, not quite straight.
        public static ushort[] Calibrated()
        {
            var ramp = ColorProfiles.Ramp(ColorProfile.Standard);
            ramp[2 * Levels + 200] -= 300;
            return ramp;
        }
    }

    [Fact]
    public void Applying_PutsTheProfileOnEveryScreen()
    {
        var screens = new FakeRamps();
        var service = new ColorProfileService(screens);

        Assert.True(service.Apply(ColorProfile.Warm));

        Assert.All(screens.Screens.Values, ramp => Assert.Equal(ColorProfiles.Ramp(ColorProfile.Warm), ramp));
    }

    [Fact]
    public void Standard_GivesBackEachScreensOwnRamp_ItsCalibrationIncluded()
    {
        var screens = new FakeRamps();
        var service = new ColorProfileService(screens);

        service.Apply(ColorProfile.Cool);
        service.Apply(ColorProfile.Warm);
        service.Apply(ColorProfile.Standard);

        Assert.Equal(FakeRamps.Calibrated(), screens.Screens[@"\\.\DISPLAY1"]);
        Assert.Equal(ColorProfiles.Ramp(ColorProfile.Standard), screens.Screens[@"\\.\DISPLAY2"]);
    }

    [Fact]
    public void Standard_FromTheStart_TouchesNoScreen()
    {
        var screens = new FakeRamps { Refuse = true };
        var service = new ColorProfileService(screens);

        Assert.True(service.Apply(ColorProfile.Standard));
    }

    [Fact]
    public void Restore_GivesTheRampsBack_AndKeepsTheChoice()
    {
        var screens = new FakeRamps();
        var service = new ColorProfileService(screens);
        service.Apply(ColorProfile.Contrast);

        service.Restore();

        Assert.Equal(FakeRamps.Calibrated(), screens.Screens[@"\\.\DISPLAY1"]);
        Assert.Equal(ColorProfile.Contrast, service.Current);
    }

    [Fact]
    public void AProfileLeftOnTheScreenByAnEarlierRun_IsNotTakenForTheScreensOwn()
    {
        var screens = new FakeRamps();
        screens.Screens[@"\\.\DISPLAY1"] = ColorProfiles.Ramp(ColorProfile.Warm);
        var service = new ColorProfileService(screens);

        service.Apply(ColorProfile.Cool);
        service.Apply(ColorProfile.Standard);

        Assert.Equal(ColorProfiles.Ramp(ColorProfile.Standard), screens.Screens[@"\\.\DISPLAY1"]);
    }

    [Fact]
    public void AProfileWindowsLoadedFromTheAppsIcc_RoundedALittle_IsNotTakenForTheScreensOwn()
    {
        var screens = new FakeRamps();
        var rounded = ColorProfiles.Ramp(ColorProfile.Cool);

        for (var index = 0; index < rounded.Length; index++)
            rounded[index] = (ushort)Math.Min(ushort.MaxValue, rounded[index] + 64);

        screens.Screens[@"\\.\DISPLAY1"] = rounded;
        var service = new ColorProfileService(screens);

        service.Apply(ColorProfile.Warm);
        service.Apply(ColorProfile.Standard);

        Assert.Equal(ColorProfiles.Ramp(ColorProfile.Standard), screens.Screens[@"\\.\DISPLAY1"]);
    }

    [Fact]
    public void AScreenThatRefuses_IsReported()
    {
        var screens = new FakeRamps { Refuse = true };

        Assert.False(new ColorProfileService(screens).Apply(ColorProfile.Warm));
    }
}
