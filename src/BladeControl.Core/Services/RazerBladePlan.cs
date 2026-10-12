namespace BladeControl.Core.Services;

/// <summary>One setting of the plan: its group and setting (a powercfg alias or id), plugged in, on battery.</summary>
/// <param name="About">What it is, for the log when this PC refuses it.</param>
/// <param name="Optional">Only some PCs have it; refusing it is not reported.</param>
internal sealed record PlanValue(string Group, string Setting, uint PluggedIn, uint OnBattery, string About, bool Optional = false);

/// <summary>
/// The "Razer Blade" power plan: Windows' Balanced plan with the processor,
/// Wi-Fi, PCI Express, NVMe and Intel graphics settings changed. Plugged in it
/// favors performance (more turbo, devices never dozing); on battery it favors
/// battery life (a calmer processor, Wi-Fi and the SSD saving power).
/// Installing it again updates it instead of adding another.
/// </summary>
internal static class RazerBladePlan
{
    /// <summary>A fixed id, so the plan is found again.</summary>
    public static readonly Guid Id = new("e1811cef-6588-4a28-876f-cc20c84725f8");

    /// <summary>Windows' Balanced plan, which it starts from.</summary>
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    public const string Name = "Razer Blade";

    private const string Processor = "SUB_PROCESSOR";
    private const string Disk = "SUB_DISK";
    private const string PciExpress = "SUB_PCIEXPRESS";
    private const string WirelessAdapter = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    private const string IntelGraphics = "44f3beca-a7c0-460e-9df2-bb8b99e0cba6";

    /// <summary>Its description in Windows, in the interface language.</summary>
    public static string Description(bool spanish) => spanish
        ? "Máximo rendimiento para jugar y trabajar, fresco y silencioso en reposo."
        : "Full performance for gaming and work, cool and quiet at rest.";

    /// <summary>The settings it changes from Balanced.</summary>
    public static readonly IReadOnlyList<PlanValue> Values =
    [
        // The processor.
        new(Processor, "PERFEPP", 20, 80, "Energy performance preference"),
        new(Processor, "PERFEPP1", 20, 80, "Energy performance preference, efficiency cores", Optional: true), // Only CPUs with two kinds of cores.
        new(Processor, "PROCTHROTTLEMIN", 5, 5, "Minimum processor state, %"),
        new(Processor, "PROCTHROTTLEMAX", 100, 100, "Maximum processor state, %"),
        new(Processor, "PERFBOOSTMODE", 2, 3, "Boost mode: Aggressive plugged in, Efficient Aggressive on battery"),
        new(Processor, "45bcc044-d885-43e2-8605-ee0ec6e96b59", 60, 40, "Boost policy, %: how much of the boost is used"),
        new(Processor, "CPMINCORES", 100, 30, "Cores kept unparked, %"),
        new(Processor, "943c8cb6-6f93-4227-ad87-e9a3feec08d1", 60, 85, "Core parking overutilization threshold, %"),
        new(Processor, "465e1f50-b610-473a-ab58-00d1077dc418", 2, 1, "Performance increase policy: Rocket (straight to the top) plugged in, Single (gradual) on battery"),
        new(Processor, "984cf492-3bed-4488-a8f9-4286c97bf5aa", 1, 3, "Performance increase time, check intervals"),
        new(Processor, "40fbefc7-2e9d-4d25-a185-0cfd8574bac6", 1, 0, "Performance decrease policy: Single (gradual) plugged in, Ideal on battery"),
        new(Processor, "4d2b0152-7d5c-498b-88e2-34345392a2c5", 30, 100, "Performance check interval, ms"),
        new(Processor, "619b7505-003b-4e82-b7a6-4dd29c300971", 99, 40, "Performance for latency sensitivity hints, %"),

        // The devices.
        new(WirelessAdapter, "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0, 3, "Wi-Fi power saving: none plugged in, maximum on battery"),
        new(PciExpress, "ASPM", 0, 2, "PCI Express link power saving: off plugged in, maximum on battery"),
        new(Disk, "fc95af4d-40e7-4b6d-835a-56d131dbc80e", 0, 200, "Primary NVMe power state latency tolerance, ms (0 never dozes)"),
        new(Disk, "dbc9e238-6de9-49e3-92cd-8c2b4946b472", 0, 200, "Secondary NVMe power state latency tolerance, ms (0 never dozes)"),
        new(IntelGraphics, "3619c3f2-afb2-4afc-b0e9-e7fef372de36", 2, 1, "Intel graphics: maximum performance plugged in, balanced on battery", Optional: true) // Only with Intel's driver.
    ];

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
            .Where(value => !plans.SetValue(Id, value.Group, value.Setting, value.PluggedIn, value.OnBattery) && !value.Optional)
            .Select(value => value.About)
            .ToList();

        plans.Rename(Id, Name, Description(spanish));

        // Activating it applies the new values, also to a plan already in use.
        plans.Activate(Id);
        return refused;
    }
}
