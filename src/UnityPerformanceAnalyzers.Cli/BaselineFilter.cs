using System.Collections.Immutable;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>What a baseline did to one run's diagnostics.</summary>
internal sealed record BaselineOutcome(
    ImmutableArray<DiagnosticRecord> Reported,
    long SuppressedCount,
    long? StaleCount);

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
        BaselineDocument baseline,
        IReadOnlyCollection<string> analyzedFiles,
        bool analysisIsComplete)
    {
        var quotas = baseline.Counts;
        var reported = ImmutableArray.CreateBuilder<DiagnosticRecord>();
        // Wide enough to hold the sum: an entry may declare up to a million occurrences and
        // nothing caps how many entries a baseline holds, so int overflows on a hostile file.
        var suppressed = 0L;
        var observed = new Dictionary<BaselineKey, int>();

        foreach (var group in diagnostics.GroupBy(KeyOf))
        {
            // Ordered so the choice of which occurrences to suppress is fixed. Identical
            // occurrences are indistinguishable, and without an order the same source would
            // report a different location under a different build of this tool.
            var occurrences = group
                .OrderBy(d => d.Line)
                .ThenBy(d => d.Column)
                .ToArray();

            observed[group.Key] = occurrences.Length;
            var quota = quotas.TryGetValue(group.Key, out var n) ? n : 0;
            var take = Math.Min(quota, occurrences.Length);

            suppressed += take;
            for (var i = take; i < occurrences.Length; i++)
            {
                reported.Add(occurrences[i]);
            }
        }

        return new BaselineOutcome(
            reported.ToImmutable(),
            suppressed,
            analysisIsComplete ? Stale(baseline, observed, analyzedFiles) : null);
    }

    /// <summary>
    /// Quota the baseline holds that this run did not use up, counted per occurrence rather
    /// than per vanished key.
    /// </summary>
    /// <remarks>
    /// Counting only keys that disappeared entirely would leave a reusable hole: a key
    /// recorded five times but occurring once keeps a quota of five, so four new violations
    /// land in it later without a word. Restricted to files this run analyzed, because
    /// analyzing one changed file is a normal invocation and summing over the whole baseline
    /// would call every other file's entries stale — then advise regenerating, which replaces
    /// a repository-wide contract with a single file's result.
    /// </remarks>
    private static long Stale(
        BaselineDocument baseline,
        IReadOnlyDictionary<BaselineKey, int> observed,
        IReadOnlyCollection<string> analyzedFiles)
    {
        var covered = new HashSet<string>(analyzedFiles, StringComparer.Ordinal);
        var stale = 0L;

        foreach (var entry in baseline.Entries)
        {
            if (!covered.Contains(entry.Key.File))
            {
                continue;
            }

            var seen = observed.TryGetValue(entry.Key, out var m) ? m : 0;
            stale += Math.Max((long)entry.Count - seen, 0L);
        }

        return stale;
    }

    /// <summary>The baseline a run would write for its own diagnostics.</summary>
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
