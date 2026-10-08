using BladeControl.Core.Hardware;
using BladeControl.Core.Models;
using BladeControl.Core.Services;
using BladeControl.Core.Tests.TestSupport;

namespace BladeControl.Core.Tests.Services;

public class LightingServiceTests
{
    private static (LightingService Service, FakeEc Ec) Create()
    {
        var ec = new FakeEc();
        return (new LightingService(ec), ec);
    }

    // ---- Keyboard color (Blade 15 Base 2020) -------------------------------

    [Fact]
    public void KeyboardColor_SendsTheStandardStaticEffectVerifiedOnTheLaptop()
    {
        // White sent as [6, FF, FF, FF] lit a Blade 15 Base (2020) white.
        var ec = new FakeEc();
        var service = new LightingService(ec, offersColor: true);

        service.SetKeyboardColor(RgbColor.White);

        var write = Assert.Single(ec.Writes);
        Assert.Equal(RazerCommands.SetStandardEffect, write.Command);
        Assert.Equal("06FFFFFF", Convert.ToHexString(write.Arguments));
    }

    [Fact]
    public void KeyboardColor_IsReadBackOnlyWhileTheEffectIsStatic()
    {
        var ec = new FakeEc();
        var service = new LightingService(ec, offersColor: true);

        ec.StandardEffect = [0x06, 0x12, 0x34, 0x56];
        Assert.Equal(new RgbColor(0x12, 0x34, 0x56), service.ReadState().KeyboardColor);

        ec.StandardEffect = [0x04, 0x00, 0x00, 0x00]; // spectrum: no single color
        Assert.Null(service.ReadState().KeyboardColor);
    }

    [Fact]
    public void KeyboardColor_ThatTheLaptopDoesNotTake_Fails()
    {
        var ec = new FakeEc { IgnoreKeyboardEffectWrites = true };
        var service = new LightingService(ec, offersColor: true);

        Assert.Throws<InvalidOperationException>(() => service.SetKeyboardColor(RgbColor.White));
    }

    [Fact]
    public void KeyboardColor_OnAModelWithoutColor_IsNeverSentOrRead()
    {
        var (service, ec) = Create();

        Assert.Null(service.ReadState().KeyboardColor);
        Assert.Throws<InvalidOperationException>(() => service.SetKeyboardColor(RgbColor.White));
        Assert.DoesNotContain(ec.Log, sent => sent.Command is RazerCommands.SetStandardEffect or RazerCommands.GetStandardEffect);
    }

    [Fact]
    public void KeyboardBreathing_SendsTheOneColorBreathingCheckedOnTheLaptop()
    {
        // [3, 1, FF, FF, FF] made a Blade 15 Base (2020) breathe in white.
        var ec = new FakeEc();
        var service = new LightingService(ec, offersColor: true);

        service.SetKeyboardBreathing(RgbColor.White);

        var write = Assert.Single(ec.Writes);
        Assert.Equal("0301FFFFFF", Convert.ToHexString(write.Arguments));

        var state = service.ReadState();
        Assert.Equal(KeyboardEffect.Breathing, state.Keyboard);
        Assert.Equal(RgbColor.White, state.KeyboardColor);
    }

    // The exact bytes sent to a Razer Blade 16 (2023) during the hardware
    // tests, so a refactor can never quietly change what goes on the wire.

    [Theory]
    [InlineData((int)KeyboardEffect.Off, "010500")]
    [InlineData((int)KeyboardEffect.StaticGreen, "01050100000144D62C")]
    [InlineData((int)KeyboardEffect.Spectrum, "010503")]
    [InlineData((int)KeyboardEffect.Breathing, "010502")]
    [InlineData((int)KeyboardEffect.Wave, "01050401")]
    public void KeyboardEffects_SendTheBytesVerifiedOnTheLaptop(int effect, string expectedArguments)
    {
        var (service, ec) = Create();

        service.SetKeyboardEffect((KeyboardEffect)effect);

        var write = Assert.Single(ec.Writes);
        Assert.Equal(RazerCommands.SetKeyboardEffect, write.Command);
        Assert.Equal(expectedArguments, Convert.ToHexString(write.Arguments));
    }

    [Fact]
    public void AKeyboardEffect_IsConfirmedByReadingItBack()
    {
        var (service, ec) = Create();

        service.SetKeyboardEffect(KeyboardEffect.Wave);

        Assert.Contains(ec.Reads, read => read.Command == RazerCommands.GetKeyboardEffect);
        Assert.Equal(4, ec.KeyboardEffectId);
    }

    [Fact]
    public void IfTheLaptopKeepsShowingTheOldEffect_ItIsAnError_NotASilentSuccess()
    {
        var (service, ec) = Create();
        ec.IgnoreKeyboardEffectWrites = true;

        var failure = Assert.Throws<InvalidOperationException>(() => service.SetKeyboardEffect(KeyboardEffect.Off));

        Assert.Contains("did not confirm", failure.Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 128)]
    [InlineData(100, 255)]
    public void KeyboardBrightness_IsSentAsAByte(int percent, int expectedByte)
    {
        var (service, ec) = Create();

        service.SetKeyboardBrightness(percent);

        var write = Assert.Single(ec.Writes);
        Assert.Equal(RazerCommands.SetBrightness, write.Command);
        Assert.Equal([0x01, 0x05, (byte)expectedByte], write.Arguments);
    }

