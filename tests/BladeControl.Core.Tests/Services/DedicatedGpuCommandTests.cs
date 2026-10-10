using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class DedicatedGpuCommandTests
{
    private const string Id = @"PCI\VEN_10DE&DEV_1F95&SUBSYS_00000000&REV_A1\4&1234&0&0008";

    [Fact]
    public void OtherArguments_AreNotTheCommand_SoTheAppStartsNormally()
    {
        var called = false;

        Assert.Null(DedicatedGpuCommand.TryRun([], () => Id, (_, _) => { called = true; return 0; }));
        Assert.Null(DedicatedGpuCommand.TryRun([DedicatedGpuCommand.Switch], () => Id, (_, _) => { called = true; return 0; }));
        Assert.Null(DedicatedGpuCommand.TryRun([DedicatedGpuCommand.Switch, "maybe"], () => Id, (_, _) => { called = true; return 0; }));
        Assert.False(called);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCommand_TurnsTheCardFoundOnOrOff_AndReportsSuccess(bool enabled)
    {
        (string Id, bool Enabled)? asked = null;

        var exitCode = DedicatedGpuCommand.TryRun(DedicatedGpuCommand.Arguments(enabled), () => Id, (id, value) => { asked = (id, value); return 0; });

        Assert.Equal((Id, enabled), asked);
        Assert.Equal(DedicatedGpuCommand.Success, exitCode);
    }

    [Fact]
    public void WindowsAskingForARestart_IsReportedAsSuch() =>
        Assert.Equal(DedicatedGpuCommand.RestartNeeded,
            DedicatedGpuCommand.TryRun(DedicatedGpuCommand.Arguments(false), () => Id, (_, _) => 3010));

    [Fact]
    public void NoCard_ChangesNothing()
    {
        var called = false;

        Assert.Equal(DedicatedGpuCommand.NoDevice,
            DedicatedGpuCommand.TryRun(DedicatedGpuCommand.Arguments(false), () => null, (_, _) => { called = true; return 0; }));
        Assert.False(called);
    }

    [Fact]
    public void ARefusalOrAFailure_ReportsFailed()
    {
        Assert.Equal(DedicatedGpuCommand.Failed, DedicatedGpuCommand.TryRun(DedicatedGpuCommand.Arguments(false), () => Id, (_, _) => 5));
        Assert.Equal(DedicatedGpuCommand.Failed, DedicatedGpuCommand.TryRun(DedicatedGpuCommand.Arguments(false), () => Id, (_, _) => throw new InvalidOperationException()));
    }
}
