using System.IO;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// The baseline for one run: reading it, and writing it.
/// </summary>
/// <remarks>
/// <para>
/// Its existence is the precondition the pieces underneath depend on and none of them could
/// state. The key a baseline entry is matched by comes from four fields on a diagnostic, and
/// <see cref="AnalysisRunner"/> only fills them when the run was told a baseline is in play.
/// Building a document from a run that was not would produce entries keyed on empty strings,
/// written happily and rejected on the next read. That held before only because one call site
/// happened to be the only one.
/// </para>
/// <para>
/// The write path also had an order: check the run may be written from, read what is already
/// there and check the new run covers it, build, write. Two of those were advisory -- the
/// writer did not check for itself, so skipping one was silent. They are steps inside a
/// method now, and there is no way to call them in the wrong sequence because there is no way
/// to call them at all.
/// </para>
/// </remarks>
internal sealed class BaselineSession
{
    private readonly CliOptions _options;

    private BaselineSession(CliOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// The session for this run, or null when it has no baseline. This is the only way to
    /// reach the operations below, and it answers the same question the analysis asked when
    /// it decided whether to populate the key fields.
    /// </summary>
    public static BaselineSession? Open(CliOptions options)
        => options.UsesBaseline ? new BaselineSession(options) : null;

    /// <summary>
    /// The run with everything the baseline already accounts for removed. Reading a baseline
    /// places no completeness demand on the run: comparing a single changed file against the
    /// contract is the ordinary way to use this. Only producing the contract needs a full run.
    /// </summary>
    public AnalysisResult Filter(AnalysisResult result)
    {
        if (_options.BaselinePath is not { } path)
        {
            return result;
        }

        var outcome = BaselineFilter.Apply(
            result.Diagnostics,
            BaselineDocument.Read(path),
            result.AnalyzedFiles,
            result.IsComplete);

        return result with
        {
            Diagnostics = outcome.Reported,
            BaselineSuppressedCount = outcome.SuppressedCount,
            BaselineStaleCount = outcome.StaleCount,
            BaselineStale = outcome.Stale,
        };
    }

    /// <summary>
    /// Rewrites the baseline with unused quota removed, returning how many entries remain, or
    /// null when this run was not asked to prune.
    /// </summary>
    /// <remarks>
    /// The preconditions are the write path's, for the write path's reasons, plus one that
    /// matters more here than there: the run has to cover every file the baseline names that
    /// is still on disk. Pruning from a single changed file would find no occurrences for
    /// every other file and take that for debt paid off, clearing the whole contract in one
    /// command reached for precisely because it sounds safer than regenerating.
    /// </remarks>
    public int? Prune(AnalysisResult result)
    {
        if (!_options.PruneBaseline || _options.BaselinePath is not { } path)
        {
            return null;
        }

        BaselineWriter.EnsureRunIsPrunable(_options, result);
        BaselineWriter.EnsureNotSymbolicLink(path);

        var existing = BaselineDocument.Read(path);
        BaselineWriter.EnsureCoversExistingBaseline(path, existing, result.AnalyzedFiles);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        var root = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;

        // The diagnostics the filter already suppressed are the occurrences that matched, so
        // pruning has to see the unfiltered set. Reported plus suppressed is not recoverable
        // after the fact, which is why this takes the pre-filter result.
        var pruned = BaselineFilter.Prune(
            existing,
            result.Diagnostics,
            result.AnalyzedFiles,
            file => File.Exists(Path.Combine(root, file)));

        BaselineWriter.Write(path, pruned);
        return pruned.Entries.Length;
    }

    /// <summary>
    /// Writes the baseline for this run and returns the entry count, or null when this run
    /// was not asked to write one. Throws rather than write something misleading.
    /// </summary>
    public int? Write(AnalysisResult result)
    {
        if (_options.WriteBaselinePath is not { } target)
        {
            return null;
        }

        BaselineWriter.EnsureRunIsWritable(_options, result);

        // Before File.Exists, and before the file is read. A link pointing at something that is
        // not a baseline would otherwise be rejected for its target's contents - the caller
        // hears that their file is malformed when what is wrong is that it is not their file.
        BaselineWriter.EnsureNotSymbolicLink(target);

        if (File.Exists(target))
        {
            BaselineWriter.EnsureCoversExistingBaseline(
                target, BaselineDocument.Read(target), result.AnalyzedFiles);
        }

        var document = BaselineFilter.Build(result.Diagnostics);
        BaselineWriter.Write(target, document);
        return document.Entries.Length;
    }
}
