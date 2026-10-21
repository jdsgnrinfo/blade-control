namespace BladeControl.Core.Models;

/// <summary>The processor settings of the Performance page's Processor card, each 0 to 100.</summary>
internal enum CpuSetting
{
    /// <summary>Energy performance preference: 0 favors speed, 100 battery life.</summary>
    Epp,

    /// <summary>The share of cores kept awake: less lets Windows park the idle ones.</summary>
    CoreParking,

    /// <summary>The maximum processor state: a cap on the frequency.</summary>
    FrequencyScaling,

    /// <summary>How much of the turbo is used.</summary>
    TurboBoost
}

/// <summary>The values chosen for one power source; null leaves the plan's own.</summary>
internal sealed record CpuValues(int? Epp = null, int? CoreParking = null, int? FrequencyScaling = null, int? TurboBoost = null)
{
    public int? Get(CpuSetting setting) => setting switch
    {
        CpuSetting.Epp => Epp,
        CpuSetting.CoreParking => CoreParking,
        CpuSetting.FrequencyScaling => FrequencyScaling,
        _ => TurboBoost
    };

    public CpuValues With(CpuSetting setting, int? value) => setting switch
    {
        CpuSetting.Epp => this with { Epp = value },
        CpuSetting.CoreParking => this with { CoreParking = value },
        CpuSetting.FrequencyScaling => this with { FrequencyScaling = value },
        _ => this with { TurboBoost = value }
    };

    public bool HasAny() => Epp is not null || CoreParking is not null || FrequencyScaling is not null || TurboBoost is not null;
}
