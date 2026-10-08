using BladeControl.Core.Models;

namespace BladeControl.Core.Tests.Models;

public class BatteryLimitRangeTests
{
    [Fact]
    public void TheOfferedRangeIsSixtyToOneHundred_AnyWholePercent()
    {
        Assert.Equal(60, BatteryLimitRange.Minimum);
        Assert.Equal(100, BatteryLimitRange.Maximum);
        Assert.Equal(1, BatteryLimitRange.Step);
    }

    [Fact]
    public void OneHundredPercentMeansNoLimit()
    {
        Assert.Equal(100, BatteryLimitRange.NoLimit);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(73)]
    [InlineData(80)]
    [InlineData(100)]
    public void IsValid_AcceptsTheOfferedSteps(int percent)
    {
        Assert.True(BatteryLimitRange.IsValid(percent));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-20)]
    [InlineData(50)]   // the EC accepts it, but this range does not offer it
    [InlineData(59)]
    [InlineData(101)]
    [InlineData(120)]
    public void IsValid_RejectsAnythingElse(int percent)
    {
        Assert.False(BatteryLimitRange.IsValid(percent));
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(69, 69)]
    [InlineData(87, 87)]
    [InlineData(100, 100)]
    public void Normalize_KeepsAnyPercentInTheRange(int input, int expected)
    {
        Assert.Equal(expected, BatteryLimitRange.Normalize(input));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-5, 60)]
    [InlineData(500, 100)]
    public void Normalize_ClampsOutOfRangeValues(int input, int expected)
    {
        Assert.Equal(expected, BatteryLimitRange.Normalize(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    [InlineData(74)]
    [InlineData(1000)]
    public void Normalize_AlwaysProducesAValidLimit(int input)
    {
        Assert.True(BatteryLimitRange.IsValid(BatteryLimitRange.Normalize(input)));
    }
}
