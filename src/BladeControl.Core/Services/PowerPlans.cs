namespace BladeControl.Core.Services;

/// <summary>One of Windows' power plans.</summary>
internal sealed record PowerPlan(Guid Id, string Name);

/// <summary>Windows' power plans: which there are, which is active, making one active, and adding one.</summary>
internal interface IPowerPlans
{
    IReadOnlyList<PowerPlan> List();

    Guid Active();

    void Activate(Guid plan);

    /// <summary>Adds a copy of <paramref name="source"/> with the id <paramref name="copy"/>.</summary>
    void Duplicate(Guid source, Guid copy);

    /// <summary>Sets a setting (its group and itself, by powercfg alias or id) plugged in and on battery; false when this PC does not have it.</summary>
    bool SetValue(Guid plan, string group, string setting, uint pluggedIn, uint onBattery);

    void Rename(Guid plan, string name, string description);
}
