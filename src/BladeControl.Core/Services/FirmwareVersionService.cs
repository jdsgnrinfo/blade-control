using BladeControl.Core.Diagnostics;
using BladeControl.Core.Hardware;

namespace BladeControl.Core.Services;

/// <summary>
/// The firmware version of the laptop's controller, as Razer numbers it
/// ("1.1"). One read-only command; the version cannot change while Windows
/// runs, so the first answer is kept.
/// </summary>
internal sealed class FirmwareVersionService(IRazerTransport transport)
{
    private string? _version;
    private bool _settled;

    /// <summary>"1.1", or null when the laptop does not say.</summary>
    public string? Read()
    {
        if (_settled)
            return _version;

        try
        {
            _version = Decode(transport.Send(RazerCommands.GetFirmwareVersion, [0x00, 0x00]));
            _settled = true;
        }
        catch (RazerCommandNotSupportedException)
        {
            _settled = true;
            AppLog.Info("This laptop does not report its firmware version.");
        }

        // Any other failure (the laptop not found yet, say) is not kept, so the next call tries again.
        return _version;
    }

    /// <summary>
    /// The version from a reply: major and minor. Both 0, or both 255, are
    /// what an empty register reads as, so they are no version.
    /// </summary>
    internal static string? Decode(byte[] reply)
    {
        var major = RazerHidPacket.GetArgument(reply, 0);
        var minor = RazerHidPacket.GetArgument(reply, 1);

        return (major, minor) is (0, 0) or (0xFF, 0xFF) ? null : $"{major}.{minor}";
    }
}
