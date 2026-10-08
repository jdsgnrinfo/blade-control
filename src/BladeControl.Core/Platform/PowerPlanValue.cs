using System.ComponentModel;
using System.Runtime.InteropServices;
using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>
/// Reads and writes one processor setting of Windows' power plans through the
/// power API. Plan settings belong to the user, so no administrator rights
/// are needed.
/// </summary>
/// <param name="setting">The setting, read and written.</param>
/// <param name="alsoWrite">Settings that follow it, written with the same values where the plan has them.</param>
internal sealed class PowerPlanValue(Guid setting, params Guid[] alsoWrite) : IPowerPlanValue
{
    // GUID_PROCESSOR_SETTINGS_SUBGROUP.
    private static readonly Guid ProcessorGroup = new("54533251-82be-4824-96c1-47b60b740d00");

    /// <summary>The processor performance boost mode: 0 keeps the CPU at its base frequency.</summary>
    public static PowerPlanValue BoostMode() => new(new Guid("be337238-0d82-4146-a960-4f3749d470c7"));

    /// <summary>
    /// The processor energy performance preference, 0 (performance) to 100
    /// (efficiency), with its twin for a hybrid CPU's efficient cores.
    /// </summary>
    public static PowerPlanValue EfficiencyPreference() =>
        new(new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"), new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6864"));

    public SavedPlanValue Read()
    {
        var scheme = ActiveScheme();
        var group = ProcessorGroup;
        var value = setting;

        Check(PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref value, out var pluggedIn), "read a plugged-in value");
        Check(PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref value, out var onBattery), "read an on-battery value");

        return new SavedPlanValue(scheme, pluggedIn, onBattery);
    }

    public void Write(Guid scheme, uint pluggedIn, uint onBattery)
    {
        WriteOne(scheme, setting, pluggedIn, onBattery);

        // The followers are best effort: a plan may not have them.
        foreach (var follower in alsoWrite)
        {
            try
            {
                WriteOne(scheme, follower, pluggedIn, onBattery);
            }
            catch (Win32Exception)
            {
            }
        }

        // A plan's new values only take effect once it is made active again.
        if (ActiveScheme() == scheme)
            Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme), "apply the power plan");
    }

    private static void WriteOne(Guid scheme, Guid which, uint pluggedIn, uint onBattery)
    {
        var group = ProcessorGroup;

        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref group, ref which, pluggedIn), "write a plugged-in value");
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref which, onBattery), "write an on-battery value");
    }

    private static Guid ActiveScheme()
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

    private static void Check(uint result, string what)
    {
        if (result != 0)
            throw new Win32Exception((int)result, $"Could not {what}.");
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr activeScheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
