using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class LidCloseOverrideTests
{
    private static readonly Guid Plan = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid OtherPlan = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    // A power plan whose lid sleeps (1) both plugged in and on battery.
    private sealed class Rig : IPowerPlanValue
    {
        public Guid Active = Plan;
        public Dictionary<Guid, (uint PluggedIn, uint OnBattery)> Plans { get; } = new() { [Plan] = (1, 1), [OtherPlan] = (1, 1) };
        public bool Fail;
        public int Writes;
        public SavedPlanValue? Persisted;
        public LidCloseOverride Service { get; }

        public Rig(SavedPlanValue? saved = null)
        {
            Persisted = saved;
            Service = new LidCloseOverride(this, saved, value => Persisted = value);
        }

        public (uint, uint) Lid(Guid plan) => Plans[plan];

        public SavedPlanValue Read() => new(Active, Plans[Active].PluggedIn, Plans[Active].OnBattery);

        public void Write(Guid scheme, uint pluggedIn, uint onBattery)
        {
            if (Fail)
                throw new InvalidOperationException("The plan could not be written.");

            Writes++;
            Plans[scheme] = (pluggedIn, onBattery);
        }
    }

    [Fact]
    public void WithAMonitor_ClosingTheLidDoesNothingPluggedIn_AndStillSleepsOnBattery()
    {
        var rig = new Rig();

        rig.Service.Update(enabled: true, externalDisplay: true);

        Assert.Equal((0u, 1u), rig.Lid(Plan));
        Assert.Equal(new SavedPlanValue(Plan, 1, 1), rig.Persisted);
        Assert.True(rig.Service.IsActive);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void WithoutTheOptionOrAMonitor_NothingChanges(bool enabled, bool externalDisplay)
    {
        var rig = new Rig();

        rig.Service.Update(enabled, externalDisplay);

        Assert.Equal((1u, 1u), rig.Lid(Plan));
        Assert.Equal(0, rig.Writes);
        Assert.Null(rig.Persisted);
    }

    [Fact]
    public void TheMonitorGoing_GivesTheLidActionBack()
    {
        var rig = new Rig();
        rig.Service.Update(true, true);

        rig.Service.Update(true, externalDisplay: false);

        Assert.Equal((1u, 1u), rig.Lid(Plan));
        Assert.Null(rig.Persisted);
        Assert.False(rig.Service.IsActive);
    }

    [Fact]
    public void TurningTheOptionOff_GivesTheLidActionBack()
    {
        var rig = new Rig();
        rig.Service.Update(true, true);

        rig.Service.Update(enabled: false, true);

        Assert.Equal((1u, 1u), rig.Lid(Plan));
    }

    [Fact]
    public void Restore_GivesItBack_AndARecheckThenLeavesItAlone()
    {
        var rig = new Rig();
        rig.Service.Update(true, true);

        rig.Service.Restore();
        rig.Service.Recheck();

        Assert.Equal((1u, 1u), rig.Lid(Plan));
        Assert.Null(rig.Persisted);
    }

    [Fact]
    public void APlanThatAlreadyDoesNothing_IsLeftAlone_AndNothingIsGivenBackLater()
    {
        var rig = new Rig();
        rig.Plans[Plan] = (0, 1);

        rig.Service.Update(true, true);
        rig.Service.Update(true, false);

        Assert.Equal((0u, 1u), rig.Lid(Plan));
        Assert.Equal(0, rig.Writes);
        Assert.Null(rig.Persisted);
    }

    [Fact]
    public void AnotherPlanBecomingActive_GetsTheChange_AndTheFirstGetsItsOwnBack()
    {
        var rig = new Rig();
        rig.Service.Update(true, true);

        rig.Active = OtherPlan;
        rig.Service.Recheck();

        Assert.Equal((1u, 1u), rig.Lid(Plan));
        Assert.Equal((0u, 1u), rig.Lid(OtherPlan));
        Assert.Equal(new SavedPlanValue(OtherPlan, 1, 1), rig.Persisted);
    }

    [Fact]
    public void ALidActionPutBackBySomethingElse_IsChangedAgain_AndTheOriginalStillComesBack()
    {
        var rig = new Rig();
        rig.Service.Update(true, true);

        rig.Plans[Plan] = (1, 1);
        rig.Service.Recheck();
        Assert.Equal((0u, 1u), rig.Lid(Plan));

        rig.Service.Update(true, false);
        Assert.Equal((1u, 1u), rig.Lid(Plan));
    }

    [Fact]
    public void ASavedValueFromBeforeARestart_IsGivenBackWhenTheMonitorIsNoLongerThere()
    {
        var rig = new Rig(new SavedPlanValue(Plan, 1, 1));
        rig.Plans[Plan] = (0, 1); // As the app left it.

        rig.Service.Update(true, externalDisplay: false);

        Assert.Equal((1u, 1u), rig.Lid(Plan));
        Assert.Null(rig.Persisted);
    }

    [Fact]
    public void AFailedWrite_KeepsTheSavedValue_ForTheNextTry()
    {
        var rig = new Rig();
        rig.Service.Update(true, true);

        rig.Fail = true;
        rig.Service.Update(true, false);
        Assert.Equal(new SavedPlanValue(Plan, 1, 1), rig.Persisted);

        rig.Fail = false;
        rig.Service.Update(true, false);
        Assert.Equal((1u, 1u), rig.Lid(Plan));
        Assert.Null(rig.Persisted);
    }
}
