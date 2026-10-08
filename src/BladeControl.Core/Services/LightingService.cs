using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>
/// Reads and changes the keyboard backlight and the lid logo. Every command
/// here was sent to a Razer Blade 16 (2023) and read back before it was written
/// down. The device stays in Normal mode throughout, so the Fn media keys keep
/// working; nothing here can change the device mode.
/// </summary>
/// <param name="offersColor">
/// The model shows a chosen static color through the standard matrix effect
/// (see <see cref="RazerLaptopModel.HasKeyboardColor"/>). Only then is that
/// command sent or read.
/// </param>
internal sealed class LightingService(IRazerTransport transport, bool offersColor = false)
{
    // Effect ids of the standard matrix effect command, as the Blade 15 Base
    // (2020) takes them (each checked by eye on that laptop).
    private const byte StandardEffectBreathing = 0x03;
    private const byte StandardEffectStatic = 0x06;
    private const byte StandardBreathingOneColor = 0x01;

    // "Variable storage" 1 writes the laptop's own saved slot, so the effect
    // survives idle and wake. (Without it the laptop shows its default again.)
    private const byte StoreInLaptop = 0x01;

    private const byte LogoLed = 0x04;
    private const byte KeyboardLed = 0x05;

    // Effect ids of the extended matrix effect command.
    private const byte EffectOff = 0x00;
    private const byte EffectStatic = 0x01;
    private const byte EffectBreathing = 0x02;
    private const byte EffectSpectrum = 0x03;
    private const byte EffectWave = 0x04;
    private const byte WaveDirection = 0x01;

    // A static effect carries a color, laid out as OpenRazer sends it:
    // [store, led, 1, 0, 0, 1, red, green, blue]. In Normal mode the laptop
    // shows Razer green whatever is sent (checked on a Blade 16 with five other
    // colors), so Razer green is sent: what is asked for is what is shown.
    private static readonly byte[] StaticGreen = [StoreInLaptop, KeyboardLed, EffectStatic, 0x00, 0x00, 0x01, 0x44, 0xD6, 0x2C];

    // Logo mode values.
    private const byte LogoSteady = 0x00;
    private const byte LogoBreathing = 0x02;

    public Task<LightingState> ReadStateAsync() => Task.Run(ReadState);

    public Task SetKeyboardEffectAsync(KeyboardEffect effect) => Task.Run(() => SetKeyboardEffect(effect));

    public Task SetKeyboardBrightnessAsync(int percent) => Task.Run(() => SetKeyboardBrightness(percent));

    public Task<int> ReadKeyboardBrightnessAsync() => Task.Run(ReadKeyboardBrightness);

    public Task SetLogoAsync(LogoMode mode) => Task.Run(() => SetLogo(mode));

    public Task SetLogoBrightnessAsync(int percent) => Task.Run(() => SetLogoBrightness(percent));

    public Task SetKeyboardColorAsync(RgbColor color) => Task.Run(() => SetKeyboardColor(color));

    public Task SetKeyboardBreathingAsync(RgbColor color) => Task.Run(() => SetKeyboardBreathing(color));

    internal LightingState ReadState() => new(
        ReadKeyboardEffect(),
        LightingBrightness.ToPercent(ReadBrightness(KeyboardLed)),
        ReadLogo(),
        LightingBrightness.ToPercent(ReadBrightness(LogoLed)),
        offersColor ? ReadKeyboardColor() : null);

    /// <summary>A steady keyboard in <paramref name="color"/>. Only on models that offer a color.</summary>
    internal void SetKeyboardColor(RgbColor color)
    {
        SendStandardEffect([StandardEffectStatic, color.Red, color.Green, color.Blue], color, KeyboardEffect.StaticGreen);
    }

    /// <summary>The keyboard breathing in <paramref name="color"/>. Only on models that offer a color.</summary>
    internal void SetKeyboardBreathing(RgbColor color)
    {
        SendStandardEffect(
            [StandardEffectBreathing, StandardBreathingOneColor, color.Red, color.Green, color.Blue],
            color,
            KeyboardEffect.Breathing);
    }

    private void SendStandardEffect(byte[] arguments, RgbColor color, KeyboardEffect effect)
    {
        if (!offersColor)
            throw new InvalidOperationException("This laptop does not show a chosen keyboard color.");

        transport.Send(RazerCommands.SetStandardEffect, arguments);

        // Believe what the laptop says it is showing, not what was asked for.
        var shown = ReadKeyboardEffect();
        var now = ReadKeyboardColor();

        if (shown != effect || now != color)
        {
            AppLog.Error($"Asked for keyboard {effect} in {color} but the laptop reports {shown?.ToString() ?? "something else"} in {now?.ToString() ?? "no color"}.");
            throw new InvalidOperationException("The Razer Blade did not confirm the keyboard color.");
        }
    }

