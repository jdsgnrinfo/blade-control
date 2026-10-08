using System.Globalization;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class HardwareStatsTextTests
{
    [Fact]
    public void MissingReadingsShowDashes()
    {
        Assert.Equal("--", HardwareStatsText.Celsius(null));
        Assert.Equal("--", HardwareStatsText.Percent(null));
        Assert.Equal("--", HardwareStatsText.Gigahertz(null, CultureInfo.InvariantCulture));
        Assert.Equal("--", HardwareStatsText.Megahertz(null));
    }

    [Fact]
    public void FiguresAreWrittenShort()
    {
        Assert.Equal("52°C", HardwareStatsText.Celsius(51.5));
        Assert.Equal("23%", HardwareStatsText.Percent(22.6));
        Assert.Equal("1455 MHz", HardwareStatsText.Megahertz(1455));
    }

    [Fact]
    public void CpuClockIsInGigahertzInTheReadersCulture()
    {
        Assert.Equal("4.52 GHz", HardwareStatsText.Gigahertz(4518, CultureInfo.InvariantCulture));
        Assert.Equal("4,52 GHz", HardwareStatsText.Gigahertz(4518, new CultureInfo("es-ES")));
    }
}
