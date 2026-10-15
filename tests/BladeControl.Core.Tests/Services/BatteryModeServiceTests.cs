using BladeControl.Core.Models;
using BladeControl.Core.Platform;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public class BatteryModeServiceTests
{
    private sealed class Power : IPowerSource
    {
        public bool? IsPluggedIn { get; set; } = true;

        public event EventHandler? PowerSourceChanged;

        public void Change(bool pluggedIn)
        {
            IsPluggedIn = pluggedIn;
            PowerSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Effects : IVisualEffects
    {
        public bool Transparency { get; set; } = true;
        public bool WindowShadows { get; set; } = true;
        public bool PointerShadow { get; set; } = true;
        public bool DragWindowContents { get; set; } = true;
    }

    private sealed class Screen : IScreenBrightness
    {
        public int? Level { get; set; } = 70;
        public int? Read() => Level;
        public void Set(int percent) => Level = percent;
    }

    private sealed class Plan : IPowerPlanValue
    {
        public SavedPlanValue Value { get; set; } = new(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), 20, 20);
        public SavedPlanValue Read() => Value;
        public void Write(Guid scheme, uint pluggedIn, uint onBattery) => Value = new(scheme, pluggedIn, onBattery);
    }

    private sealed class Rig
    {
        public Power Power { get; } = new();
        public Effects Effects { get; } = new();
        public Screen Screen { get; } = new();
        public Plan Plan { get; } = new();
        public int Keyboard { get; set; } = 60;
        public BatteryModeSaved? Saved { get; private set; }
        public BatteryModeService Service { get; }

        public Rig(bool enabled = true, BatteryModeSaved? saved = null)
        {
            Saved = saved;
            Service = new BatteryModeService(Power, Effects, Screen, Plan,
                () => Task.FromResult<int?>(Keyboard),
                percent => { Keyboard = percent; return Task.CompletedTask; },
                enabled, saved, value => Saved = value);
        }

        // The service works off the caller's thread: wait for it to settle.
        public async Task Unplug()
        {
            Power.Change(false);
            await Settle();
        }

        public async Task PlugIn()
        {
            Power.Change(true);
            await Settle();
        }

        public static Task Settle() => Task.Delay(150);
    }

    [Fact]
    public async Task Unplugged_ItDimsAndTurnsTheEffectsOff_AndSavesWhatWasThere()
    {
        var rig = new Rig();
        await rig.Service.StartAsync();
        await rig.Unplug();

        Assert.Equal(BatteryModeService.ScreenPercent, rig.Screen.Level);
        Assert.Equal(BatteryModeService.KeyboardPercent, rig.Keyboard);
        Assert.False(rig.Effects.Transparency);
        Assert.False(rig.Effects.WindowShadows);
        Assert.False(rig.Effects.PointerShadow);
        Assert.False(rig.Effects.DragWindowContents);
        Assert.Equal(100u, rig.Plan.Value.OnBattery);
        Assert.Equal(20u, rig.Plan.Value.PluggedIn);
        Assert.Equal(70, rig.Saved?.ScreenBrightness);
        Assert.Equal(60, rig.Saved?.KeyboardBrightness);
    }

    [Fact]
    public async Task PluggingIn_PutsEverythingBack()
    {
        var rig = new Rig();
        await rig.Service.StartAsync();
        await rig.Unplug();
        await rig.PlugIn();

        Assert.Equal(70, rig.Screen.Level);
        Assert.Equal(60, rig.Keyboard);
        Assert.True(rig.Effects.Transparency && rig.Effects.WindowShadows && rig.Effects.PointerShadow && rig.Effects.DragWindowContents);
        Assert.Equal(20u, rig.Plan.Value.OnBattery);
        Assert.Null(rig.Saved);
    }

    [Fact]
    public async Task ALevelChangedByHand_StaysAsTheUserChose()
    {
        var rig = new Rig();
        await rig.Service.StartAsync();
        await rig.Unplug();
        rig.Screen.Level = 50;
        rig.Keyboard = 0;
        await rig.PlugIn();

        Assert.Equal(50, rig.Screen.Level);
        Assert.Equal(0, rig.Keyboard);
    }

    [Fact]
    public async Task LevelsAlreadyLow_AreNotRaised_NorTouchedLater()
    {
        var rig = new Rig();
        rig.Screen.Level = 15;
        rig.Keyboard = 0;
        await rig.Service.StartAsync();
        await rig.Unplug();

        Assert.Equal(15, rig.Screen.Level);
        Assert.Equal(0, rig.Keyboard);
        Assert.Null(rig.Saved?.ScreenBrightness);
    }

    [Fact]
    public async Task EffectsAlreadyOff_StayOff_AfterPluggingIn()
    {
        var rig = new Rig();
        rig.Effects.Transparency = false;
        await rig.Service.StartAsync();
        await rig.Unplug();
        await rig.PlugIn();

        Assert.False(rig.Effects.Transparency);
        Assert.True(rig.Effects.WindowShadows);
    }

    [Fact]
    public async Task Off_NothingChangesOnBattery()
    {
        var rig = new Rig(enabled: false);
        await rig.Service.StartAsync();
        await rig.Unplug();

        Assert.Equal(70, rig.Screen.Level);
        Assert.True(rig.Effects.Transparency);
        Assert.Null(rig.Saved);
    }

    [Fact]
    public async Task TurningItOffOnBattery_PutsBackAtOnce()
    {
        var rig = new Rig();
        await rig.Service.StartAsync();
        await rig.Unplug();
        await rig.Service.SetEnabledAsync(false);

        Assert.Equal(70, rig.Screen.Level);
        Assert.True(rig.Effects.Transparency);
    }

    [Fact]
    public async Task WhatACrashLeft_IsPutBackAtTheNextStart_WhenPluggedIn()
    {
        var left = new BatteryModeSaved(Transparency: true, ScreenBrightness: 80, ScreenBrightnessSet: 25);
        var rig = new Rig(saved: left);
        rig.Effects.Transparency = false;
        rig.Screen.Level = 25;

        await rig.Service.StartAsync();

        Assert.True(rig.Effects.Transparency);
        Assert.Equal(80, rig.Screen.Level);
        Assert.Null(rig.Saved);
    }

    [Fact]
    public async Task BeforeTheAppStartsIt_NothingIsTouched()
    {
        var rig = new Rig();
        await rig.Unplug();
        await rig.Service.RestoreAsync("test");

        Assert.Equal(70, rig.Screen.Level);
        Assert.True(rig.Effects.Transparency);
    }

    [Fact]
    public async Task BatteryPercentageUpdates_DoNotApplyItTwice()
    {
        var rig = new Rig();
        await rig.Service.StartAsync();
        await rig.Unplug();
        rig.Screen.Level = 40; // The user brightens it while on battery.
        await rig.Unplug();    // Windows reports a percentage change.

        Assert.Equal(40, rig.Screen.Level);
        Assert.Equal(70, rig.Saved?.ScreenBrightness);
    }
}
