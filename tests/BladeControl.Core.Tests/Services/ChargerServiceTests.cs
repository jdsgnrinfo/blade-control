using BladeControl.Core.Hardware;
using BladeControl.Core.Services;
using BladeControl.Core.Tests.TestSupport;

namespace BladeControl.Core.Tests.Services;

public class ChargerServiceTests
{
    private static ChargerService Answering(params byte[] reply) =>
        new(new ScriptedTransport((_, _) => FakeEc.Respond(reply)));

    [Fact]
    public void TheBlade15Base2020sOwnCharger_IsTheMainOne_WithoutWatts() =>
        Assert.Equal("Main", Answering(0x0D, 0x0C).Read());

    [Fact]
    public void OnBattery_ThereIsNoCharger() =>
        Assert.Null(Answering(0x00, 0x0C).Read());

    [Theory]
    [InlineData(0x02, "USB-C · 45 W")]
    [InlineData(0x04, "USB-C · 65 W")]
    [InlineData(0x07, "USB-C · 100 W")]
    [InlineData(0x11, "Main · 280 W")]
    [InlineData(0x12, "Main · 330 W")]
    public void KnownCodes_NameTheChargerAndItsWatts(byte source, string expected) =>
        Assert.Equal(expected, Answering(source, 0x11).Read());

    [Fact]
    public void ItSendsOnlyTheReadCommand()
    {
        var sent = new List<ushort>();
        var service = new ChargerService(new ScriptedTransport((command, _) =>
        {
            sent.Add(command);
            return FakeEc.Respond(0x0D, 0x0C);
        }));

        service.Read();

        Assert.Equal([RazerCommands.GetPowerSource], sent);
    }

    [Fact]
    public void ALaptopThatDoesNotSay_IsNotAskedAgain()
    {
        var asked = 0;
        var service = new ChargerService(new ScriptedTransport((command, _) =>
        {
            asked++;
            throw new RazerCommandNotSupportedException(command);
        }));

        Assert.Null(service.Read());
        Assert.Null(service.Read());
        Assert.Equal(1, asked);
    }
}
