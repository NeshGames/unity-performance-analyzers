using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// Feeds a single .editorconfig to the analyzers. Analyzer options (upa_hot_path_*,
/// upa_enum_switch_allow_default) and per-rule severities both travel this channel, so a
/// run with --editorconfig sees the same configuration an IDE would.
/// </summary>
internal sealed class EditorConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private static readonly AnalyzerConfigOptions s_empty = new DictionaryOptions(
        ImmutableDictionary<string, string>.Empty);

    private readonly AnalyzerConfigOptions _global;
    private readonly ImmutableDictionary<string, AnalyzerConfigOptions> _perTree;

    private EditorConfigOptionsProvider(
        AnalyzerConfigOptions global,
        ImmutableDictionary<string, AnalyzerConfigOptions> perTree)
    {
        _global = global;
        _perTree = perTree;
    }

    /// <summary>
    /// The two things an .editorconfig carries for us, which travel different Roslyn
    /// channels: analyzer key/value options (read by the analyzers themselves) and
    /// per-rule severities (applied by the compilation, not visible to analyzers).
    /// Severities stay keyed by file, because a section can scope a rule to one pattern.
    /// </summary>
    public sealed record Parsed(
        AnalyzerConfigOptionsProvider Provider,
        ImmutableDictionary<string, ImmutableDictionary<string, ReportDiagnostic>> SeveritiesByFile);

    public static Parsed Create(
        string? editorConfigPath,
        ImmutableArray<Microsoft.CodeAnalysis.SyntaxTree> trees)
    {
        if (editorConfigPath is null)
        {
            return new Parsed(
                new EditorConfigOptionsProvider(
                    s_empty,
                    ImmutableDictionary<string, AnalyzerConfigOptions>.Empty),
                ImmutableDictionary<string, ImmutableDictionary<string, ReportDiagnostic>>.Empty);
        }

        var config = AnalyzerConfig.Parse(
            File.ReadAllText(editorConfigPath),
            Path.GetFullPath(editorConfigPath));
        var set = AnalyzerConfigSet.Create(ImmutableArray.Create(config));

        var perTreeOptions = ImmutableDictionary.CreateBuilder<string, AnalyzerConfigOptions>(StringComparer.Ordinal);
        var perTreeSeverities = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, ReportDiagnostic>>(
            StringComparer.Ordinal);

        foreach (var tree in trees)
        {
            var result = set.GetOptionsForSourcePath(Path.GetFullPath(tree.FilePath));
            perTreeOptions[tree.FilePath] = new DictionaryOptions(result.AnalyzerOptions);

            // dotnet_diagnostic.<id>.severity lands in TreeOptions, which Roslyn applies
            // through the compilation rather than handing to analyzers — so it has to be
            // lifted out here, and kept per file: a section can target one pattern, and
            // merging every file's settings into one map would let the last file parsed
            // decide for all of them.
            var severities = ImmutableDictionary.CreateBuilder<string, ReportDiagnostic>(StringComparer.Ordinal);
            foreach (var entry in result.TreeOptions)
            {
                severities[CanonicalRuleId(entry.Key)] = entry.Value;
            }

            perTreeSeverities[tree.FilePath] = severities.ToImmutable();
        }

        return new Parsed(
            new EditorConfigOptionsProvider(
                new DictionaryOptions(set.GlobalConfigOptions.AnalyzerOptions),
                perTreeOptions.ToImmutable()),
            perTreeSeverities.ToImmutable());
    }

    /// <summary>
    /// .editorconfig keys are lowercased when parsed, but severity lookups on the
    /// compilation are case-sensitive — so "upa0001" has to become "UPA0001" or the
    /// setting is dropped without a word. Unknown ids pass through untouched.
    /// </summary>
    private static string CanonicalRuleId(string id) =>
        s_canonicalIds.TryGetValue(id, out var canonical) ? canonical : id;

    private static readonly ImmutableDictionary<string, string> s_canonicalIds =
        AnalyzerCatalog.AllRuleIds().ToImmutableDictionary(
            id => id,
            id => id,
            StringComparer.OrdinalIgnoreCase);

    public override AnalyzerConfigOptions GlobalOptions => _global;

    public override AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.SyntaxTree tree) =>
        _perTree.TryGetValue(tree.FilePath, out var options) ? options : s_empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_empty;

    private sealed class DictionaryOptions : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> _values;

        public DictionaryOptions(ImmutableDictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, out string value) =>
            _values.TryGetValue(key, out value!);
    }
}
