using System.Globalization;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class SystemInfoTextTests
{
    [Theory]
    [InlineData("Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz", "Intel Core i7-10750H")]
    [InlineData("AMD Ryzen 7 4800H with Radeon Graphics", "AMD Ryzen 7 4800H")]
    [InlineData("12th Gen Intel(R) Core(TM) i7-12800H", "12th Gen Intel Core i7-12800H")]
    public void CpuName_DropsTheMarksAndTheBaseClock(string name, string expected) =>
        Assert.Equal(expected, SystemInfoText.CpuName(name));

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1660 Ti", "GeForce GTX 1660 Ti")]
    [InlineData("Intel(R) UHD Graphics", "UHD Graphics")]
    [InlineData("AMD Radeon RX 6800M", "Radeon RX 6800M")]
    [InlineData("Microsoft Basic Display Adapter", "Microsoft Basic Display Adapter")]
    public void GpuName_DropsTheMaker(string name, string expected) =>
        Assert.Equal(expected, SystemInfoText.GpuName(name));

    [Theory]
    [InlineData("Razer Blade 15 Base (2020)", "Razer Blade 15 Base")]
    [InlineData("Razer Blade 14", "Razer Blade 14")]
    [InlineData(null, "DESKTOP-1234")]
    public void ComputerName_IsTheModelWithoutItsYear_OrWindowsName(string? model, string expected) =>
        Assert.Equal(expected, SystemInfoText.ComputerName(model, "DESKTOP-1234"));

    [Fact]
    public void OsName_CallsWindows11ByItsName() =>
        Assert.Equal("Windows 11 Home", SystemInfoText.OsName("Windows 10 Home", 26200));

    [Fact]
    public void OsName_LeavesWindows10Alone() =>
        Assert.Equal("Windows 10 Pro", SystemInfoText.OsName("Windows 10 Pro", 19045));

    [Fact]
    public void OsDetail_GivesTheVersionAndBuild() =>
        Assert.Equal("25H2 (build 26200.9457)", SystemInfoText.OsDetail("25H2", 26200, 9457));

    [Fact]
    public void CpuDetail_GivesCoresAndThreads() =>
        Assert.Equal("6 cores · 12 threads", SystemInfoText.CpuDetail(6, 12));

    [Theory]
    [InlineData(500107862016L, "466 GB")]
    [InlineData(1000204886016L, "932 GB")]
    [InlineData(25769803776L, "24 GB")]
    [InlineData(4294967296L, "4.0 GB")]
    [InlineData(2199023255552L, "2.0 TB")]
    [InlineData(134217728L, "128 MB")]
    public void Size_IsInBinaryUnitsLikeWindows(long bytes, string expected) =>
        Assert.Equal(expected, SystemInfoText.Size(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void BiosDate_IsInTheReadersFormat() =>
        Assert.Equal("4/6/2020", SystemInfoText.BiosDate("06/04/2020", CultureInfo.GetCultureInfo("es-ES")));

    [Theory]
    [InlineData("Blade 15 Base Model (Early 2020) - RZ09-0328", "Blade 15 Base Model (Early 2020)")]
    [InlineData("Blade 16 - RZ09-0483T-EUR2", "Blade 16")]
    [InlineData("Blade 15 Base Model (Early 2020)", "Blade 15 Base Model (Early 2020)")]
    [InlineData("ROG Zephyrus G14", "ROG Zephyrus G14")]
    public void ModelName_DropsTheProductCodeAtTheEnd(string model, string expected) =>
        Assert.Equal(expected, SystemInfoText.ModelName(model));

    [Theory]
    [InlineData("NVIDIA", 6442450944L, "NVIDIA (6.0 GB)")]
    [InlineData("Intel", 134217728L, "Intel (128 MB)")]
    [InlineData("Intel", null, "Intel")]
    [InlineData(null, 134217728L, "128 MB")]
    [InlineData(null, null, null)]
    public void GpuDetail_PutsTheMemoryInBracketsAfterTheMaker(string? vendor, long? bytes, string? expected) =>
        Assert.Equal(expected, SystemInfoText.GpuDetail(vendor, bytes, CultureInfo.InvariantCulture));
}
