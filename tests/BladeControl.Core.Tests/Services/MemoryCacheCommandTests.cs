using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class MemoryCacheCommandTests
{
    [Fact]
    public void OtherArguments_AreNotTheCommand_SoTheAppStartsNormally()
    {
        var purged = false;

        Assert.Null(MemoryCacheCommand.TryRun([], () => purged = true));
        Assert.Null(MemoryCacheCommand.TryRun(["--razer-services", "stop"], () => purged = true));
        Assert.Null(MemoryCacheCommand.TryRun([MemoryCacheCommand.Switch, "extra"], () => purged = true));
        Assert.False(purged);
    }

    [Fact]
    public void TheCommand_ClearsTheCache_AndReportsSuccess()
    {
        var purged = false;

        var exitCode = MemoryCacheCommand.TryRun(MemoryCacheCommand.Arguments(), () => purged = true);

        Assert.True(purged);
        Assert.Equal(MemoryCacheCommand.Success, exitCode);
    }

    [Fact]
    public void ARefusalOrAFailure_ReportsFailed()
    {
        Assert.Equal(MemoryCacheCommand.Failed, MemoryCacheCommand.TryRun(MemoryCacheCommand.Arguments(), () => false));
        Assert.Equal(MemoryCacheCommand.Failed, MemoryCacheCommand.TryRun(MemoryCacheCommand.Arguments(), () => throw new InvalidOperationException()));
    }
}
