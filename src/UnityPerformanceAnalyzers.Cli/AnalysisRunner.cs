using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>One reported diagnostic, flattened for output.</summary>
internal sealed record DiagnosticRecord(
    string Id,
    string Severity,
    string Title,
    string Message,
    string File,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string HelpUri,
    ImmutableDictionary<string, string?> Properties)
{
    /// <summary>Normalized path for baseline keys; empty when no baseline is in play.</summary>
    public string BaselineFile { get; init; } = string.Empty;

    /// <summary>Enclosing type chain for baseline keys; empty when no baseline is in play.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Enclosing member for baseline keys; empty when no baseline is in play.</summary>
    public string Member { get; init; } = string.Empty;

    /// <summary>Normalized source line for baseline keys; empty when no baseline is in play.</summary>
    public string Snippet { get; init; } = string.Empty;
}

/// <summary>
/// One compiler error. Not a finding — it is the reason a run's findings may be missing.
/// </summary>
internal sealed record CompileError(string Id, string Message, string File, int Line, int Column);

/// <summary>Everything one run produced.</summary>
internal sealed record AnalysisResult(
    ImmutableArray<DiagnosticRecord> Diagnostics,
    ImmutableArray<string> ExcludedRules,
    ImmutableArray<CompileError> CompileErrors,
    ImmutableArray<string> AnalyzerFailures)
{
    /// <summary>How many compile errors the run hit. Never a finding, never weighed by --fail-on.</summary>
    public int CompileErrorCount => CompileErrors.Length;

    /// <summary>Baseline-normalized paths of the files this run analyzed.</summary>
    public ImmutableArray<string> AnalyzedFiles { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>How many diagnostics a baseline suppressed.</summary>
    public long BaselineSuppressedCount { get; init; }

    /// <summary>
    /// Baseline quota this run did not use, or null when the analysis was too incomplete to
    /// tell. Zero means checked and none stale; null means this run cannot say.
    /// </summary>
    public long? BaselineStaleCount { get; init; }

    /// <summary>
    /// Whether the run is complete enough for its numbers to be trusted. Unresolved types keep
    /// rules from firing, which reads as debt that has been paid off.
    /// </summary>
    public bool IsComplete => AnalyzerFailures.IsEmpty && CompileErrorCount == 0;
}

/// <summary>Runs the analyzers over a built compilation and normalizes what they report.</summary>
internal static class AnalysisRunner
{
    /// <summary>
    /// Roslyn reports an analyzer that threw as this diagnostic — a warning, which would
    /// otherwise slip past an error-level threshold while the analysis it was supposed to
    /// perform never happened.
    /// </summary>
    private const string AnalyzerFailureId = "AD0001";

    public static AnalysisResult Run(
        CliOptions options,
        ImmutableArray<DiagnosticAnalyzer>? analyzerOverride = null,
        CancellationToken cancellationToken = default)
    {
        var input = CompilationBuilder.Build(options);
        var (catalogAnalyzers, excludedRules) = AnalyzerCatalog.Load(options.WholeAssembly);
        var analyzers = analyzerOverride ?? catalogAnalyzers;

        // Compile errors never become findings: this tool speaks for the analyzers, not for
        // the compiler, and a partial file set produces them routinely. They are listed
        // separately because a count alone leaves nothing to act on - under --whole-assembly
        // they are fatal and writing a baseline is refused, and "145 compile errors" does not
        // tell anyone which type is missing.
        var compileErrors = input.Compilation
            .GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => ToCompileError(d))
            .OrderBy(e => e.File, StringComparer.Ordinal)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.Column)
            .ToImmutableArray();

        var diagnostics = input.Compilation
            .WithAnalyzers(analyzers, input.Options)
            .GetAnalyzerDiagnosticsAsync(cancellationToken)
            .GetAwaiter()
            .GetResult();

        // An analyzer crash is an execution failure, not a finding: it is reported
        // separately so it cannot be weighed against a severity threshold.
        var analyzerFailures = diagnostics
            .Where(d => d.Id == AnalyzerFailureId)
            .Select(d => d.GetMessage())
            .ToImmutableArray();

        // Key fields need a semantic model per diagnostic, so they are only computed when a
        // baseline is actually in play.
        var baselineDirectory = options.UsesBaseline ? options.BaselineDirectory : null;

        var records = diagnostics
            .Where(d => d.Id != AnalyzerFailureId)
            .Select(d => ToRecord(d, input.Compilation, baselineDirectory))
            .OrderBy(r => r.File, StringComparer.Ordinal)
            .ThenBy(r => r.Line)
            .ThenBy(r => r.Column)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToImmutableArray();

        var result = new AnalysisResult(records, excludedRules, compileErrors, analyzerFailures);
        if (baselineDirectory is null)
        {
            return result;
        }

        return result with
        {
            AnalyzedFiles = NormalizeInputs(options.Files, baselineDirectory),
        };
    }

    /// <summary>
    /// The analyzed file set in baseline form. A file outside the baseline's directory cannot
    /// be named by a relative key at all, so the contract would not be self-contained.
    /// </summary>
    private static ImmutableArray<string> NormalizeInputs(
        IEnumerable<string> files,
        string baselineDirectory)
    {
        var normalized = ImmutableArray.CreateBuilder<string>();
        foreach (var file in files)
        {
            var value = BaselinePath.Normalize(file, baselineDirectory)
                ?? throw new CliException(
                    $"{file} is outside the baseline directory ({baselineDirectory}); "
                    + "a baseline can only cover files beneath it.");

            normalized.Add(value);
        }

        return normalized.ToImmutable();
    }

    /// <summary>
    /// Same path and position conventions as a finding, so the two read alike in a log — but a
    /// deliberately smaller shape, because a compile error is a fact about the run, not about
    /// the code under review.
    /// </summary>
    private static CompileError ToCompileError(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return new CompileError(
            diagnostic.Id,
            diagnostic.GetMessage(),
            span.Path ?? string.Empty,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
    }

    private static DiagnosticRecord ToRecord(
        Diagnostic diagnostic,
        Compilation compilation,
        string? baselineDirectory)
    {
        var span = diagnostic.Location.GetLineSpan();
        var descriptor = diagnostic.Descriptor;

        var record = new DiagnosticRecord(
            diagnostic.Id,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            descriptor.Title.ToString(),
            diagnostic.GetMessage(),
            span.Path ?? string.Empty,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1,
            descriptor.HelpLinkUri,
            diagnostic.Properties);

        if (baselineDirectory is null)
        {
            return record;
        }

        var symbol = BaselineSymbol.Of(compilation, diagnostic.Location);
        return record with
        {
            BaselineFile = BaselinePath.Normalize(record.File, baselineDirectory) ?? record.File,
            Type = symbol.Type,
            Member = symbol.Member,
            Snippet = Snippet(diagnostic.Location),
        };
    }

    /// <summary>
    /// The source line the diagnostic starts on, with whitespace normalized. Reformatting a
    /// file in bulk is the most ordinary thing to do while adopting these rules, which is
    /// exactly when a baseline is meant to hold.
    /// </summary>
    private static string Snippet(Location location)
    {
        var tree = location.SourceTree;
        if (tree is null)
        {
            return string.Empty;
        }

        var text = tree.GetText();
        var line = text.Lines.GetLineFromPosition(location.SourceSpan.Start).ToString();

        // char.IsWhiteSpace, fixed: ASCII-space, char.IsWhiteSpace and regex \s disagree over
        // tabs, NBSP and U+2009, and a baseline that keys differently under two implementations
        // turns existing debt into new violations while looking entirely normal.
        var normalized = new System.Text.StringBuilder(line.Length);
        var pendingSpace = false;
        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(c);
        }

        return normalized.ToString();
    }

    /// <summary>True when any diagnostic reaches the --fail-on threshold.</summary>
    public static bool ShouldFail(AnalysisResult result, string failOn)
    {
        if (failOn == "none")
        {
            return false;
        }

        var threshold = failOn switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Info,
        };

        return result.Diagnostics.Any(d => ParseSeverity(d.Severity) >= threshold);
    }

    private static DiagnosticSeverity ParseSeverity(string severity) => severity switch
    {
        "error" => DiagnosticSeverity.Error,
        "warning" => DiagnosticSeverity.Warning,
        "info" => DiagnosticSeverity.Info,
        _ => DiagnosticSeverity.Hidden,
    };
}

/// <summary>Signals a usage or execution failure; the entry point maps it to exit code 2.</summary>
internal sealed class CliException : Exception
{
    public CliException(string message) : base(message)
    {
    }
}