    // The standard effect answers [effect, ...]: a static effect as
    // [6, red, green, blue], a one-color breathing as [3, 1, red, green, blue].
    // Only those two have a color worth showing.
    private RgbColor? ReadKeyboardColor()
    {
        var response = ReadStandardEffect();

        return response[0] switch
        {
            StandardEffectStatic => new RgbColor(response[1], response[2], response[3]),
            StandardEffectBreathing when response[1] == StandardBreathingOneColor => new RgbColor(response[2], response[3], response[4]),
            _ => null
        };
    }

    private byte[] ReadStandardEffect()
    {
        var response = transport.Send(RazerCommands.GetStandardEffect, [0x00, 0x00, 0x00, 0x00, 0x00]);
        return [.. Enumerable.Range(0, 5).Select(index => RazerHidPacket.GetArgument(response, index))];
    }

    internal void SetKeyboardEffect(KeyboardEffect effect)
    {
        byte[] arguments = effect switch
        {
            KeyboardEffect.Off => [StoreInLaptop, KeyboardLed, EffectOff],
            KeyboardEffect.StaticGreen => StaticGreen,
            KeyboardEffect.Spectrum => [StoreInLaptop, KeyboardLed, EffectSpectrum],
            KeyboardEffect.Breathing => [StoreInLaptop, KeyboardLed, EffectBreathing],
            KeyboardEffect.Wave => [StoreInLaptop, KeyboardLed, EffectWave, WaveDirection],
            _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null)
        };

        transport.Send(RazerCommands.SetKeyboardEffect, arguments);

        // Believe what the laptop says it is doing, not what was asked for.
        var now = ReadKeyboardEffect();

        if (now != effect)
        {
            AppLog.Error($"Asked for keyboard effect {effect} but the laptop reports {now?.ToString() ?? "something else"}.");
            throw new InvalidOperationException($"The Razer Blade did not confirm the keyboard effect {effect}.");
        }
    }

    internal void SetLogo(LogoMode mode)
    {
        // Off is only the power switch; the mode is left as it was.
        if (mode != LogoMode.Off)
        {
            transport.SendAndConfirm(
                RazerCommands.SetLogoMode,
                [0x01, LogoLed, mode == LogoMode.Breathing ? LogoBreathing : LogoSteady],
                "the logo mode");
        }

        transport.SendAndConfirm(
            RazerCommands.SetLogoPower,
            [0x01, LogoLed, (byte)(mode == LogoMode.Off ? 0x00 : 0x01)],
            "the logo power");
    }

    internal void SetKeyboardBrightness(int percent) => SetBrightness(KeyboardLed, percent, "keyboard brightness");

    /// <summary>The keyboard's brightness, 0 to 100.</summary>
    internal int ReadKeyboardBrightness() => LightingBrightness.ToPercent(ReadBrightness(KeyboardLed));

    internal void SetLogoBrightness(int percent) => SetBrightness(LogoLed, percent, "logo brightness");

    private void SetBrightness(byte led, int percent, string description) =>
        transport.SendAndConfirm(
            RazerCommands.SetBrightness,
            [0x01, led, LightingBrightness.ToByte(percent)],
            description);

    private KeyboardEffect? ReadKeyboardEffect()
    {
        var response = transport.Send(RazerCommands.GetKeyboardEffect, [StoreInLaptop, KeyboardLed, 0x00]);

        if (RazerHidPacket.GetArgument(response, 1) != KeyboardLed)
            throw new InvalidOperationException("The keyboard effect response was for a different light.");

        // Effects set by other software (reactive, starlight) are not ones this
        // app offers: report "unknown", never a wrong one.
        return RazerHidPacket.GetArgument(response, 2) switch
        {
            EffectOff => KeyboardEffect.Off,
            EffectStatic => KeyboardEffect.StaticGreen,
            EffectBreathing => KeyboardEffect.Breathing,
            EffectSpectrum => KeyboardEffect.Spectrum,
            EffectWave => KeyboardEffect.Wave,
            _ => null
        };
    }

    private LogoMode? ReadLogo()
    {
        var power = ReadRegister(RazerCommands.GetLogoPower, LogoLed);

        if (power == 0x00)
            return LogoMode.Off;

        return ReadRegister(RazerCommands.GetLogoMode, LogoLed) switch
        {
            LogoSteady => LogoMode.On,
            LogoBreathing => LogoMode.Breathing,
            _ => null
        };
    }

    private byte ReadBrightness(byte led) => ReadRegister(RazerCommands.GetBrightness, led);

    // The lighting registers answer [1, led, value].
    private byte ReadRegister(ushort command, byte led)
    {
        var response = transport.Send(command, [0x01, led, 0x00]);

        if (RazerHidPacket.GetArgument(response, 1) != led)
            throw new InvalidOperationException($"The lighting response for 0x{command:X4} was for a different light.");

        return RazerHidPacket.GetArgument(response, 2);
    }
}
