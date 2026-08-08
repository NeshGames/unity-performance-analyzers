using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>One rule as the catalog sees it; mirrors package/Editor/rules.json plus compilationWide.</summary>
internal sealed record RuleEntry(
    string Id,
    string Title,
    string Category,
    string DefaultSeverity,
    bool EnabledByDefault,
    bool HotPath,
    string? Condition,
    bool CompilationWide,
    string HelpUri);

/// <summary>
/// Reflects the analyzer assembly for both the runnable analyzers and the rule catalog.
/// Reading metadata off the analyzers themselves (rather than a committed rules.json)
/// means the catalog can never be stale relative to the DLL in use.
/// </summary>
internal static class AnalyzerCatalog
{
    private static Assembly AnalyzerAssembly => typeof(UPA1001NonExhaustiveEnumSwitchAnalyzer).Assembly;

    public static string ToolVersion =>
        AnalyzerAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
        ?? AnalyzerAssembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    private static IEnumerable<Type> AnalyzerTypes() =>
        AnalyzerAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// Analyzers to run. Compilation-wide rules are excluded unless the caller declares
    /// the file set to be a whole assembly: a partial compilation
    /// makes their "nothing else does X" premise trivially true.
    /// </summary>
    public static (ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<string> ExcludedRules) Load(bool wholeAssembly)
    {
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        var excluded = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in AnalyzerTypes())
        {
            var instance = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
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

    public static ImmutableArray<RuleEntry> Rules()
    {
        var rules = new SortedDictionary<string, RuleEntry>(StringComparer.Ordinal);

        foreach (var type in AnalyzerTypes())
        {
            var hotPath = type.GetCustomAttribute<HotPathRuleAttribute>() is object;
            var condition = type.GetCustomAttribute<ConditionalRuleAttribute>()?.Condition;
            var compilationWide = type.GetCustomAttribute<CompilationWideRuleAttribute>() is object;
            var instance = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;

            foreach (var descriptor in instance.SupportedDiagnostics)
            {
                // A rule may export several descriptors under one id (UPA2012); first wins.
                if (rules.ContainsKey(descriptor.Id))
                {
                    continue;
                }

                rules[descriptor.Id] = new RuleEntry(
                    descriptor.Id,
                    descriptor.Title.ToString(),
                    descriptor.Category,
                    descriptor.DefaultSeverity.ToString(),
                    descriptor.IsEnabledByDefault,
                    hotPath,
                    condition,
                    compilationWide,
                    descriptor.HelpLinkUri);
            }
        }

        return rules.Values.ToImmutableArray();
    }

    /// <summary>Every diagnostic id the assembly exports — used by --all-warn.</summary>
    public static ImmutableArray<string> AllRuleIds() =>
        Rules().Select(r => r.Id).ToImmutableArray();
}
