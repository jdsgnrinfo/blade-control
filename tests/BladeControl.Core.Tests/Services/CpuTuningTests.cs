using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class CpuTuningTests
{
    private static readonly Guid Plan = new("e1811cef-6588-4a28-876f-cc20c84725f8");

    // One plan setting, starting at the Razer Blade plan's values.
    private sealed class Value(uint pluggedIn, uint onBattery) : IPowerPlanValue
    {
        public uint PluggedIn { get; private set; } = pluggedIn;
        public uint OnBattery { get; private set; } = onBattery;
        public int Writes { get; private set; }

        public SavedPlanValue Read() => new(Plan, PluggedIn, OnBattery);

        public void Write(Guid scheme, uint pluggedIn, uint onBattery)
        {
            Writes++;
            PluggedIn = pluggedIn;
            OnBattery = onBattery;
        }
    }

    private sealed class Mode : IPowerMode
    {
        public bool Balanced { get; set; }

        public bool IsBalanced() => Balanced;

        public void SetBalanced() => Balanced = true;
    }

    private sealed class Rig
    {
        public Dictionary<CpuSetting, Value> Values { get; } = Enum.GetValues<CpuSetting>()
            .ToDictionary(setting => setting, setting => new Value(CpuTuning.Default(setting).PluggedIn, CpuTuning.Default(setting).OnBattery));

        public Mode Mode { get; } = new();

        public CpuTuning Tuning(bool pluggedInNow = true, CpuValues? pluggedIn = null, CpuValues? onBattery = null) =>
            new(setting => Values[setting], Mode, () => pluggedInNow, pluggedIn, onBattery);
    }

    [Theory]
    [InlineData(CpuSetting.Epp, 0, 0)]
    [InlineData(CpuSetting.Epp, 4, 0)]
    [InlineData(CpuSetting.Epp, 16, 20)]
    [InlineData(CpuSetting.Epp, 53, 50)]
    [InlineData(CpuSetting.CoreParking, 140, 100)]
    [InlineData(CpuSetting.FrequencyScaling, 0, 10)]
    internal void Values_snap_to_steps_of_10(CpuSetting setting, double value, int snapped) =>
        Assert.Equal(snapped, CpuTuning.Snap(setting, value));

    [Fact]
    public void Nothing_chosen_leaves_the_plan_and_the_power_mode_alone()
    {
        var rig = new Rig();

        Assert.True(rig.Tuning().Recheck());

        Assert.All(rig.Values.Values, value => Assert.Equal(0, value.Writes));
        Assert.False(rig.Mode.Balanced);
    }

    [Fact]
    public void Choosing_one_source_keeps_the_other()
    {
        var rig = new Rig();

        Assert.True(rig.Tuning().Set(CpuSetting.CoreParking, forPluggedIn: true, 40));

        Assert.Equal(40u, rig.Values[CpuSetting.CoreParking].PluggedIn);
        Assert.Equal(30u, rig.Values[CpuSetting.CoreParking].OnBattery);
        Assert.Equal(0, rig.Values[CpuSetting.Epp].Writes);
    }

    [Fact]
    public void A_plan_that_drifted_gets_the_chosen_values_back_once()
    {
        var rig = new Rig();
        var tuning = rig.Tuning(pluggedIn: new CpuValues(Epp: 60, TurboBoost: 0));

        tuning.Recheck();
        tuning.Recheck();

        Assert.Equal(60u, rig.Values[CpuSetting.Epp].PluggedIn);
        Assert.Equal(0u, rig.Values[CpuSetting.TurboBoost].PluggedIn);
        Assert.Equal(1, rig.Values[CpuSetting.Epp].Writes);
    }

    [Fact]
    public void A_choice_for_the_source_in_use_puts_Windows_power_mode_on_Balanced()
    {
        var rig = new Rig();

        rig.Tuning(pluggedInNow: true, pluggedIn: new CpuValues(Epp: 40)).Recheck();

        Assert.True(rig.Mode.Balanced);
    }

    [Fact]
    public void The_power_mode_is_left_alone_for_a_source_with_nothing_chosen()
    {
        var rig = new Rig();

        rig.Tuning(pluggedInNow: false, pluggedIn: new CpuValues(Epp: 40)).Recheck();

        Assert.False(rig.Mode.Balanced);
    }

    [Fact]
    public void Reset_forgets_the_choices_and_puts_the_Razer_Blade_values_back()
    {
        var rig = new Rig();
        var tuning = rig.Tuning(pluggedIn: new CpuValues(Epp: 80, CoreParking: 20), onBattery: new CpuValues(Epp: 100));
        tuning.Recheck();

        Assert.True(tuning.Reset(forPluggedIn: true));

        Assert.False(tuning.PluggedIn.HasAny());
        Assert.Equal(20u, rig.Values[CpuSetting.Epp].PluggedIn);
        Assert.Equal(100u, rig.Values[CpuSetting.CoreParking].PluggedIn);
        Assert.Equal(100u, rig.Values[CpuSetting.Epp].OnBattery);
    }
}
