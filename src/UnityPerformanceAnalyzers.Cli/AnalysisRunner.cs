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
    ImmutableDictionary<string, string?> Properties);

/// <summary>Everything one run produced.</summary>
internal sealed record AnalysisResult(
    ImmutableArray<DiagnosticRecord> Diagnostics,
    ImmutableArray<string> ExcludedRules,
    int CompileErrorCount,
    ImmutableArray<string> AnalyzerFailures);

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

        // Compile errors are counted, never reported: this tool speaks for the analyzers,
        // not for the compiler, and a partial file set produces them routinely.
        var compileErrorCount = input.Compilation
            .GetDiagnostics(cancellationToken)
            .Count(d => d.Severity == DiagnosticSeverity.Error);

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

        var records = diagnostics
            .Where(d => d.Id != AnalyzerFailureId)
            .Select(ToRecord)
            .OrderBy(r => r.File, StringComparer.Ordinal)
            .ThenBy(r => r.Line)
            .ThenBy(r => r.Column)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToImmutableArray();

        return new AnalysisResult(records, excludedRules, compileErrorCount, analyzerFailures);
    }

    private static DiagnosticRecord ToRecord(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var descriptor = diagnostic.Descriptor;

        return new DiagnosticRecord(
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
