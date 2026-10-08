using BladeControl.Core.Hardware;
using BladeControl.Core.Models;
using BladeControl.Core.Services;
using BladeControl.Core.Tests.TestSupport;

namespace BladeControl.Core.Tests.Services;

public class PerformanceServiceTests
{
    // The EC's wire values (see PerformanceMode, CpuBoost, GpuBoost).
    private const byte Balanced = 0;
    private const byte Custom = 4;
    private const byte Silent = 5;

    private static (FakeEc Ec, PerformanceService Service) Create(byte mode = Balanced, byte cpu = 1, byte gpu = 0)
    {
        var ec = new FakeEc { CpuBoost = cpu, GpuBoost = gpu };
        ec.SetBothZones(mode);
        return (ec, new PerformanceService(ec));
    }

    private static string Describe(FakeEc.Sent sent) =>
        $"{sent.Command:X4}:{Convert.ToHexString(sent.Arguments)}";

    private static IEnumerable<FakeEc.Sent> BoostWrites(FakeEc ec) =>
        ec.Writes.Where(write => write.Command == RazerCommands.SetBoost);

    // ---- ReadState -------------------------------------------------------

    [Fact]
    public void ReadState_InBalanced_ReportsTheModeAndDoesNotReadBoostLevels()
    {
        var (ec, service) = Create(Balanced);

        var state = service.ReadState();

        Assert.Equal(new PerformanceState(PerformanceMode.Balanced, null, null), state);
        Assert.DoesNotContain(ec.Log, sent => sent.Command == RazerCommands.GetBoost);
    }

    [Fact]
    public void ReadState_InSilent_ReportsSilent()
    {
        var (_, service) = Create(Silent);

        Assert.Equal(PerformanceMode.Silent, service.ReadState().Mode);
    }

