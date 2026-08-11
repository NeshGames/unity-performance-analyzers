using System.Reflection;

using Microsoft.CodeAnalysis.Diagnostics;

using UnityPerformanceAnalyzers;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Reads the <see cref="UpaClaimAttribute"/> off the analyzers so the generated presets and the
/// analyzers themselves decide "is this about per-frame cost" from one source.
/// </summary>
/// <remarks>
/// They used to answer it separately — the analyzers from the claim, the preset generator from
/// the diagnostic category — and disagreed on UPA0019, which is categorised Performance and
/// reports a defect. The presets downgraded it in editor folders while the analyzer kept
/// reporting it, and nothing in either output said so.
/// </remarks>
internal static class UpaClaims
{
    /// <summary>Every diagnostic id whose analyzer declares this kind of claim, sorted.</summary>
    public static string[] RuleIdsClaiming(UpaClaimKind kind)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in typeof(UpaClaimAttribute).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            {
                continue;
            }

            var claim = type.GetCustomAttribute<UpaClaimAttribute>();
            if (claim is null || claim.Kind != kind)
            {
                continue;
            }

            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                ids.Add(descriptor.Id);
            }
        }

        return ids.ToArray();
    }
}
