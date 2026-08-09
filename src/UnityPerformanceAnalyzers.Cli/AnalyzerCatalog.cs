using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using UnityPerformanceAnalyzers.Catalog;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// What this tool needs from the analyzer assembly: the analyzers to run, and the rule list
/// behind <c>--list-rules</c>. The walk itself lives in <see cref="UpaRuleCatalog"/>, shared
/// with the manifest generator so the two cannot disagree about what a rule is.
/// </summary>
internal static class AnalyzerCatalog
{
    public static string ToolVersion => UpaRuleCatalog.Version;

    /// <summary>
    /// Analyzers to run. Compilation-wide rules are excluded unless the caller declares
    /// the file set to be a whole assembly: a partial compilation
    /// makes their "nothing else does X" premise trivially true.
    /// </summary>
    public static (ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<string> ExcludedRules) Load(bool wholeAssembly)
    {
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        var excluded = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (type, instance) in UpaRuleCatalog.Analyzers())
        {
            if (!wholeAssembly && type.GetCustomAttribute<CompilationWideRuleAttribute>() is object)
            {
                foreach (var descriptor in instance.SupportedDiagnostics)
                {
                    excluded.Add(descriptor.Id);
                }

                continue;
            }

            analyzers.Add(instance);
        }

        return (analyzers.ToImmutable(), excluded.ToImmutableArray());
    }

    public static ImmutableArray<UpaRule> Rules() => UpaRuleCatalog.Rules().ToImmutableArray();

    /// <summary>Every diagnostic id the assembly exports — used by --all-warn.</summary>
    public static ImmutableArray<string> AllRuleIds() =>
        UpaRuleCatalog.Rules().Select(rule => rule.Id).ToImmutableArray();
}
