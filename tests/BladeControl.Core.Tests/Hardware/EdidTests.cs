using System.Globalization;
using BladeControl.Core.Hardware;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Hardware;

public class EdidTests
{
    // The base block of a Blade 15 Base (2020)'s panel, a CSOT MNF601BA1-4.
    private static readonly byte[] Blade15Panel = Convert.FromHexString((
        "00 FF FF FF FF FF FF 00 0E 77 0F 15 00 00 00 00 00 21 01 04 A5 22 13 78 03 ED F5 A3 55 4C 9C 26 " +
        "0D 50 54 00 00 00 01 01 01 01 01 01 01 01 01 01 01 01 01 01 01 01 8D 38 80 A0 70 38 50 40 30 20 " +
        "36 00 58 C1 10 00 00 18 1A 71 80 A0 70 38 50 40 30 20 36 00 58 C1 10 00 00 18 00 00 00 FD 00 30 " +
        "78 A7 A7 23 01 0A 20 20 20 20 20 20 00 00 00 FE 00 4D 4E 46 36 30 31 42 41 31 2D 34 0A 20 00 15").Replace(" ", ""));

    [Fact]
    public void Parse_ReadsAPanelsBaseBlock()
    {
        var edid = Edid.Parse(Blade15Panel);

        Assert.NotNull(edid);
        Assert.Equal("CSW", edid.MakerCode);
        Assert.Equal(0x150F, edid.ProductCode);
        Assert.Equal("MNF601BA1-4", edid.Model);
        Assert.Equal((344, 193), (edid.WidthMillimeters, edid.HeightMillimeters));
        Assert.Equal((1920, 1080), (edid.NativeWidth, edid.NativeHeight));
        Assert.Equal(8, edid.BitsPerColor);
    }

    [Fact]
    public void Parse_RefusesWhatIsNotAnEdid()
    {
        Assert.Null(Edid.Parse(new byte[128]));
        Assert.Null(Edid.Parse(Blade15Panel.AsSpan(0, 64)));
    }

    [Fact]
    public void Panel_IsShownAsSold()
    {
        var edid = Edid.Parse(Blade15Panel)!;

        Assert.Equal("CSOT", DisplayPanelText.MakerName(edid.MakerCode));
        Assert.Equal("15.6″", DisplayPanelText.Diagonal(edid.DiagonalInches!.Value, CultureInfo.InvariantCulture));
        Assert.Equal("34.4 × 19.3 cm", DisplayPanelText.Area(344, 193, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(15.53, "15.6″")]
    [InlineData(16.02, "16.0″")]
    [InlineData(12.5, "12.5″")]
    public void Diagonal_SnapsToTheSizesLaptopsAreSoldAs(double inches, string expected) =>
        Assert.Equal(expected, DisplayPanelText.Diagonal(inches, CultureInfo.InvariantCulture));

    [Fact]
    public void RegistryKeyFor_FindsTheMonitorsDeviceParameters() =>
        Assert.Equal(
            @"SYSTEM\CurrentControlSet\Enum\DISPLAY\CSW150F\4&2f2e9f3c&0&UID8388688\Device Parameters",
            DisplayPanelReader.RegistryKeyFor(@"\\?\DISPLAY#CSW150F#4&2f2e9f3c&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"));
}
