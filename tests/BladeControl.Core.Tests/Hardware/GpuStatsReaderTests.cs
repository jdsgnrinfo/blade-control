using BladeControl.Core.Hardware;
using BladeControl.Core.Models;

namespace BladeControl.Core.Tests.Hardware;

public class GpuStatsReaderTests
{
    private const int ThisApp = 13576;
    private const long OneMegabyte = 1024 * 1024;

    [Fact]
    public void NobodyOnTheGpu_LetsItSleep() =>
        Assert.False(GpuStatsReader.SomeoneElseUses([], ThisApp));

    [Fact]
    public void ThisAppAndWindows_DoNotCount()
    {
        // As read with the GPU awake and only the monitor on it: System holds a little, this app nothing.
        GpuProcessUsage[] usage = [new(4, 4 * OneMegabyte), new(ThisApp, OneMegabyte)];

        Assert.False(GpuStatsReader.SomeoneElseUses(usage, ThisApp));
    }

    [Fact]
    public void AnotherApp_WithVideoMemoryOnIt_Counts() =>
        Assert.True(GpuStatsReader.SomeoneElseUses([new(4, 4 * OneMegabyte), new(20376, 7 * OneMegabyte)], ThisApp));

    [Fact]
    public void AnotherApp_WithNoVideoMemory_DoesNotCount() =>
        Assert.False(GpuStatsReader.SomeoneElseUses([new(3744, 0)], ThisApp));
}
