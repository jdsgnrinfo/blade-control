using BladeControl.Core.Hardware;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class BatteryDetailsTextTests
{
    // The figures a Blade 15 Base (2020) reported while charging.
    private static readonly BatteryDetails Charging = new(
        PluggedIn: true, Charging: true, Discharging: false,
        RemainingMilliwattHours: 28906, FullChargeMilliwattHours: 55409, DesignMilliwattHours: 65003,
        RateMilliwatts: 45018, VoltageMillivolts: 16442, EstimatedSecondsLeft: null, CycleCount: null,
        Name: "Blade", Manufacturer: "Razer", Chemistry: "Li-I");

    private static readonly BatteryDetails Discharging = Charging with
    {
        PluggedIn = false, Charging = false, Discharging = true, RateMilliwatts = -18200
    };

    [Fact]
    public void Header_ShowsTheChargePercentAndAShortStatus()
    {
        Assert.Equal(52, BatteryDetailsText.ChargePercent(Charging));
        Assert.Equal("Charging", BatteryDetailsText.HeaderStatus(Charging));
        Assert.Equal("On battery", BatteryDetailsText.HeaderStatus(Discharging));
        Assert.Equal("Plugged in", BatteryDetailsText.HeaderStatus(Charging with { Charging = false }));
    }

    [Fact]
    public void Health_IsTheFullChargeCapacityOverTheDesignCapacity()
    {
        Assert.Equal(85, BatteryDetailsText.HealthPercent(Charging));
        Assert.Equal("55.4 Wh of 65.0 Wh (factory)", BatteryDetailsText.HealthCaption(Charging));
    }

    [Fact]
    public void Health_IsLeftOutWithoutADesignCapacity()
    {
        var noDesign = Charging with { DesignMilliwattHours = null };

        Assert.Null(BatteryDetailsText.HealthPercent(noDesign));
        Assert.Null(BatteryDetailsText.HealthCaption(noDesign));
    }

    [Fact]
    public void Charging_ShowsTheRateInAndTheTimeToFull()
    {
        Assert.Equal(("45.0 W", "Charging"), BatteryDetailsText.PowerParts(Charging));
        Assert.Equal(("35 min", "to full"), BatteryDetailsText.TimeParts(Charging)); // 26.5 Wh at 45 W
    }

    [Fact]
    public void OnBattery_ShowsTheDrawAndTheTimeLeft()
    {
        Assert.Equal(("18.2 W", "Using"), BatteryDetailsText.PowerParts(Discharging));
        Assert.Equal(("1 h 35 min", "left"), BatteryDetailsText.TimeParts(Discharging)); // 28.9 Wh at 18.2 W
    }

    [Fact]
    public void PowerLine_PutsTheDirectionBeforeTheWatts_AndIsNullWithoutARate()
    {
        Assert.Equal("Using 18.2 W", BatteryDetailsText.PowerLine(Discharging));
        Assert.Equal("Charging 45.0 W", BatteryDetailsText.PowerLine(Charging));
        Assert.Null(BatteryDetailsText.PowerLine(Charging with { Charging = false, RateMilliwatts = 0 }));
        Assert.Null(BatteryDetailsText.PowerLine(null));
    }

    [Fact]
    public void OnBattery_PrefersWindowsOwnEstimate()
    {
        Assert.Equal(("2 h 0 min", "left"), BatteryDetailsText.TimeParts(Discharging with { EstimatedSecondsLeft = 7200 }));
    }

    [Fact]
    public void PluggedInButNotCharging_ShowsNoRateOrTime()
    {
        var full = Charging with { Charging = false, RateMilliwatts = 0 };

        Assert.Null(BatteryDetailsText.PowerParts(full));
        Assert.Null(BatteryDetailsText.TimeParts(full));
    }

    [Fact]
    public void Charge_ShowsTheWattHoursStoredAndOutOfHowMany()
    {
        Assert.Equal(("28.9 Wh", "of 55.4 Wh"), BatteryDetailsText.ChargeParts(Charging));
    }

    [Fact]
    public void Voltage_IsInVoltsWithTwoDecimals()
    {
        Assert.Equal("16.44 V", BatteryDetailsText.Voltage(Charging));
    }

    [Fact]
    public void Identity_NamesTheBatteryAndSpellsOutAShortChemistryCode()
    {
        Assert.Equal("Razer Blade · Lithium-ion", BatteryDetailsText.Identity(Charging));
    }
}
