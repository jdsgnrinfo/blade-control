using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class DgpuTextTests
{
    private static DgpuApp App(string name, long megabytes, int pid) =>
        new(pid, name, megabytes * 1024 * 1024, DgpuAppVerdict.Close);

    [Fact]
    public void Confirmation_ListsEveryAppItWillAskToClose_AndSaysTheyCanAskToSave()
    {
        var text = DgpuText.BuildConfirmation([App("blender", 800, 1), App("msedge", 150, 2)]);

        Assert.Contains("blender (800 MB)", text);
        Assert.Contains("msedge (150 MB)", text);
        Assert.Contains("save your work", text);
    }

    [Fact]
    public void Confirmation_ShowsSeveralInstancesOfOneAppOnce()
    {
        var text = DgpuText.BuildConfirmation([App("msedge", 100, 1), App("msedge", 60, 2)]);

        Assert.Contains("msedge (2 instances, 160 MB)", text);
    }

    [Theory]
    [InlineData((int)DgpuFreeUpOutcome.NoDedicatedGpu, "No dedicated GPU")]
    [InlineData((int)DgpuFreeUpOutcome.ExternalDisplay, "external display is connected")]
    [InlineData((int)DgpuFreeUpOutcome.NothingToClose, "No apps that can be closed")]
    [InlineData((int)DgpuFreeUpOutcome.ConditionsChanged, "nothing was closed")]
    [InlineData((int)DgpuFreeUpOutcome.Busy, "Already checking")]
    [InlineData((int)DgpuFreeUpOutcome.Failed, "log")]
    public void EveryOutcomeThatClosedNothing_HasAnExplanation(int outcome, string expected) =>
        Assert.Contains(expected, DgpuText.DescribeOutcome((DgpuFreeUpOutcome)outcome));

    [Theory]
    [InlineData((int)DgpuFreeUpOutcome.Closed)]
    [InlineData((int)DgpuFreeUpOutcome.Declined)]
    public void OutcomesThatSpeakForThemselves_AreNotExplained(int outcome) =>
        Assert.Null(DgpuText.DescribeOutcome((DgpuFreeUpOutcome)outcome));

    [Fact]
    public void ExternalDisplayMessage_TellsTheUserWhatToDo() =>
        Assert.Contains("Disconnect it and try again", DgpuText.DescribeOutcome(DgpuFreeUpOutcome.ExternalDisplay));

    [Fact]
    public void StateLine_SaysAwakeOrAsleep_AndNothingWhenUnknown()
    {
        Assert.Equal("Awake now", DgpuText.StateLine(true));
        Assert.StartsWith("Asleep now", DgpuText.StateLine(false));
        Assert.Equal(string.Empty, DgpuText.StateLine(null));
    }

    [Fact]
    public void UsersLine_LeavesWindowsOut_AndSaysWhenNoAppIsUsingIt()
    {
        // As scanned: System is in session 0, dwm is protected.
        var system = new DgpuApp(4, "System", 4 * 1024 * 1024, DgpuAppVerdict.OtherSession);
        var dwm = new DgpuApp(1732, "dwm", 2 * 1024 * 1024, DgpuAppVerdict.Protected);

        Assert.Equal("No app is using it.", DgpuText.UsersLine([]));
        Assert.Equal("No app is using it.", DgpuText.UsersLine([system, dwm]));
    }

    [Fact]
    public void UsersLine_NamesTerminalsAndEditors_ThatAreNeverClosed()
    {
        var terminal = new DgpuApp(20376, "powershell", 7 * 1024 * 1024, DgpuAppVerdict.Protected);

        Assert.Equal("Used by: powershell", DgpuText.UsersLine([terminal]));
    }

    [Fact]
    public void UsersLine_NamesEachAppOnce_TheBiggestFirst()
    {
        var text = DgpuText.UsersLine([App("msedge", 60, 1), App("blender", 800, 2), App("msedge", 50, 3)]);

        Assert.Equal("Used by: blender, msedge", text);
    }

    [Fact]
    public void UsersLine_IncludesAppsItWouldNotClose()
    {
        var helper = new DgpuApp(5, "NVIDIA Broadcast", 30 * 1024 * 1024, DgpuAppVerdict.NoWindow);

        Assert.Equal("Used by: NVIDIA Broadcast", DgpuText.UsersLine([helper]));
    }

    [Fact]
    public void UsersLine_NamesThree_AndCountsTheRest() =>
        Assert.Equal(
            "Used by: a, b, c and 2 more",
            DgpuText.UsersLine([App("a", 50, 1), App("b", 40, 2), App("c", 30, 3), App("d", 20, 4), App("e", 10, 5)]));
}
