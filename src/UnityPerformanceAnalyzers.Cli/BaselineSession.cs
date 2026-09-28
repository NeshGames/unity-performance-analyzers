using System.IO;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>The baseline for one run: reading it or replacing it from a complete analysis.</summary>
internal sealed class BaselineSession
{
    private readonly CliOptions _options;

    private BaselineSession(CliOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// The session for this run, or null when no baseline path was supplied. Analysis populates
    /// stable key fields only in this mode, so all baseline operations stay behind this entry.
    /// </summary>
    public static BaselineSession? Open(CliOptions options)
        => options.UsesBaseline ? new BaselineSession(options) : null;

    /// <summary>
    /// Removes findings the existing contract already accounts for. Reading a baseline places
    /// no completeness demand on the run: narrowing the report to changed files is supported.
    /// </summary>
    public AnalysisResult Filter(AnalysisResult result)
    {
        if (_options.BaselinePath is not { } path)
        {
            return result;
        }

        var outcome = BaselineFilter.Apply(result.Diagnostics, BaselineDocument.Read(path));

        return result with
        {
            Diagnostics = outcome.Reported,
            BaselineSuppressedCount = outcome.SuppressedCount,
        };
    }

    /// <summary>
    /// Replaces the contract with the findings from this complete run and returns the entry
    /// count, or null when this run was not asked to update it.
    /// </summary>
    public int? Update(AnalysisResult result)
    {
        if (_options.UpdateBaselinePath is not { } target)
        {
            return null;
        }

        BaselineWriter.EnsureRunIsUpdatable(_options, result);

        // Refuse links before File.Exists or reading the target: the caller named the link,
        // not the file at its other end.
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
