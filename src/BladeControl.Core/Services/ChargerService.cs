using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;
using BladeControl.Core.Localization;

namespace BladeControl.Core.Services;

/// <summary>
/// Which charger the laptop's controller says is powering it: USB-C and its
/// watts, or the laptop's own charger. One read-only command.
/// </summary>
/// <remarks>
/// The codes come from OpenBlade's captures of the Blade 16 (2025 and 2026):
/// 2 to 7 are USB-C at 45 to 100 W, 0x11 and 0x12 the 280 W and 330 W
/// chargers. A Blade 15 Base (2020) answers 0x0D with its own charger, a code
/// not in those captures: any code not known is shown as the main charger,
/// without watts, rather than guessed.
/// </remarks>
internal sealed class ChargerService(IRazerTransport transport)
{
    private bool _isSupported = true;

    /// <summary>
    /// The charger in, as the panel shows it ("USB-C · 65 W"), or null on
    /// battery or when the laptop does not say.
    /// </summary>
    public string? Read()
    {
        if (!_isSupported)
            return null;

        try
        {
            return Describe(RazerHidPacket.GetArgument(transport.Send(RazerCommands.GetPowerSource, [0x00, 0x00]), 0));
        }
        catch (RazerCommandNotSupportedException)
        {
            _isSupported = false;
            AppLog.Info("This laptop does not say which charger is in.");
            return null;
        }
    }

    /// <summary>The charger a source code stands for; null for the battery.</summary>
    internal static string? Describe(byte source) => source switch
    {
        0x00 => null,
        0x02 => UsbC(45),
        0x03 => UsbC(60),
        0x04 => UsbC(65),
        0x05 => UsbC(80),
        0x06 => UsbC(90),
        0x07 => UsbC(100),
        0x11 => Main(280),
        0x12 => Main(330),
        _ => L.T("Main")
    };

    private static string UsbC(int watts) => $"USB-C · {watts} W";

    private static string Main(int watts) => $"{L.T("Main")} · {watts} W";
}
