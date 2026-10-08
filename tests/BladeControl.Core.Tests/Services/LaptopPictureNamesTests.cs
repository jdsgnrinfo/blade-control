using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class LaptopPictureNamesTests
{
    [Fact]
    public void CandidatesFor_AnAppModel_GoesFromTheSameLaptopThatYearToAnyBlade() =>
        Assert.Equal(
            ["blade-15-base-2020", "blade-15-2020", "blade-15-base", "blade-15", "blade"],
            LaptopPictureNames.CandidatesFor("Razer Blade 15 Base (2020)"));

    [Fact]
    public void CandidatesFor_WindowsModel_ReadsTheSameWay() =>
        Assert.Equal(
            ["blade-15-base-2020", "blade-15-2020", "blade-15-base", "blade-15", "blade"],
            LaptopPictureNames.CandidatesFor("Blade 15 Base Model (Early 2020) - RZ09-0328"));

    [Fact]
    public void CandidatesFor_AModelWithoutEdition_SkipsTheEditionNames() =>
        Assert.Equal(["blade-16-2023", "blade-16", "blade"], LaptopPictureNames.CandidatesFor("Razer Blade 16 (2023)"));

    [Fact]
    public void CandidatesFor_AStealth_FallsBackToItsSizeOfBlade() =>
        Assert.Equal(
            ["blade-stealth-13-2020", "blade-stealth-13", "blade-13-2020", "blade-13", "blade"],
            LaptopPictureNames.CandidatesFor("Razer Blade Stealth 13 (Late 2020) - RZ09-0310"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Some Other Laptop")]
    public void CandidatesFor_NoBlade_IsAnyBlade(string? model) =>
        Assert.Equal(["blade"], LaptopPictureNames.CandidatesFor(model));
}
