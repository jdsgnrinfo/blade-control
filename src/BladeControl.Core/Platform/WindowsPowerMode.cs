using System.Runtime.InteropServices;
using BladeControl.Core.Services;

namespace BladeControl.Core.Platform;

/// <summary>
/// Windows' power mode (Settings, System, Power: Best power efficiency,
/// Balanced, Best performance) for the power source in use. The other two
/// modes are overlays on the plan that replace its processor settings, the
/// EPP among them; Balanced, no overlay, lets the plan's own values work.
/// </summary>
internal sealed class WindowsPowerMode : IPowerMode
{
    public bool IsBalanced() => PowerGetEffectiveOverlayScheme(out var overlay) != 0 || overlay == Guid.Empty;

    public void SetBalanced()
    {
        var result = PowerSetActiveOverlayScheme(Guid.Empty);

        if (result != 0)
            throw new System.ComponentModel.Win32Exception((int)result, "Could not set Windows' power mode to Balanced.");
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
}
