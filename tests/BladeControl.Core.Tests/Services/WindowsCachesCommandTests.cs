using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class WindowsCachesCommandTests
{
    [Fact]
    public void OtherArguments_AreNotTheCommand_SoTheAppStartsNormally()
    {
        var cleared = false;

        Assert.Null(WindowsCachesCommand.TryRun([], () => cleared = true));
        Assert.Null(WindowsCachesCommand.TryRun(["--razer-services", "stop"], () => cleared = true));
        Assert.Null(WindowsCachesCommand.TryRun([WindowsCachesCommand.Switch, "extra"], () => cleared = true));
        Assert.False(cleared);
    }

    [Fact]
    public void TheCommand_ClearsTheCaches_AndReportsSuccess()
    {
        var cleared = false;

        var exitCode = WindowsCachesCommand.TryRun(WindowsCachesCommand.Arguments(), () => cleared = true);

        Assert.True(cleared);
        Assert.Equal(WindowsCachesCommand.Success, exitCode);
    }

    [Fact]
    public void ARefusalOrAFailure_ReportsFailed()
    {
        Assert.Equal(WindowsCachesCommand.Failed, WindowsCachesCommand.TryRun(WindowsCachesCommand.Arguments(), () => false));
        Assert.Equal(WindowsCachesCommand.Failed, WindowsCachesCommand.TryRun(WindowsCachesCommand.Arguments(), () => throw new InvalidOperationException()));
    }
}
