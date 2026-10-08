using System.Runtime.CompilerServices;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Tests.TestSupport;

internal static class TestLogging
{
    // Runs once when the test assembly loads, before any test. Code under test
    // logs failures on purpose; without this those lines would land in the
    // real razerhelper.log in the developer's profile.
    [ModuleInitializer]
    internal static void RedirectLogToTempFolder() =>
        AppLog.RedirectTo(Path.Combine(Path.GetTempPath(), "BladeControl.Core.Tests", "logs"));
}
