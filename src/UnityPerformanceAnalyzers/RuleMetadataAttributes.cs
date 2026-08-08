using System;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Marks an analyzer whose rules report on per-frame hot paths only. Purely
    /// declarative metadata: the RuleManifest tool reflects over these attributes to build
    /// the rule catalog, so the flag travels with the rule instead of living in a table
    /// that can drift. Applies to every diagnostic ID the analyzer exports. Analyzers that
    /// merely offer an opt-in hot-path narrowing (UPA0003) do not carry this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HotPathRuleAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks an analyzer whose verdict depends on the whole compilation rather than on the
    /// code in front of it — a rule that reports the ABSENCE of something elsewhere (for
    /// example "no type derives from this class"). Tools that analyze an arbitrary file
    /// subset rather than a full assembly must skip these, because a partial compilation
    /// makes the absence trivially true and the report a false positive.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CompilationWideRuleAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks an analyzer that registers only under a condition — a referenced ecosystem
    /// assembly (UniTask, R3, DOTween) or the UPA_TARGET_WEBGL define ("WebGL"). Purely
    /// declarative metadata for the RuleManifest catalog; the actual gating lives in the
    /// analyzer's CompilationStart logic. Applies to every diagnostic ID the analyzer
    /// exports.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ConditionalRuleAttribute : Attribute
    {
        /// <summary>Creates the marker with the named activation condition.</summary>
        public ConditionalRuleAttribute(string condition)
        {
            Condition = condition;
        }

        /// <summary>"UniTask", "R3", "DOTween", or "WebGL".</summary>
        public string Condition { get; }
    }
}
