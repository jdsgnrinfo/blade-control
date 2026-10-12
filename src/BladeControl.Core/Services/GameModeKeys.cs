namespace BladeControl.Core.Services;

/// <summary>What game mode blocks: the Windows keys always, Alt+Tab and Alt+F4 when chosen.</summary>
internal sealed record GameModeOptions(bool BlockAltTab = false, bool BlockAltF4 = false);

/// <summary>
/// Decides, key by key, what game mode keeps from Windows. A key is blocked
/// as it goes down and then again as it comes up; a key that went down before
/// game mode started (or with its combination not held) is let up normally,
/// so Windows never thinks a key is stuck.
/// </summary>
internal sealed class GameModeKeys
{
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;
    public const int Tab = 0x09;
    public const int F4 = 0x73;

    private readonly HashSet<int> _blockedDown = [];

    public GameModeOptions Options { get; set; } = new();

    /// <summary>Whether to keep this key press or release from Windows. <paramref name="altDown"/> is whether Alt is held.</summary>
    public bool ShouldBlock(int virtualKey, bool down, bool altDown)
    {
        if (!down)
            return _blockedDown.Remove(virtualKey);

        var block = virtualKey switch
        {
            LeftWindows or RightWindows => true,
            Tab => altDown && Options.BlockAltTab,
            F4 => altDown && Options.BlockAltF4,
            _ => false
        };

        if (block)
            _blockedDown.Add(virtualKey);

        return block;
    }
}