    [Fact]
    public void ApplyProfile_Gaming_WritesByteOneToBothZones()
    {
        // Gaming is wire byte 1, as a Blade 15 Base (2020) reads it back.
        var (ec, service) = Create(Balanced);

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Gaming));

        Assert.Equal(PerformanceMode.Gaming, state.Mode);
        Assert.Equal(
            ["0D02:01010100", "0D02:01020100"],
            ec.Writes.Select(Describe));
    }

    [Fact]
    public void ApplyProfile_AModeTheFirmwareDoesNotTake_ReportsTheModeItStayedIn()
    {
        // Gaming is offered on every model; one whose firmware echoes the
        // write without acting on it must come back as still in Balanced, so
        // the Performance section can mark Gaming as not supported.
        var (ec, service) = Create(Balanced);
        ec.IgnoreModeWrites = true;

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Gaming));

        Assert.Equal(PerformanceMode.Balanced, state.Mode);
    }

    [Fact]
    public void ReadState_InCustom_AlsoReadsTheBoostLevels()
    {
        var (_, service) = Create(Custom, cpu: 2, gpu: 1);

        Assert.Equal(
            new PerformanceState(PerformanceMode.Custom, CpuBoost.High, GpuBoost.Medium, MaxFan: false),
            service.ReadState());
    }

    [Theory]
    [InlineData(2)]   // Performance: real to the EC, not offered on this model
    [InlineData(6)]   // Battery
    [InlineData(7)]   // Hyperboost
    [InlineData(200)]
    public void ReadState_ReportsNoModeForAValueThisAppDoesNotOffer(byte wireMode)
    {
        var (_, service) = Create(wireMode);

        Assert.Null(service.ReadState().Mode);
    }

    [Fact]
    public void ReadState_ReportsNoLevelForABoostValueThisAppDoesNotOffer()
    {
        // 4 is the CPU overclock value, 3 is beyond the GPU's range.
        var (_, service) = Create(Custom, cpu: 4, gpu: 3);

        var state = service.ReadState();

        Assert.Equal(PerformanceMode.Custom, state.Mode);
        Assert.Null(state.Cpu);
        Assert.Null(state.Gpu);
    }

    [Fact]
    public void ReadState_RereadsWhenTheZonesBrieflyDisagree_AndSettlesOnTheNewMode()
    {
        // A mode change (Fn+P, another tool) can land between the two zone
        // reads. The disagreement is transient, so the service asks again.
        var (ec, service) = Create();
        ec.ZoneMode[0] = Balanced;
        ec.ZoneMode[1] = Custom;

        var modeReads = 0;
        ec.BeforeSend = (fake, command) =>
        {
            if (command == RazerCommands.GetPerformanceMode && ++modeReads == 3)
                fake.SetBothZones(Custom);
        };

        Assert.Equal(PerformanceMode.Custom, service.ReadState().Mode);
    }

    [Fact]
    public void ReadState_ReportsNoModeWhenTheZonesKeepDisagreeing()
    {
        var (ec, service) = Create();
        ec.ZoneMode[0] = Balanced;
        ec.ZoneMode[1] = Custom;

        var state = service.ReadState();

        Assert.Null(state.Mode);
        Assert.DoesNotContain(ec.Log, sent => sent.Command == RazerCommands.GetBoost);
    }

    // ---- ApplyProfile: nothing to do ------------------------------------

    [Fact]
    public void ApplyProfile_WithAProfileTheEcAlreadyMatches_WritesNothing()
    {
        var (ec, service) = Create(Custom, cpu: 1, gpu: 0);

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Custom, CpuBoost.Medium, GpuBoost.Low));

        Assert.Empty(ec.Writes);
        Assert.Equal(new PerformanceState(PerformanceMode.Custom, CpuBoost.Medium, GpuBoost.Low, MaxFan: false), state);
    }

    [Fact]
    public void ApplyProfile_WithAnEmptyProfile_LeavesTheEcAlone()
    {
        var (ec, service) = Create(Silent);

        var state = service.ApplyProfile(new PowerProfile());

        Assert.Empty(ec.Writes);
        Assert.Equal(PerformanceMode.Silent, state.Mode);
    }

    // ---- ApplyProfile: mode changes -------------------------------------

    [Fact]
    public void ApplyProfile_ChangingTheMode_WritesBothZonesWithTheFanOnAuto()
    {
        var (ec, service) = Create(Balanced);

        service.ApplyProfile(new PowerProfile(PerformanceMode.Silent));

        Assert.Equal(
            ["0D02:01010500", "0D02:01020500"],   // [enable, zone, mode, fan=auto]
            ec.Writes.Select(Describe));
    }

    [Fact]
    public void ApplyProfile_ReturnsWhatTheEcReportsAfterTheChange()
    {
        var (ec, service) = Create(Balanced);

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Silent));

        Assert.Equal(PerformanceMode.Silent, state.Mode);
        Assert.Equal(new byte[] { Silent, Silent }, ec.ZoneMode);
    }

    [Fact]
    public void ApplyProfile_LeavingCustom_DoesNotTouchTheBoostLevelsTheEcKeeps()
    {
        var (ec, service) = Create(Custom, cpu: 2, gpu: 1);

        service.ApplyProfile(new PowerProfile(PerformanceMode.Balanced));

        Assert.Empty(BoostWrites(ec));
        Assert.Equal(2, ec.CpuBoost);
        Assert.Equal(1, ec.GpuBoost);
    }

    // ---- ApplyProfile: boost levels -------------------------------------

    [Fact]
    public void ApplyProfile_IntoCustom_WritesTheModeFirstAndThenTheBoostLevels()
    {
        var (ec, service) = Create(Balanced, cpu: 0, gpu: 0);

        service.ApplyProfile(new PowerProfile(PerformanceMode.Custom, CpuBoost.High, GpuBoost.Medium));

        Assert.Equal(
            ["0D02:01010400", "0D02:01020400", "0D07:010102", "0D07:010201"],
            ec.Writes.Select(Describe));
    }

    [Fact]
    public void ApplyProfile_InCustom_WritesOnlyTheBoostLevelThatDiffers()
    {
        var (ec, service) = Create(Custom, cpu: 1, gpu: 0);

        service.ApplyProfile(new PowerProfile(PerformanceMode.Custom, CpuBoost.Medium, GpuBoost.High));

        Assert.Equal(["0D07:010202"], ec.Writes.Select(Describe));
    }

    [Fact]
    public void ApplyProfile_WithOnlyBoostLevels_ChangesThemInCustomWithoutWritingTheMode()
    {
        var (ec, service) = Create(Custom, cpu: 1, gpu: 0);

        service.ApplyProfile(new PowerProfile(Cpu: CpuBoost.Boost));

        Assert.Equal(["0D07:010103"], ec.Writes.Select(Describe));
    }

    [Fact]
    public void ApplyProfile_DoesNotWriteBoostLevelsOutsideCustom()
    {
        // The EC drops or misapplies boost writes outside Custom, so a
        // profile that mentions boosts must not send them from another mode.
        var (ec, service) = Create(Balanced);

        service.ApplyProfile(new PowerProfile(PerformanceMode.Silent, CpuBoost.Boost, GpuBoost.High));

        Assert.Empty(BoostWrites(ec));
    }

    [Fact]
    public void ApplyProfile_DoesNotWriteBoostLevelsWhenTheEcIgnoresTheSwitchToCustom()
    {
        var (ec, service) = Create(Balanced);
        ec.IgnoreModeWrites = true;

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Custom, CpuBoost.Boost, GpuBoost.High));

        Assert.Empty(BoostWrites(ec));
        Assert.Equal(PerformanceMode.Balanced, state.Mode);
    }

    // ---- Max fan speed ---------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadState_InCustom_ReportsTheMaxFanFlag(bool flag)
    {
        var (ec, service) = Create(Custom);
        ec.MaxFan = flag;

        Assert.Equal(flag, service.ReadState().MaxFan);
    }

    [Theory]
    [InlineData(Balanced)]
    [InlineData(Silent)]
    public void ReadState_OutsideCustom_DoesNotReadMaxFan(byte mode)
    {
        // The EC's read-back is meaningless outside Custom, so it is not asked.
        var (ec, service) = Create(mode);

        Assert.Null(service.ReadState().MaxFan);
        Assert.DoesNotContain(ec.Log, sent => sent.Command == RazerCommands.GetMaxFan);
    }

    [Fact]
    public void ReadState_ReportsNoMaxFanValueForAnEcAnswerItDoesNotKnow()
    {
        var (ec, _) = Create(Custom);
        var odd = new ScriptedTransport((command, args) =>
            command == RazerCommands.GetMaxFan ? FakeEc.Respond(1, 0) : ec.Send(command, args));

        Assert.Null(new PerformanceService(odd).ReadState().MaxFan);
    }

    [Fact]
    public void SetMaxFan_On_SendsTheOnValueAndReportsTheNewState()
    {
        var (ec, service) = Create(Custom);

        var state = service.SetMaxFan(true);

        Assert.Equal(["070F:02"], ec.Writes.Select(Describe));
        Assert.True(state.MaxFan);
        Assert.True(ec.MaxFan);
    }

    [Fact]
    public void SetMaxFan_Off_SendsTheOffValueAndReportsTheNewState()
    {
        var (ec, service) = Create(Custom);
        ec.MaxFan = true;

        var state = service.SetMaxFan(false);

        Assert.Equal(["070F:00"], ec.Writes.Select(Describe));
        Assert.False(state.MaxFan);
        Assert.False(ec.MaxFan);
    }

    [Theory]
    [InlineData(Balanced)]
    [InlineData(Silent)]
    public void SetMaxFan_RefusesOutsideCustom_AndSendsNothing(byte mode)
    {
        // The EC rejects the command there, so it must never be sent.
        var (ec, service) = Create(mode);

        Assert.Throws<InvalidOperationException>(() => service.SetMaxFan(true));

        Assert.Empty(ec.Writes);
    }

    [Fact]
    public void SetMaxFan_LeavesTheModeAndBoostLevelsAlone()
    {
        var (_, service) = Create(Custom, cpu: 3, gpu: 2);

        var state = service.SetMaxFan(true);

        Assert.Equal(PerformanceMode.Custom, state.Mode);
        Assert.Equal(CpuBoost.Boost, state.Cpu);
        Assert.Equal(GpuBoost.High, state.Gpu);
    }

    [Fact]
    public void SetMaxFan_ThrowsWhenTheEcDoesNotEchoTheValue()
    {
        var (ec, service) = Create(Custom);
        ec.EchoOverride = _ => [0x00];

        var exception = Assert.Throws<InvalidOperationException>(() => service.SetMaxFan(true));

        Assert.Contains("did not confirm max fan speed on", exception.Message);
    }

    [Fact]
    public void SetMaxFan_ReportsWhatTheEcSays_NotWhatWasAskedFor()
    {
        // An EC that accepts the write but does not apply it: the caller must see the truth.
        var (ec, _) = Create(Custom);
        var stubborn = new ScriptedTransport((command, args) =>
        {
            var response = ec.Send(command, args);

            if (command == RazerCommands.SetMaxFan)
                ec.MaxFan = false;

            return response;
        });

        var state = new PerformanceService(stubborn).SetMaxFan(true);

        Assert.False(state.MaxFan);
    }

    [Fact]
    public void LeavingCustom_ClearsMaxFan_WithoutTheServiceTouchingIt()
    {
        var (ec, service) = Create(Custom);
        ec.MaxFan = true;

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Balanced));

        Assert.Null(state.MaxFan);
        Assert.False(ec.MaxFan);
        Assert.DoesNotContain(ec.Writes, write => write.Command == RazerCommands.SetMaxFan);
    }

    [Fact]
    public void ApplyingAProfile_NeverTurnsMaxFanOnOrOff()
    {
        // Max fan is a one-off, not part of any profile.
        var (ec, service) = Create(Custom, cpu: 1, gpu: 0);
        ec.MaxFan = true;

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Custom, CpuBoost.Boost, GpuBoost.High));

        Assert.DoesNotContain(ec.Writes, write => write.Command == RazerCommands.SetMaxFan);
        Assert.True(state.MaxFan);
    }

    // ---- Failures --------------------------------------------------------

    [Fact]
    public void ApplyProfile_ThrowsWhenTheEcDoesNotEchoTheModeWrite()
    {
        var (ec, service) = Create(Balanced);
        ec.EchoOverride = args => [args[0], args[1], Balanced, args[3]];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.ApplyProfile(new PowerProfile(PerformanceMode.Silent)));

        Assert.Contains("did not confirm", exception.Message);
    }

    [Fact]
    public void ApplyProfile_ThrowsWhenTheEcDoesNotEchoTheBoostWrite()
    {
        var (ec, service) = Create(Custom, cpu: 1);
        ec.EchoOverride = args => [args[0], args[1], 0];

        Assert.Throws<InvalidOperationException>(() =>
            service.ApplyProfile(new PowerProfile(Cpu: CpuBoost.Boost)));
    }

    [Fact]
    public void ReadState_ThrowsWhenTheEcAnswersForTheWrongZone()
    {
        var transport = new ScriptedTransport((_, args) => FakeEc.Respond(0x00, 9, 0x00, 0x00));
        var service = new PerformanceService(transport);

        Assert.Throws<InvalidOperationException>(() => service.ReadState());
    }

    [Fact]
    public async Task TheAsyncEntryPoints_ReturnTheSameResultsAsTheSynchronousOnes()
    {
        var (_, service) = Create(Custom, cpu: 1, gpu: 0);

        var state = await service.ReadStateAsync();
        var applied = await service.ApplyProfileAsync(new PowerProfile(PerformanceMode.Custom, CpuBoost.High, null));

        Assert.Equal(PerformanceMode.Custom, state.Mode);
        Assert.Equal(CpuBoost.High, applied.Cpu);
    }

    // ---- Manual fan method (Blade 15 Base 2020) -------------------------------

    private static (FakeEc Ec, PerformanceService Service) CreateManual(byte mode)
    {
        var ec = new FakeEc();
        ec.SetBothZones(mode);
        return (ec, new PerformanceService(ec, MaxFanMethod.ManualFan));
    }

    [Fact]
    public void ManualMaxFan_SendsTheBytesCheckedOnTheLaptop()
    {
        // Balanced, both fans manual at 10000 RPM (0x64), above their ceiling so they
        // run at full power (5000 already made a Blade 15 Base (2020) audibly
        // spin up). The mode itself is left alone.
        var (ec, service) = CreateManual(Balanced);

        var state = service.SetMaxFan(true);

        Assert.Equal(
            ["0D02:01010001", "0D01:010164", "0D02:01020001", "0D01:010264"],
            ec.Writes.Select(Describe));
        Assert.True(state.MaxFan);
        Assert.Equal(PerformanceMode.Balanced, state.Mode);
    }

    [Fact]
    public void ManualMaxFan_Off_PutsBothFansBackOnAutomatic()
    {
        var (ec, service) = CreateManual(Balanced);
        service.SetMaxFan(true);
        ec.Log.Clear();

        var state = service.SetMaxFan(false);

        Assert.Equal(["0D02:01010000", "0D02:01020000"], ec.Writes.Select(Describe));
        Assert.False(state.MaxFan);
    }

    [Fact]
    public void ManualMaxFan_IsReadInModesOtherThanCustom()
    {
        var (ec, service) = CreateManual(Balanced);
        ec.ZoneFanMode[0] = 1;
        ec.ZoneFanMode[1] = 1;

        Assert.True(service.ReadState().MaxFan);
    }

    [Fact]
    public void ManualMaxFan_IsNeverTurnedOnInSilent()
    {
        var (ec, service) = CreateManual(Silent);

        Assert.Throws<InvalidOperationException>(() => service.SetMaxFan(true));
        Assert.Empty(ec.Writes);
    }

    [Fact]
    public void ManualMaxFan_ChangingModeTurnsItOff()
    {
        // A mode write carries "fans automatic", so Max never outlives a mode change.
        var (_, service) = CreateManual(Balanced);
        service.SetMaxFan(true);

        var state = service.ApplyProfile(new PowerProfile(PerformanceMode.Gaming));

        Assert.False(state.MaxFan);
    }
}
