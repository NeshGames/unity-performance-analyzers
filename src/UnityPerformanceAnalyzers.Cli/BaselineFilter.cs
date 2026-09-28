using System.Collections.Immutable;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>What a baseline did to one run's diagnostics.</summary>
internal sealed record BaselineOutcome(
    ImmutableArray<DiagnosticRecord> Reported,
    long SuppressedCount);

/// <summary>Applies a baseline to a run, and builds the one a run would write.</summary>
internal static class BaselineFilter
{
    /// <summary>
    /// Suppresses what the baseline already accounts for and reports the rest.
    /// </summary>
    /// <remarks>
    /// Matching is by occurrence count, not set membership. The key holds no line number, so
    /// two identical snippets in one member produce one key; under set semantics the second
    /// occurrence is either always let through or always blocked, and both are wrong without
    /// being visible.
    /// </remarks>
    public static BaselineOutcome Apply(
        ImmutableArray<DiagnosticRecord> diagnostics,
        BaselineDocument baseline)
    {
        var quotas = baseline.Counts;
        var reported = ImmutableArray.CreateBuilder<DiagnosticRecord>();
        // Wide enough to hold the sum: an entry may declare up to a million occurrences and
        // nothing caps how many entries a baseline holds, so int can overflow on a hostile file.
        var suppressed = 0L;

        foreach (var group in diagnostics.GroupBy(KeyOf))
        {
            // Ordered so the choice of which occurrences to suppress is fixed. Identical
            // occurrences are indistinguishable, and without an order the same source would
            // report a different location under a different build of this tool.
            var occurrences = group
                .OrderBy(d => d.Line)
                .ThenBy(d => d.Column)
                .ToArray();

            var quota = quotas.TryGetValue(group.Key, out var n) ? n : 0;
            var take = Math.Min(quota, occurrences.Length);

            suppressed += take;
            for (var i = take; i < occurrences.Length; i++)
            {
                reported.Add(occurrences[i]);
            }
        }

        return new BaselineOutcome(reported.ToImmutable(), suppressed);
    }

    /// <summary>The baseline a complete run writes for its current diagnostics.</summary>
    public static BaselineDocument Build(ImmutableArray<DiagnosticRecord> diagnostics) =>
        new(diagnostics
            .GroupBy(KeyOf)
            .Select(g => new BaselineEntry(g.Key, g.Count()))
            .ToImmutableArray());

    private static BaselineKey KeyOf(DiagnosticRecord record) => new(
        record.BaselineFile,
        record.Id,
        record.Type,
        record.Member,
        record.Snippet);
}
