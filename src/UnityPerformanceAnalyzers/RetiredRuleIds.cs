namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Diagnostic IDs that shipped previously and must never be reused for a different rule.
    /// Keep this list after the analyzer implementation, resources and live documentation are gone.
    /// </summary>
    internal static class RetiredRuleIds
    {
        internal static readonly string[] All =
        {
            "UPA0022",
            "UPA1000",
            "UPA2001",
        };
    }
}
