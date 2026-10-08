using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class KeyboardScreenOffServiceTests
{
    // A keyboard at 80%.
    private sealed class Rig
    {
        public int Brightness = 80;
        public List<int> Writes { get; } = [];
        public KeyboardScreenOffService Service { get; }

        // Runs on a step of the fade, in place of the pause between steps.
        public Action<int>? DuringFade { get; set; }

        private int _pauses;

        // Everything runs in place and the fade does not wait, so each call is done when it returns.
        public Rig() =>
            Service = new KeyboardScreenOffService(
                () => Brightness,
                value => { Brightness = value; Writes.Add(value); },
                pause: _ => DuringFade?.Invoke(++_pauses),
                run: work => { work(); return Task.CompletedTask; });
    }

    [Fact]
    public void ScreenOff_FadesTheKeyboardDownInSteps()
    {
        var rig = new Rig();

        rig.Service.OnDisplayChanged(on: false);

        Assert.Equal(KeyboardScreenOffService.FadeSteps, rig.Writes.Count);
        Assert.Equal(0, rig.Writes[^1]);
        Assert.True(rig.Writes.Zip(rig.Writes.Skip(1)).All(pair => pair.First >= pair.Second), "Never brighter on the way down.");
        Assert.True(rig.Writes[0] is > 0 and < 80, "The first step is part of the way, not straight to dark.");
    }

    [Fact]
    public void TheScreenComingBackMidFade_StopsIt_AndPutsTheBrightnessBack()
    {
        var rig = new Rig();
        rig.DuringFade = step =>
        {
            if (step == 4)
                rig.Service.OnDisplayChanged(on: true);
        };

        rig.Service.OnDisplayChanged(on: false);

        Assert.Equal(80, rig.Brightness);
        Assert.Equal(4 + KeyboardScreenOffService.FadeSteps, rig.Writes.Count); // Four steps down, then the whole way back up; nothing after.
        Assert.True(rig.Writes.Skip(4).Zip(rig.Writes.Skip(5)).All(pair => pair.First <= pair.Second), "Straight back up from where it got to.");
    }

    [Fact]
    public void ScreenOn_FadesTheKeyboardBackUpInSteps()
    {
        var rig = new Rig();
        rig.Service.OnDisplayChanged(on: false);
        rig.Writes.Clear();

        rig.Service.OnDisplayChanged(on: true);

        Assert.Equal(KeyboardScreenOffService.FadeSteps, rig.Writes.Count);
        Assert.Equal(80, rig.Writes[^1]);
        Assert.True(rig.Writes.Zip(rig.Writes.Skip(1)).All(pair => pair.First <= pair.Second), "Never darker on the way up.");
        Assert.True(rig.Writes[0] is > 0 and < 80, "The first step is part of the way, not straight to full.");
    }

    [Fact]
    public void TheScreenGoingOffMidFadeUp_FadesDown_AndStillBringsTheOriginalBrightnessBack()
    {
        var rig = new Rig();
        rig.Service.OnDisplayChanged(on: false);

        var turned = false;
        rig.DuringFade = _ =>
        {
            if (!turned && rig.Brightness > 0)
            {
                turned = true;
                rig.Service.OnDisplayChanged(on: false);
            }
        };

        rig.Service.OnDisplayChanged(on: true);
        Assert.Equal(0, rig.Brightness);

        rig.DuringFade = null;
        rig.Service.OnDisplayChanged(on: true);
        Assert.Equal(80, rig.Brightness);
    }

    [Fact]
    public void ScreenOff_TurnsTheKeyboardOff_AndScreenOn_BringsTheSameBrightnessBack()
    {
        var rig = new Rig();

        rig.Service.OnDisplayChanged(on: false);
        Assert.Equal(0, rig.Brightness);

        rig.Service.OnDisplayChanged(on: true);
        Assert.Equal(80, rig.Brightness);
    }

    [Fact]
    public void ARepeatedScreenOffKeepsTheOriginalBrightness()
    {
        var rig = new Rig();

        rig.Service.OnDisplayChanged(on: false);
        rig.Service.OnDisplayChanged(on: false);
        rig.Service.OnDisplayChanged(on: true);

        Assert.Equal(80, rig.Brightness);
        Assert.Equal(KeyboardScreenOffService.FadeSteps * 2, rig.Writes.Count); // One fade down, one back up.
    }

    [Fact]
    public void ScreenOnWithoutAnOffChangesNothing()
    {
        var rig = new Rig();

        rig.Service.OnDisplayChanged(on: true);

        Assert.Empty(rig.Writes);
    }

    [Fact]
    public void AKeyboardAlreadyDarkIsNotTouched()
    {
        var rig = new Rig { Brightness = 0 };

        rig.Service.OnDisplayChanged(on: false);
        rig.Service.OnDisplayChanged(on: true);

        Assert.Empty(rig.Writes);
    }

    [Fact]
    public void ABrightnessChangedWhileTheScreenWasOffIsLeftAlone()
    {
        var rig = new Rig();
        rig.Service.OnDisplayChanged(on: false);

        rig.Brightness = 40; // The Fn keys, meanwhile.
        rig.Service.OnDisplayChanged(on: true);

        Assert.Equal(40, rig.Brightness);
    }

    [Fact]
    public void TurnedOff_DoesNothing_AndTurningItOffLightsTheKeyboardAgain()
    {
        var rig = new Rig();
        rig.Service.OnDisplayChanged(on: false);

        rig.Service.SetEnabled(false);
        Assert.Equal(80, rig.Brightness);

        rig.Service.OnDisplayChanged(on: false);
        Assert.Equal(80, rig.Brightness);
    }

    [Fact]
    public void RestoreOnExitLightsTheKeyboardAgain()
    {
        var rig = new Rig();
        rig.Service.OnDisplayChanged(on: false);

        rig.Service.Restore();

        Assert.Equal(80, rig.Brightness);
    }

    [Fact]
    public void Idle_TurnsTheLightOff_OnlyWhenItsOptionIsOn()
    {
        var rig = new Rig();

        rig.Service.SetActive(LightsOffReason.Idle, true);
        Assert.Equal(80, rig.Brightness);

        rig.Service.SetEnabled(LightsOffReason.Idle, true);
        Assert.Equal(0, rig.Brightness);

        rig.Service.SetActive(LightsOffReason.Idle, false);
        Assert.Equal(80, rig.Brightness);
    }

    [Fact]
    public void TheLightStaysOff_WhileAnyReasonStillHolds()
    {
        var rig = new Rig();
        rig.Service.SetEnabled(LightsOffReason.LowBattery, true);

        rig.Service.SetActive(LightsOffReason.LowBattery, true);
        rig.Service.OnDisplayChanged(on: false);
        rig.Service.OnDisplayChanged(on: true);

        Assert.Equal(0, rig.Brightness);

        rig.Service.SetActive(LightsOffReason.LowBattery, false);

        Assert.Equal(80, rig.Brightness);
    }

    [Fact]
    public void TurningAReasonOff_BringsTheLightBack()
    {
        var rig = new Rig();
        rig.Service.SetEnabled(LightsOffReason.LowBattery, true);
        rig.Service.SetActive(LightsOffReason.LowBattery, true);

        rig.Service.SetEnabled(LightsOffReason.LowBattery, false);

        Assert.Equal(80, rig.Brightness);
    }

    [Fact]
    public void AReasonReportedAgain_DoesNotFadeAgain()
    {
        var rig = new Rig();
        rig.Service.SetEnabled(LightsOffReason.Idle, true);
        rig.Service.SetActive(LightsOffReason.Idle, true);
        var writes = rig.Writes.Count;

        rig.Service.SetActive(LightsOffReason.Idle, true);

        Assert.Equal(writes, rig.Writes.Count);
    }
}
