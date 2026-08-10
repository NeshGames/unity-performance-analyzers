using System.Collections.Immutable;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>One baseline entry the run did not use up, and by how much.</summary>
internal sealed record StaleEntry(BaselineKey Key, int Recorded, int Observed)
{
    /// <summary>Quota left unused. Never negative; more occurrences than recorded is growth.</summary>
    public int Unused => Math.Max(Recorded - Observed, 0);
}

/// <summary>What a baseline did to one run's diagnostics.</summary>
internal sealed record BaselineOutcome(
    ImmutableArray<DiagnosticRecord> Reported,
    long SuppressedCount,
    long? StaleCount,
    ImmutableArray<StaleEntry> Stale);

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

        var stale = Stale(baseline, observed, analyzedFiles);

        return new BaselineOutcome(
            reported.ToImmutable(),
            suppressed,
            // The total is withheld when the run could not tell, and the list goes with it:
            // reporting entries as stale off an under-reported analysis invites deleting quota
            // that is still doing its job.
            analysisIsComplete ? stale.Sum(entry => (long)entry.Unused) : null,
            analysisIsComplete ? stale : ImmutableArray<StaleEntry>.Empty);
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
    private static ImmutableArray<StaleEntry> Stale(
        BaselineDocument baseline,
        IReadOnlyDictionary<BaselineKey, int> observed,
        IReadOnlyCollection<string> analyzedFiles)
    {
        var covered = new HashSet<string>(analyzedFiles, StringComparer.Ordinal);
        var stale = ImmutableArray.CreateBuilder<StaleEntry>();

        foreach (var entry in baseline.Entries)
        {
            if (!covered.Contains(entry.Key.File))
            {
                continue;
            }

            var seen = observed.TryGetValue(entry.Key, out var m) ? m : 0;
            if (seen < entry.Count)
            {
                stale.Add(new StaleEntry(entry.Key, entry.Count, seen));
            }
        }

        return Ordered(stale.ToImmutable());
    }

    /// <summary>
    /// The order both the report and the pruned file use: the same one a written baseline
    /// uses, so a prune produces a diff a reviewer can read.
    /// </summary>
    private static ImmutableArray<StaleEntry> Ordered(ImmutableArray<StaleEntry> entries) =>
        entries
            .OrderBy(e => e.Key.File, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Rule, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Type, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Member, StringComparer.Ordinal)
            .ToImmutableArray();

    /// <summary>
    /// The baseline with unused quota removed: counts drop to what this run observed, entries
    /// nothing was observed for are dropped, and entries whose file is gone are dropped too.
    /// </summary>
    /// <remarks>
    /// Only ever subtracts. That is the whole difference from regenerating: a violation
    /// introduced since the baseline was written keeps being reported instead of being frozen
    /// into the contract by an operation the user reached for to make the contract smaller.
    /// </remarks>
    public static BaselineDocument Prune(
        BaselineDocument baseline,
        ImmutableArray<DiagnosticRecord> diagnostics,
        IReadOnlyCollection<string> analyzedFiles,
        Func<string, bool> fileStillExists)
    {
        var observed = diagnostics
            .GroupBy(KeyOf)
            .ToDictionary(g => g.Key, g => g.Count());

        var covered = new HashSet<string>(analyzedFiles, StringComparer.Ordinal);
        var kept = ImmutableArray.CreateBuilder<BaselineEntry>();

        foreach (var entry in baseline.Entries)
        {
            if (!covered.Contains(entry.Key.File))
            {
                // Not analyzed and still on disk cannot happen here - the caller refuses that
                // run before reaching this point - so what is left is a file that is gone.
                if (fileStillExists(entry.Key.File))
                {
                    kept.Add(entry);
                }

                continue;
            }

            var seen = observed.TryGetValue(entry.Key, out var m) ? m : 0;
            var count = Math.Min(entry.Count, seen);
            if (count > 0)
            {
                kept.Add(new BaselineEntry(entry.Key, count));
            }
        }

        return new BaselineDocument(kept.ToImmutable());
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
