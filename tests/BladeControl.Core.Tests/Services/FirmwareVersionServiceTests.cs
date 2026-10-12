using BladeControl.Core.Hardware;
using BladeControl.Core.Services;
using BladeControl.Core.Tests.TestSupport;

namespace BladeControl.Core.Tests.Services;

public class FirmwareVersionServiceTests
{
    [Fact]
    public void ReadsMajorAndMinor_AsTheBlade15Base2020Answers()
    {
        var service = new FirmwareVersionService(new ScriptedTransport((_, _) => FakeEc.Respond(1, 1)));

        Assert.Equal("1.1", service.Read());
    }

    [Fact]
    public void AsksOnce_WithTheReadCommandOnly()
    {
        var sent = new List<ushort>();
        var service = new FirmwareVersionService(new ScriptedTransport((command, _) =>
        {
            sent.Add(command);
            return FakeEc.Respond(2, 3);
        }));

        Assert.Equal("2.3", service.Read());
        Assert.Equal("2.3", service.Read());
        Assert.Equal([RazerCommands.GetFirmwareVersion], sent);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0xFF, 0xFF)]
    public void AnEmptyRegister_IsNoVersion(byte major, byte minor) =>
        Assert.Null(new FirmwareVersionService(new ScriptedTransport((_, _) => FakeEc.Respond(major, minor))).Read());

    [Fact]
    public void ALaptopThatDoesNotSay_IsNotAskedAgain()
    {
        var asked = 0;
        var service = new FirmwareVersionService(new ScriptedTransport((command, _) =>
        {
            asked++;
            throw new RazerCommandNotSupportedException(command);
        }));

        Assert.Null(service.Read());
        Assert.Null(service.Read());
        Assert.Equal(1, asked);
    }

    [Fact]
    public void AnyOtherFailure_IsTriedAgainNextTime()
    {
        var calls = 0;
        var service = new FirmwareVersionService(new ScriptedTransport((_, _) =>
            ++calls == 1 ? throw new IOException("Not found yet.") : FakeEc.Respond(1, 1)));

        Assert.Throws<IOException>(() => service.Read());
        Assert.Equal("1.1", service.Read());
    }
}
