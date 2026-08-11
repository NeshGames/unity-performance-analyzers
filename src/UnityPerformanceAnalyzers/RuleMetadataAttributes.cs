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

    /// <summary>What kind of claim a rule makes about the code it reports.</summary>
    /// <remarks>
    /// The distinction decides whether a finding survives in code Unity strips from a player
    /// build. Per-frame cost is meaningless in an editor-only method; a defect is not.
    /// </remarks>
    public enum UpaClaimKind
    {
        /// <summary>The rule reports work that costs something when it repeats.</summary>
        PerFrameCost,

        /// <summary>The rule reports a defect, which is a defect wherever it runs.</summary>
        Correctness,
    }

    /// <summary>
    /// Declares what kind of claim an analyzer makes. Required on every analyzer, and enforced
    /// by a contract test rather than by convention.
    /// </summary>
    /// <remarks>
    /// The alternative — deciding per rule from its category, or from a reading of what the
    /// rule "is about" — is not mechanically checkable. Ecosystem rules carry both kinds
    /// (UPA2000 is cost, UPA2012 is correctness), so category cannot answer it, and an
    /// implementer who guesses wrong produces output indistinguishable from a correct one:
    /// a rule wrongly treated as cost goes quiet in editor-only methods and leaves no trace.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class UpaClaimAttribute : Attribute
    {
        /// <summary>Creates the marker with the kind of claim this analyzer makes.</summary>
        public UpaClaimAttribute(UpaClaimKind kind)
        {
            Kind = kind;
        }

        /// <summary>Whether the rule reports per-frame cost or a defect.</summary>
        public UpaClaimKind Kind { get; }
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
