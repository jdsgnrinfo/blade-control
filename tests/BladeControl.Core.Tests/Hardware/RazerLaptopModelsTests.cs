using BladeControl.Core.Hardware;

namespace BladeControl.Core.Tests.Hardware;

public class RazerLaptopModelsTests
{
    [Fact]
    public void IsNotEmpty()
    {
        Assert.NotEmpty(RazerLaptopModels.Known);
    }

    [Fact]
    public void HasNoDuplicateProductIds()
    {
        var productIds = RazerLaptopModels.Known.Select(model => model.ProductId);

        Assert.Equal(productIds.Distinct().Count(), productIds.Count());
    }

    [Fact]
    public void ListsTheVerifiedBlade16_2023()
    {
        Assert.Contains(
            RazerLaptopModels.Known,
            model => model is { ProductId: 0x029F, Name: "Razer Blade 16 (2023)", Verified: true });
    }

    [Fact]
    public void ListsTheVerifiedBlade16_2025()
    {
        Assert.Contains(
            RazerLaptopModels.Known,
            model => model is { ProductId: 0x02C6, Name: "Razer Blade 16 (2025)", Verified: true });
    }

    [Fact]
    public void GivesTheBlade16_2025ItsPerformanceMode()
    {
        Assert.Contains(
            RazerLaptopModels.Known,
            model => model is { ProductId: 0x02C6, Name: "Razer Blade 16 (2025)", PerformanceWire: 0x02 });
    }
}
