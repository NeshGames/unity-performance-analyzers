using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using UnityPerformanceAnalyzers;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// One rule as the analyzer assembly describes itself. Read by reflection rather than from
/// the committed catalog: that file is allowed to go stale between releases, so generating
/// anything from it would inherit the drift the generation exists to remove.
/// </summary>
public sealed record CatalogRule(
    string Id,
    string Title,
    string Category,
    string DefaultSeverity,
    bool EnabledByDefault,
    bool HotPath,
    string? Condition,
    string HelpUri);

/// <summary>Reads the rule set out of the analyzer assembly.</summary>
public static class RuleCatalog
{
    /// <summary>Every rule the analyzer assembly exports, ordered by ID.</summary>
    public static IReadOnlyList<CatalogRule> Collect()
    {
        var analyzerAssembly = typeof(UPA1001NonExhaustiveEnumSwitchAnalyzer).Assembly;
        var rules = new SortedDictionary<string, CatalogRule>(StringComparer.Ordinal);

        foreach (var type in analyzerAssembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            {
                continue;
            }

            var isHotPath = type.GetCustomAttribute<HotPathRuleAttribute>() is not null;
            var condition = type.GetCustomAttribute<ConditionalRuleAttribute>()?.Condition;

            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                // Rules with several message formats declare one descriptor per format under
                // a single ID; the first carries the catalog data.
                if (rules.ContainsKey(descriptor.Id))
                {
                    continue;
                }

                rules[descriptor.Id] = new CatalogRule(
                    descriptor.Id,
                    descriptor.Title.ToString(),
                    descriptor.Category,
                    descriptor.DefaultSeverity.ToString(),
                    descriptor.IsEnabledByDefault,
                    isHotPath,
                    condition,
                    descriptor.HelpLinkUri);
            }
        }

        return rules.Values.ToArray();
    }

    /// <summary>The version the analyzer assembly was built with.</summary>
    public static string Version()
    {
        var analyzerAssembly = typeof(UPA1001NonExhaustiveEnumSwitchAnalyzer).Assembly;
        return analyzerAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? analyzerAssembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }
}
