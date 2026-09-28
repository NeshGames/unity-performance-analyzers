using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// A ruleset's two severity channels: entries naming a rule, and the blanket
/// <c>IncludeAll</c> action covering everything it did not name.
/// </summary>
internal sealed record RulesetSeverities(
    ImmutableDictionary<string, ReportDiagnostic> Specific,
    ReportDiagnostic General)
{
    public static readonly RulesetSeverities None = new(
        ImmutableDictionary<string, ReportDiagnostic>.Empty,
        ReportDiagnostic.Default);
}

/// <summary>
/// Resolves whole-compilation diagnostic severity for the CLI.
///
/// Precedence, weakest first: the ruleset, then --all-warn, which forces every known
/// analyzer rule to Warning. Analyzer behavior options do not travel this channel.
/// </summary>
internal sealed class SeverityOptionsProvider : SyntaxTreeOptionsProvider
{
    private readonly ImmutableDictionary<string, ReportDiagnostic> _global;
    private readonly ReportDiagnostic _generalRulesetAction;
    private readonly ImmutableHashSet<string> _knownRuleIds;
    private readonly ImmutableHashSet<string> _forcedWarn;

    private SeverityOptionsProvider(
        ImmutableDictionary<string, ReportDiagnostic> global,
        ReportDiagnostic generalRulesetAction,
        ImmutableHashSet<string> knownRuleIds,
        ImmutableHashSet<string> forcedWarn)
    {
        _global = global;
        _generalRulesetAction = generalRulesetAction;
        _knownRuleIds = knownRuleIds;
        _forcedWarn = forcedWarn;
    }

    public static SeverityOptionsProvider Create(RulesetSeverities ruleset, bool allWarn)
    {
        var knownRuleIds = AnalyzerCatalog.AllRuleIds().ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        return new SeverityOptionsProvider(
            ruleset.Specific,
            ruleset.General,
            knownRuleIds,
            allWarn ? knownRuleIds : ImmutableHashSet<string>.Empty);
    }

    public override bool TryGetDiagnosticValue(
        SyntaxTree tree,
        string diagnosticId,
        CancellationToken cancellationToken,
        out ReportDiagnostic severity)
    {
        severity = default;
        return false;
    }

    public override bool TryGetGlobalDiagnosticValue(
        string diagnosticId,
        CancellationToken cancellationToken,
        out ReportDiagnostic severity)
    {
        if (_forcedWarn.Contains(diagnosticId))
        {
            severity = ReportDiagnostic.Warn;
            return true;
        }

        if (_global.TryGetValue(diagnosticId, out severity))
        {
            return true;
        }

        if (_generalRulesetAction != ReportDiagnostic.Default && _knownRuleIds.Contains(diagnosticId))
        {
            severity = _generalRulesetAction;
            return true;
        }

        severity = default;
        return false;
    }

    public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) =>
        GeneratedKind.Unknown;
}