    [Fact]
    public void LogoBrightness_UsesTheLogoLight()
    {
        var (service, ec) = Create();

        service.SetLogoBrightness(40);

        Assert.Equal(0x04, Assert.Single(ec.Writes).Arguments[1]);
        Assert.Equal(102, ec.LogoBrightness);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void BrightnessOutsideZeroToOneHundred_IsRefusedAndNothingIsSent(int percent)
    {
        var (service, ec) = Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.SetKeyboardBrightness(percent));
        Assert.Empty(ec.Log);
    }

    [Fact]
    public void LogoOn_SetsTheSteadyModeThenPowersItOn()
    {
        var (service, ec) = Create();

        service.SetLogo(LogoMode.On);

        Assert.Equal(
            [(RazerCommands.SetLogoMode, "010400"), (RazerCommands.SetLogoPower, "010401")],
            ec.Writes.Select(write => (write.Command, Convert.ToHexString(write.Arguments))));
        Assert.True(ec.LogoOn);
    }

    [Fact]
    public void LogoBreathing_SetsTheBreathingModeThenPowersItOn()
    {
        var (service, ec) = Create();

        service.SetLogo(LogoMode.Breathing);

        Assert.Equal(
            [(RazerCommands.SetLogoMode, "010402"), (RazerCommands.SetLogoPower, "010401")],
            ec.Writes.Select(write => (write.Command, Convert.ToHexString(write.Arguments))));
    }

    [Fact]
    public void LogoOff_OnlyTurnsThePowerOff_AndLeavesTheModeAlone()
    {
        var (service, ec) = Create();
        ec.LogoOn = true;
        ec.LogoModeByte = 2;

        service.SetLogo(LogoMode.Off);

        var write = Assert.Single(ec.Writes);
        Assert.Equal(RazerCommands.SetLogoPower, write.Command);
        Assert.Equal("010400", Convert.ToHexString(write.Arguments));
        Assert.Equal(2, ec.LogoModeByte);
    }

    [Fact]
    public void ALogoChangeThatIsNotEchoedBack_IsAnError()
    {
        var (service, ec) = Create();
        ec.EchoOverride = _ => [0x00, 0x00, 0x00];

        Assert.Throws<InvalidOperationException>(() => service.SetLogo(LogoMode.On));
    }

    [Fact]
    public void ReadState_ShowsWhatTheLaptopIsDoing()
    {
        var (service, ec) = Create();
        ec.KeyboardEffectId = 4;
        ec.KeyboardBrightness = 128;
        ec.LogoOn = true;
        ec.LogoModeByte = 2;
        ec.LogoBrightness = 255;

        var state = service.ReadState();

        Assert.Equal(new LightingState(KeyboardEffect.Wave, 50, LogoMode.Breathing, 100), state);
    }

    [Fact]
    public void ReadState_WithTheLogoOff_SaysOff_WhateverModeIsStored()
    {
        var (service, ec) = Create();
        ec.LogoOn = false;
        ec.LogoModeByte = 2;

        Assert.Equal(LogoMode.Off, service.ReadState().Logo);
    }

    [Fact]
    public void AStaticEffect_IsShownAsStaticGreen()
    {
        var (service, ec) = Create();
        ec.KeyboardEffectId = 1;

        Assert.Equal(KeyboardEffect.StaticGreen, service.ReadState().Keyboard);
    }

    [Theory]
    [InlineData(5)] // Reactive.
    [InlineData(7)] // Starlight.
    [InlineData(10)] // Wheel, which the Blade does not support.
    public void AnEffectSetByOtherSoftware_IsReportedAsUnknown_NotAsSomethingElse(byte effectId)
    {
        var (service, ec) = Create();
        ec.KeyboardEffectId = effectId;

        Assert.Null(service.ReadState().Keyboard);
    }

    [Fact]
    public void ALogoModeWeDoNotOffer_IsReportedAsUnknown()
    {
        var (service, ec) = Create();
        ec.LogoOn = true;
        ec.LogoModeByte = 9;

        Assert.Null(service.ReadState().Logo);
    }

    [Fact]
    public void ReadingState_NeverWrites()
    {
        var (service, ec) = Create();

        service.ReadState();

        Assert.Empty(ec.Writes);
    }

    [Fact]
    public void ADifferentLightInTheResponse_IsRefused()
    {
        // A response for another light must never be taken as the keyboard's.
        Assert.Throws<InvalidOperationException>(() => new LightingService(new WrongLightTransport()).ReadState());
    }

    [Fact]
    public void NothingHereCanChangeTheDeviceMode()
    {
        // Driver mode (device mode 3, command 0x0004) is what switches the Fn
        // media keys off. Whatever lighting is asked to do, it must never be sent.
        var (service, ec) = Create();
        service.SetKeyboardEffect(KeyboardEffect.Wave);
        service.SetKeyboardBrightness(70);
        service.SetLogo(LogoMode.Breathing);
        service.SetLogo(LogoMode.Off);

        Assert.DoesNotContain(ec.Log, sent => sent.Command == 0x0004);
    }

    // Answers every read for the wrong light.
    private sealed class WrongLightTransport : IRazerTransport
    {
        public byte[] Send(ushort command, ReadOnlySpan<byte> arguments) => FakeEc.Respond(0x01, 0x09, 0x03);
    }
}
