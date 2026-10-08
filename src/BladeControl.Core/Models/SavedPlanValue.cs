namespace BladeControl.Core.Models;

/// <summary>A power plan setting's values, plugged in and on battery, and the plan they belong to: what Silent replaced, to put back later.</summary>
internal sealed record SavedPlanValue(Guid Scheme, uint PluggedIn, uint OnBattery);
