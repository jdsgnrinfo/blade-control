using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>
/// Windows' power plans through the power API, as the Control Panel lists
/// them. Choosing the active plan, and adding or changing one of the user's
/// own, needs no administrator rights.
/// </summary>
internal sealed class WindowsPowerPlans : IPowerPlans
{
    private const uint AccessScheme = 16;
    private const uint NoMoreItems = 259;

    public IReadOnlyList<PowerPlan> List()
    {
        var found = new List<PowerPlan>();
        var buffer = new byte[16];

        for (uint index = 0; ; index++)
        {
            var size = (uint)buffer.Length;
            var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);

            if (result == NoMoreItems)
                break;

            Check(result, "list the power plans");
            var plan = new Guid(buffer);
            found.Add(new PowerPlan(plan, FriendlyName(plan) ?? plan.ToString()));
        }

        return found;
    }

    public Guid Active()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer), "find the active power plan");

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    public void Activate(Guid plan) =>
        Check(PowerSetActiveScheme(IntPtr.Zero, ref plan), "change the power plan");

    public void Duplicate(Guid source, Guid copy)
    {
        if (!PowerCfg("/duplicatescheme", source.ToString(), copy.ToString()))
            throw new Win32Exception($"Windows did not create the power plan {copy}.");
    }

    public bool SetProcessorValue(Guid plan, string setting, uint pluggedIn, uint onBattery) =>
        PowerCfg("/setacvalueindex", plan.ToString(), "SUB_PROCESSOR", setting, pluggedIn.ToString(CultureInfo.InvariantCulture)) &&
        PowerCfg("/setdcvalueindex", plan.ToString(), "SUB_PROCESSOR", setting, onBattery.ToString(CultureInfo.InvariantCulture));

    public void Rename(Guid plan, string name, string description) =>
        PowerCfg("/changename", plan.ToString(), name, description);

    // Windows' own powercfg, as the plan script uses: it knows the settings by
    // their aliases and needs no administrator rights for the user's plans.
    // True when it reports success.
    private static bool PowerCfg(params string[] arguments)
    {
        var start = new ProcessStartInfo("powercfg.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new Win32Exception("Could not start powercfg.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static string? FriendlyName(Guid plan)
    {
        uint size = 0;

        if (PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0)
            return null;

        var name = new byte[size];

        if (PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, name, ref size) != 0)
            return null;

        var text = System.Text.Encoding.Unicode.GetString(name).TrimEnd('\0').Trim();
        return text.Length > 0 ? text : null;
    }

    private static void Check(uint result, string what)
    {
        if (result != 0)
            throw new Win32Exception((int)result, $"Could not {what}.");
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr rootKey, IntPtr scheme, IntPtr subgroup, uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootKey, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr rootKey, ref Guid scheme);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
