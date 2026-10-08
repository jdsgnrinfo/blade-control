using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class RazerBladePlanTests
{
    [Fact]
    public void Installing_CopiesBalanced_SetsTheProcessor_NamesIt_AndMakesItActive()
    {
        var plans = new FakePlans();

        var refused = RazerBladePlan.Install(plans, spanish: false);

        Assert.Empty(refused);
        Assert.Equal((RazerBladePlan.Balanced, RazerBladePlan.Id), Assert.Single(plans.Copies));
        Assert.Equal(RazerBladePlan.Values.Count, plans.Values.Count);
        Assert.Contains((RazerBladePlan.Id, "PERFBOOSTMODE", 2u, 3u), plans.Values);
        Assert.Equal((RazerBladePlan.Id, "Razer Blade", RazerBladePlan.Description(false)), plans.Name);
        Assert.Equal(RazerBladePlan.Id, plans.ActiveId);
    }

    [Fact]
    public void InstallingAgain_UpdatesThePlan_InsteadOfAddingAnother()
    {
        var plans = new FakePlans();
        plans.Existing.Add(new PowerPlan(RazerBladePlan.Id, "Razer Blade"));

        RazerBladePlan.Install(plans, spanish: true);

        Assert.Empty(plans.Copies);
        Assert.Equal(RazerBladePlan.Values.Count, plans.Values.Count);
        Assert.Equal(RazerBladePlan.Description(true), plans.Name?.Description);
    }

    [Fact]
    public void ASettingThePcLacks_IsReported_ButTheEfficiencyCoresOneIsNot()
    {
        var plans = new FakePlans { Refuse = ["PERFEPP1", "CPMINCORES"] };

        var refused = RazerBladePlan.Install(plans, spanish: false);

        Assert.Equal(["CPMINCORES"], refused);
        Assert.Equal(RazerBladePlan.Id, plans.ActiveId);
    }

    private sealed class FakePlans : IPowerPlans
    {
        public List<PowerPlan> Existing { get; } = [];

        public List<(Guid Source, Guid Copy)> Copies { get; } = [];

        public List<(Guid Plan, string Setting, uint PluggedIn, uint OnBattery)> Values { get; } = [];

        public (Guid Plan, string Name, string Description)? Name { get; private set; }

        public Guid ActiveId { get; private set; }

        public HashSet<string> Refuse { get; init; } = [];

        public IReadOnlyList<PowerPlan> List() => Existing;

        public Guid Active() => ActiveId;

        public void Activate(Guid plan) => ActiveId = plan;

        public void Duplicate(Guid source, Guid copy)
        {
            Copies.Add((source, copy));
            Existing.Add(new PowerPlan(copy, "Copy"));
        }

        public bool SetProcessorValue(Guid plan, string setting, uint pluggedIn, uint onBattery)
        {
            if (Refuse.Contains(setting))
                return false;

            Values.Add((plan, setting, pluggedIn, onBattery));
            return true;
        }

        public void Rename(Guid plan, string name, string description) => Name = (plan, name, description);
    }
}
