using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class BatteryUsageTests
{
    // Three entries in the shape powercfg writes them, from a Blade 15 Base (2020).
    private const string Report = """
        <?xml version="1.0" encoding="utf-8"?>
        <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
          <RecentUsage>
            <UsageEntry Timestamp="2026-10-08T06:20:01Z" LocalTimestamp="2026-10-08T02:20:01" Duration="234900000000"
                        Ac="1" EntryType="Suspend" ChargeCapacity="56903" Discharge="0" FullChargeCapacity="56903" IsNextOnBattery="0" />
            <UsageEntry Timestamp="2026-10-08T12:52:31Z" LocalTimestamp="2026-10-08T08:52:31" Duration="11730000000"
                        Ac="1" EntryType="Active" ChargeCapacity="56903" Discharge="0" FullChargeCapacity="56903" IsNextOnBattery="1" />
            <UsageEntry Timestamp="2026-10-08T13:12:04Z" LocalTimestamp="2026-10-08T09:12:04" Duration="6880000000"
                        Ac="0" EntryType="Active" ChargeCapacity="56641" Discharge="-5143" FullChargeCapacity="56903" IsNextOnBattery="0" />
          </RecentUsage>
        </BatteryReport>
        """;

    private static readonly DateTime Now = new(2026, 10, 8, 9, 23, 32);

    [Fact]
    public void Parse_ReadsEachEntryInLocalTime()
    {
        var entries = BatteryUsage.Parse(Report);

        Assert.Equal(3, entries.Count);
        Assert.Equal(new DateTime(2026, 10, 8, 2, 20, 1), entries[0].Start);
        Assert.Equal(UsageState.Asleep, entries[0].State);
        Assert.True(entries[0].OnAc);
        Assert.Equal(TimeSpan.FromSeconds(23490), entries[0].Duration);
        Assert.Equal(UsageState.Active, entries[2].State);
        Assert.False(entries[2].OnAc);
        Assert.Equal(56641, entries[2].ChargeMilliwattHours);
    }

    [Fact]
    public void Parse_GivesNothingForTextThatIsNotAReport()
    {
        Assert.Empty(BatteryUsage.Parse("not xml"));
        Assert.Empty(BatteryUsage.Parse("<BatteryReport />"));
    }

    [Fact]
    public void Build_HasTwentyFourHoursEndingWithTheHourNow()
    {
        var day = BatteryUsage.Build(BatteryUsage.Parse(Report), Now, chargeNow: 51498, onAcNow: false, fullChargeMilliwattHours: 56903);

        Assert.Equal(BatteryUsage.HoursShown, day.Hours.Count);
        Assert.Equal(new DateTime(2026, 10, 7, 10, 0, 0), day.Start);
        Assert.Equal(new DateTime(2026, 10, 8, 9, 0, 0), day.Hours[^1].Start);
    }

    [Fact]
    public void Build_LeavesHoursBeforeTheRecordEmpty_AndEndsWithTheChargeNow()
    {
        var day = BatteryUsage.Build(BatteryUsage.Parse(Report), Now, chargeNow: 51498, onAcNow: false, fullChargeMilliwattHours: 56903);

        // The record starts at 2:20, so the bar for 1:00 to 2:00 has nothing.
        Assert.Null(day.Hours.Single(hour => hour.Start.Hour == 1).Fraction);
        Assert.Equal(1.0, day.Hours.Single(hour => hour.Start.Hour == 3).Fraction!.Value, 3);
        Assert.Equal(51498 / 56903.0, day.Hours[^1].Fraction!.Value, 3);
    }

    [Fact]
    public void Build_MarksTheChargerUntilItWasUnplugged()
    {
        var day = BatteryUsage.Build(BatteryUsage.Parse(Report), Now, chargeNow: 51498, onAcNow: false, fullChargeMilliwattHours: 56903);

        var span = Assert.Single(day.Charging);
        Assert.True(span.StartsInside);
        Assert.Equal((new DateTime(2026, 10, 8, 2, 20, 1) - day.Start).TotalHours / 24, span.From, 3);
        Assert.Equal((new DateTime(2026, 10, 8, 9, 12, 4) - day.Start).TotalHours / 24, span.To, 2);
    }

    [Fact]
    public void Build_AddsUpTimeOnAndAsleep()
    {
        var day = BatteryUsage.Build(BatteryUsage.Parse(Report), Now, chargeNow: 51498, onAcNow: false, fullChargeMilliwattHours: 56903);

        Assert.Equal(TimeSpan.FromSeconds(23490), day.Asleep);
        Assert.Equal(TimeSpan.FromSeconds(1173 + 688), day.ScreenOn);
        Assert.Equal(TimeSpan.Zero, day.ScreenOff);
    }

    [Fact]
    public void Build_WithNoRecord_ShowsOnlyTheHourNow()
    {
        var day = BatteryUsage.Build([], Now, chargeNow: 28451, onAcNow: true, fullChargeMilliwattHours: 56903);

        Assert.All(day.Hours.Take(BatteryUsage.HoursShown - 1), hour => Assert.Null(hour.Fraction));
        Assert.Equal(0.5, day.Hours[^1].Fraction!.Value, 2);
        Assert.Single(day.Charging);
    }
}
