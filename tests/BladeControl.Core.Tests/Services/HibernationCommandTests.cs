using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class HibernationCommandTests
{
    [Fact]
    public void OtherArguments_AreNotTheCommand_SoTheAppStartsNormally()
    {
        var called = false;

        Assert.Null(HibernationCommand.TryRun([], _ => called = true));
        Assert.Null(HibernationCommand.TryRun([HibernationCommand.Switch], _ => called = true));
        Assert.Null(HibernationCommand.TryRun([HibernationCommand.Switch, "maybe"], _ => called = true));
        Assert.Null(HibernationCommand.TryRun([HibernationCommand.Switch, "off", "extra"], _ => called = true));
        Assert.False(called);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCommand_PassesOnOrOff_AndReportsSuccess(bool enabled)
    {
        bool? asked = null;

        var exitCode = HibernationCommand.TryRun(HibernationCommand.Arguments(enabled), value => { asked = value; return true; });

        Assert.Equal(enabled, asked);
        Assert.Equal(HibernationCommand.Success, exitCode);
    }

    [Fact]
    public void ARefusalOrAFailure_ReportsFailed()
    {
        Assert.Equal(HibernationCommand.Failed, HibernationCommand.TryRun(HibernationCommand.Arguments(false), _ => false));
        Assert.Equal(HibernationCommand.Failed, HibernationCommand.TryRun(HibernationCommand.Arguments(false), _ => throw new InvalidOperationException()));
    }
}
