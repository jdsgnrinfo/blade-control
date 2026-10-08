using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class SilentPlanOverrideTests
{
    private static readonly Guid Plan = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    // A power plan with boost Aggressive (2) plugged in and Enabled (1) on battery.
    private sealed class Rig : IPowerPlanValue
    {
        public uint PluggedIn = 2;
        public uint OnBattery = 1;
        public bool Fail;
        public int Writes;
        public SavedPlanValue? Persisted;
        public SilentPlanOverride Service { get; }

        public Rig(SavedPlanValue? saved = null, uint silentValue = 0)
        {
            Persisted = saved;
            Service = new SilentPlanOverride(this, silentValue, "CPU boost", saved, value => Persisted = value);
        }

        // The plan Windows has active; Plan's values are PluggedIn and OnBattery, any other's are in Others.
        public Guid Active = Plan;
        public Dictionary<Guid, (uint PluggedIn, uint OnBattery)> Others { get; } = [];

        public SavedPlanValue Read() =>
            Active == Plan ? new(Plan, PluggedIn, OnBattery) : new(Active, Others[Active].PluggedIn, Others[Active].OnBattery);

        public void Write(Guid scheme, uint pluggedIn, uint onBattery)
        {
            if (Fail)
                throw new InvalidOperationException("The plan could not be written.");

            Writes++;

            if (scheme == Plan)
                (PluggedIn, OnBattery) = (pluggedIn, onBattery);
            else
                Others[scheme] = (pluggedIn, onBattery);
        }
    }

    [Fact]
    public void ABoostPutBackWhileInSilent_IsTakenAwayAgain_AndTheOriginalStillComesBack()
    {
        var rig = new Rig();
        rig.Service.OnModeChanged(PerformanceMode.Silent);

        (rig.PluggedIn, rig.OnBattery) = (2, 2); // Windows or Razer's software, meanwhile.
        rig.Service.Recheck();
        Assert.Equal((0u, 0u), (rig.PluggedIn, rig.OnBattery));

        rig.Service.OnModeChanged(PerformanceMode.Balanced);
        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));
    }

    [Fact]
    public void AnotherPlanBecomingActiveInSilent_LosesItsBoost_AndTheFirstPlanGetsItsBack()
    {
        var other = new Guid("27b6984e-62f8-4534-b530-b5c18eb33484");
        var rig = new Rig();
        rig.Others[other] = (2, 2);
        rig.Service.OnModeChanged(PerformanceMode.Silent);

        rig.Active = other; // The Idle option, say.
        rig.Service.Recheck();

        Assert.Equal((0u, 0u), rig.Others[other]);
        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));

        rig.Service.OnModeChanged(PerformanceMode.Balanced);
        Assert.Equal((2u, 2u), rig.Others[other]);
    }

    [Fact]
    public void Recheck_OutsideSilent_ChangesNothing()
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Balanced);
        rig.Service.Recheck();

        Assert.Equal(0, rig.Writes);
    }

    [Fact]
    public void Recheck_WhenTheBoostIsStillOff_WritesNothing()
    {
        var rig = new Rig();
        rig.Service.OnModeChanged(PerformanceMode.Silent);
        var writes = rig.Writes;

        rig.Service.Recheck();

        Assert.Equal(writes, rig.Writes);
    }

    [Fact]
    public void Silent_TurnsTheBoostOff_AndSavesWhatItReplaced()
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Silent);

        Assert.Equal((0u, 0u), (rig.PluggedIn, rig.OnBattery));
        Assert.Equal(new SavedPlanValue(Plan, 2, 1), rig.Persisted);
    }

    [Theory]
    [InlineData((byte)PerformanceMode.Balanced)]
    [InlineData((byte)PerformanceMode.Custom)]
    public void BalancedOrCustom_GiveTheBoostBack(byte modeByte)
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Silent);
        rig.Service.OnModeChanged((PerformanceMode)modeByte);

        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));
        Assert.Null(rig.Persisted);
    }

    [Fact]
    public void AnUnknownModeOrGaming_LeavesTheBoostOff()
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Silent);
        rig.Service.OnModeChanged(null);
        rig.Service.OnModeChanged(PerformanceMode.Gaming);

        Assert.Equal((0u, 0u), (rig.PluggedIn, rig.OnBattery));
    }

    [Fact]
    public void Silent_ReportedAgain_KeepsTheFirstSavedValues()
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Silent);
        rig.Service.OnModeChanged(PerformanceMode.Silent);
        rig.Service.OnModeChanged(PerformanceMode.Balanced);

        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));
    }

    [Fact]
    public void AfterARestart_TheSavedValuesStillComeBackWithBalanced()
    {
        // The app was closed in Silent: the plan was left with the boost off.
        var rig = new Rig(saved: new SavedPlanValue(Plan, 3, 1)) { PluggedIn = 0, OnBattery = 0 };

        rig.Service.OnModeChanged(PerformanceMode.Balanced);

        Assert.Equal((3u, 1u), (rig.PluggedIn, rig.OnBattery));
        Assert.Null(rig.Persisted);
    }

    [Fact]
    public void Off_LeavesSilentAlone()
    {
        var rig = new Rig();

        rig.Service.SetEnabled(false);
        rig.Service.OnModeChanged(PerformanceMode.Silent);

        Assert.Equal(0, rig.Writes);
    }

    [Fact]
    public void TurningItOffInSilent_GivesTheBoostBack_AndOnAgain_TakesItAway()
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Silent);
        rig.Service.SetEnabled(false);
        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));

        rig.Service.SetEnabled(true);
        Assert.Equal((0u, 0u), (rig.PluggedIn, rig.OnBattery));
    }

    [Fact]
    public void AFailedGiveBack_KeepsTheSavedValues_ForTheNextTry()
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Silent);
        rig.Fail = true;
        rig.Service.OnModeChanged(PerformanceMode.Balanced);

        Assert.NotNull(rig.Persisted);

        rig.Fail = false;
        rig.Service.OnModeChanged(PerformanceMode.Custom);

        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));
        Assert.Null(rig.Persisted);
    }

    [Fact]
    public void Silent_WritesItsOwnValue_ForEachPowerSource()
    {
        // The energy preference: 80 leans towards efficiency.
        var rig = new Rig(silentValue: 80);

        rig.Service.OnModeChanged(PerformanceMode.Silent);
        Assert.Equal((80u, 80u), (rig.PluggedIn, rig.OnBattery));

        rig.Service.OnModeChanged(PerformanceMode.Balanced);
        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));
    }

    [Fact]
    public void Restore_GivesTheBoostBackInAnyMode()
    {
        var rig = new Rig();

        rig.Service.OnModeChanged(PerformanceMode.Silent);
        rig.Service.Restore();

        Assert.Equal((2u, 1u), (rig.PluggedIn, rig.OnBattery));
        Assert.Null(rig.Persisted);
    }
}
