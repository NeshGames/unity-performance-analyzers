using System.Globalization;
using System.Runtime.CompilerServices;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Pins the UI culture for the whole test run.
    /// </summary>
    /// <remarks>
    /// Most rule tests assert the diagnostic's full message text. The package ships English
    /// resources only, but message arguments are still formatted with the current culture,
    /// so a suite whose verdict depends on the developer's OS language would report a
    /// disagreement that looks like a code difference.
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
