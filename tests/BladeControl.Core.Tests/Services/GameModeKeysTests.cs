using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class GameModeKeysTests
{
    [Theory]
    [InlineData(GameModeKeys.LeftWindows)]
    [InlineData(GameModeKeys.RightWindows)]
    public void TheWindowsKeys_AreBlockedDownAndUp(int key)
    {
        var keys = new GameModeKeys();

        Assert.True(keys.ShouldBlock(key, down: true, altDown: false));
        Assert.True(keys.ShouldBlock(key, down: false, altDown: false));
    }

    [Fact]
    public void OtherKeys_GoThrough()
    {
        var keys = new GameModeKeys { Options = new(BlockAltTab: true, BlockAltF4: true) };

        Assert.False(keys.ShouldBlock(0x41, true, false)); // A
        Assert.False(keys.ShouldBlock(0x41, false, false));
        Assert.False(keys.ShouldBlock(GameModeKeys.Tab, true, altDown: false)); // Tab alone
        Assert.False(keys.ShouldBlock(GameModeKeys.F4, true, altDown: false));  // F4 alone
    }

    [Fact]
    public void AltTabAndAltF4_GoThroughUnlessChosen()
    {
        var keys = new GameModeKeys();

        Assert.False(keys.ShouldBlock(GameModeKeys.Tab, true, altDown: true));
        Assert.False(keys.ShouldBlock(GameModeKeys.F4, true, altDown: true));
    }

    [Fact]
    public void AltTabAndAltF4_AreBlockedWhenChosen()
    {
        var keys = new GameModeKeys { Options = new(BlockAltTab: true, BlockAltF4: true) };

        Assert.True(keys.ShouldBlock(GameModeKeys.Tab, true, altDown: true));
        Assert.True(keys.ShouldBlock(GameModeKeys.Tab, false, altDown: true));
        Assert.True(keys.ShouldBlock(GameModeKeys.F4, true, altDown: true));
        Assert.True(keys.ShouldBlock(GameModeKeys.F4, false, altDown: false)); // Alt let go first
    }

    [Fact]
    public void AKeyThatWentDownUnblocked_IsLetUpNormally()
    {
        var keys = new GameModeKeys();

        // Tab went down without Alt, then Alt+Tab was chosen; its release must still reach Windows.
        Assert.False(keys.ShouldBlock(GameModeKeys.Tab, true, altDown: false));
        keys.Options = new(BlockAltTab: true);

        Assert.False(keys.ShouldBlock(GameModeKeys.Tab, false, altDown: true));
    }

    [Fact]
    public void AKeyBlockedDown_IsBlockedUpOnlyOnce()
    {
        var keys = new GameModeKeys();

        keys.ShouldBlock(GameModeKeys.LeftWindows, true, false);
        Assert.True(keys.ShouldBlock(GameModeKeys.LeftWindows, false, false));
        Assert.False(keys.ShouldBlock(GameModeKeys.LeftWindows, false, false));
    }
}
