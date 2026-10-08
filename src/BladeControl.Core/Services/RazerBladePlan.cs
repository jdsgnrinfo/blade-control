namespace BladeControl.Core.Services;

/// <summary>
/// The "Razer Blade" power plan, the same one tools/RazerBladePowerPlan.ps1
/// adds: Windows' Balanced plan with only the processor changed, for full
/// performance under load, cool and quiet rest, and longer battery.
/// Installing it again updates it instead of adding another.
/// </summary>
internal static class RazerBladePlan
{
    /// <summary>A fixed id, so the plan is found again.</summary>
    public static readonly Guid Id = new("e1811cef-6588-4a28-876f-cc20c84725f8");

    /// <summary>Windows' Balanced plan, which it starts from.</summary>
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    public const string Name = "Razer Blade";

    /// <summary>Its description in Windows, in the interface language.</summary>
    public static string Description(bool spanish) => spanish
        ? "Máximo rendimiento para jugar y trabajar, fresco y silencioso en reposo."
        : "Full performance for gaming and work, cool and quiet at rest.";

    /// <summary>The processor settings it changes: plugged in, on battery.</summary>
    public static readonly IReadOnlyList<(string Setting, uint PluggedIn, uint OnBattery)> Values =
    [
        ("PERFEPP", 33, 50),          // Energy performance preference
        ("PERFEPP1", 33, 50),         // The same, for the efficiency cores of newer CPUs
        ("PROCTHROTTLEMIN", 5, 5),    // Minimum processor state, %
        ("PROCTHROTTLEMAX", 100, 100),// Maximum processor state, %
        ("PERFBOOSTMODE", 2, 3),      // Processor boost: Aggressive plugged in, Efficient Enabled on battery
        ("CPMINCORES", 100, 50)       // Cores kept unparked, %: all plugged in, half on battery
    ];

    // Only on CPUs with two kinds of cores.
    private const string OptionalSetting = "PERFEPP1";

    public static bool IsInstalled(IPowerPlans plans) => plans.List().Any(plan => plan.Id == Id);

    /// <summary>
    /// Adds the plan, or updates it, and makes it the active one. Returns the
    /// settings this PC refused (none on most); a plan that cannot be created throws.
    /// </summary>
    public static IReadOnlyList<string> Install(IPowerPlans plans, bool spanish)
    {
        if (!IsInstalled(plans))
            plans.Duplicate(Balanced, Id);

        var refused = Values
            .Where(value => !plans.SetProcessorValue(Id, value.Setting, value.PluggedIn, value.OnBattery) && value.Setting != OptionalSetting)
            .Select(value => value.Setting)
            .ToList();

        plans.Rename(Id, Name, Description(spanish));

        // Activating it applies the new values, also to a plan already in use.
        plans.Activate(Id);
        return refused;
    }
}
