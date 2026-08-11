using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Base class carrying the project-wide analyzer skeleton: concurrent execution on,
    /// generated code ignored, and all registration funneled through a compilation-start
    /// action that hands the rule a <see cref="UpaCompilationContext"/> — the profile, the
    /// hot-path classification and the type lookups, resolved once for this compilation.
    /// Rules implement <see cref="InitializeCore"/> only. Analyzers with a reason to
    /// derive from <see cref="DiagnosticAnalyzer"/> directly must reproduce this skeleton
    /// themselves.
    /// </summary>
    /// <remarks>
    /// The context is created inside the callback and passed by argument. Nothing derived
    /// from a compilation may be stored in a field: Roslyn serves many compilations from one
    /// analyzer instance, so an instance field is shared state between them.
    /// </remarks>
    public abstract class UpaAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc/>
        public sealed override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            // Read off the concrete analyzer rather than passed in by each rule: a rule that
            // could forget to pass it would go quiet in editor-only methods with nothing in the
            // output to say so. A contract test requires the attribute on every analyzer.
            var claim = (UpaClaimAttribute?)System.Attribute.GetCustomAttribute(
                GetType(), typeof(UpaClaimAttribute));

            context.RegisterCompilationStartAction(
                start => InitializeCore(new UpaCompilationContext(start, claim?.Kind ?? UpaClaimKind.Correctness)));
        }

        /// <summary>
        /// Per-compilation setup: read what the rule needs off <paramref name="ctx"/> and
        /// register the node, operation, or symbol actions for this rule.
        /// </summary>
        private protected abstract void InitializeCore(UpaCompilationContext ctx);
    }
}
