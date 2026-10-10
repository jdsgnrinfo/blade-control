using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>
/// The rules that decide what the performance UI offers and stores. Pure
/// functions with no hardware or UI, so every rule can be tested directly.
/// </summary>
internal static class PowerProfileRules
{
    /// <summary>Windows reporting no power state (no battery, or unknown) counts as plugged in.</summary>
    public static bool TreatAsPluggedIn(bool? isPluggedIn) => isPluggedIn != false;

    /// <summary>
    /// On battery Balanced and Silent are offered (Synapse offers only
    /// Balanced); Custom stays plugged-in only. That is policy, not a hardware
    /// limit: the EC accepts every mode on battery.
    /// </summary>
    public static bool IsModeAllowed(PerformanceMode mode, bool pluggedIn) =>
        pluggedIn || mode is PerformanceMode.Balanced or PerformanceMode.Silent;

    /// <summary>
    /// Max fan speed is offered only when plugged in, like Custom. With the
    /// controller flag it is a Custom-only setting (the EC rejects it
    /// elsewhere); Synapse also requires CPU Boost and GPU High, which this
    /// deliberately does not, yet. With the manual fan method it works in any
    /// mode but Silent, which holds the fans down.
    /// </summary>
    public static bool CanUseMaxFan(
        PerformanceState state,
        bool pluggedIn,
        MaxFanMethod method = MaxFanMethod.ControllerFlag) =>
        pluggedIn && method switch
        {
            MaxFanMethod.ControllerFlag => state.Mode == PerformanceMode.Custom,
            MaxFanMethod.ManualFan => state.Mode is PerformanceMode mode && mode != PerformanceMode.Silent,
            _ => false
        };

    /// <summary>Boost levels belong to Custom, so they follow Custom's availability.</summary>
    public static bool CanChangeBoost(PerformanceState state, bool pluggedIn) =>
        state.Mode == PerformanceMode.Custom && IsModeAllowed(PerformanceMode.Custom, pluggedIn);

    /// <summary>
    /// The middle mode: Gaming in place of Balanced when that experimental
    /// setting is on and the laptop is plugged in; Balanced otherwise, and
    /// always on battery, where Gaming would drain it.
    /// </summary>
    public static PerformanceMode MiddleMode(bool gamingInsteadOfBalanced, bool pluggedIn) =>
        gamingInsteadOfBalanced && pluggedIn ? PerformanceMode.Gaming : PerformanceMode.Balanced;

    /// <summary>
    /// Makes a stored profile safe to apply on <paramref name="pluggedIn"/>.
    /// Balanced and Gaming are the same middle mode, so each becomes whichever
    /// the setting asks for (see <see cref="MiddleMode"/>). A battery profile
    /// saved by an earlier version may hold a mode that is no longer offered
    /// there; it falls back to Balanced. Plugged-in profiles are otherwise
    /// never restricted.
    /// </summary>
    public static PowerProfile Sanitize(PowerProfile profile, bool pluggedIn, bool gamingInsteadOfBalanced = false)
    {
        var middle = MiddleMode(gamingInsteadOfBalanced, pluggedIn);

        if (profile.Mode is PerformanceMode.Balanced or PerformanceMode.Gaming && profile.Mode != middle)
            profile = profile with { Mode = middle };

        if (profile.Mode is not PerformanceMode mode || IsModeAllowed(mode, pluggedIn))
            return profile;

        return profile with { Mode = PerformanceMode.Balanced };
    }

    /// <summary>
    /// The profile to store after the EC settled in <paramref name="state"/>.
    /// It records what the EC really ended up in, so a stored profile is fully
    /// specified. Boost levels the EC does not report (it only reports them in
    /// Custom) keep their requested value for the next time Custom is used.
    /// </summary>
    public static PowerProfile Remember(PowerProfile requested, PerformanceState state) =>
        requested with
        {
            Mode = state.Mode ?? requested.Mode,
            Cpu = state.Cpu ?? requested.Cpu,
            Gpu = state.Gpu ?? requested.Gpu
        };
}
