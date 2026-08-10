using System.Globalization;
using System.Runtime.CompilerServices;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Pins the UI culture for the whole test run.
    /// </summary>
    /// <remarks>
    /// Most rule tests assert the diagnostic's full message text, and diagnostics are
    /// localized: once the Traditional Chinese satellite exists, those assertions pass or
    /// fail according to the operating system language of whoever runs them. That was not
    /// hypothetical — adding the satellite turned five rule tests red on a zh-TW machine
    /// while CI, which runs in English, stayed green. A suite whose verdict depends on the
    /// developer's Windows install is worse than a failing one, because the disagreement
    /// looks like a code difference.
    /// <para>
    /// English is the pinned language because that is what the assertions are written in.
    /// The localization tests set the culture themselves, per test, and restore it after.
    /// </para>
    /// </remarks>
    internal static class TestCulture
    {
        [ModuleInitializer]
        public static void PinToEnglish()
        {
            var english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            CultureInfo.CurrentUICulture = english;
        }
    }
}
