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
/// Resolves every per-rule severity the run should apply, and does it per syntax tree —
/// an .editorconfig can scope a rule to one file pattern, so flattening the settings into
/// one compilation-wide map would let one file's configuration govern the others (and
/// make results depend on the order files were passed).
///
/// Precedence, weakest first: the ruleset (whole compilation), the .editorconfig entry
/// matching that file, then --all-warn, which forces everything on because that is its
/// entire purpose.
/// </summary>
internal sealed class SeverityOptionsProvider : SyntaxTreeOptionsProvider
{
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, ReportDiagnostic>> _perTree;
    private readonly ImmutableDictionary<string, ReportDiagnostic> _global;
    private readonly ReportDiagnostic _generalRulesetAction;
    private readonly ImmutableHashSet<string> _knownRuleIds;
    private readonly ImmutableHashSet<string> _forcedWarn;

    private SeverityOptionsProvider(
        ImmutableDictionary<string, ImmutableDictionary<string, ReportDiagnostic>> perTree,
        ImmutableDictionary<string, ReportDiagnostic> global,
        ReportDiagnostic generalRulesetAction,
        ImmutableHashSet<string> knownRuleIds,
        ImmutableHashSet<string> forcedWarn)
    {
        _perTree = perTree;
        _global = global;
        _generalRulesetAction = generalRulesetAction;
        _knownRuleIds = knownRuleIds;
        _forcedWarn = forcedWarn;
    }

    public static SeverityOptionsProvider Create(
        ImmutableDictionary<string, ImmutableDictionary<string, ReportDiagnostic>> editorConfigPerTree,
        RulesetSeverities ruleset,
        bool allWarn)
    {
        var knownRuleIds = AnalyzerCatalog.AllRuleIds().ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        return new SeverityOptionsProvider(
            editorConfigPerTree,
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
        if (_forcedWarn.Contains(diagnosticId))
        {
            severity = ReportDiagnostic.Warn;
            return true;
        }

        if (_perTree.TryGetValue(tree.FilePath, out var options) &&
            options.TryGetValue(diagnosticId, out severity))
        {
            return true;
        }

        severity = default;
        return false;
    }

    // Roslyn consults this after the per-tree lookup, which is exactly where the
    // whole-compilation ruleset belongs: a file-scoped .editorconfig entry outranks it.
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

        // A ruleset's <IncludeAll Action="..."/> sets the policy for everything it did not
        // name individually — ignoring it would let this tool report rules the ruleset
        // switched off, or miss a blanket promotion. Scoped to rules this build knows,
        // since those are the only diagnostics it reports.
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
