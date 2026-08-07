using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Base class carrying the project-wide analyzer skeleton: concurrent execution on,
    /// generated code ignored, and all registration funneled through a compilation-start
    /// action so per-compilation state (profiles, options, type lookups) resolves once.
    /// Rules implement <see cref="InitializeCore"/> only. Analyzers with a reason to
    /// derive from <see cref="DiagnosticAnalyzer"/> directly must reproduce this skeleton
    /// themselves.
    /// </summary>
    public abstract class UpaAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc/>
        public sealed override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(InitializeCore);
        }

        /// <summary>
        /// Per-compilation setup: resolve types/profiles/options and register the node,
        /// operation, or symbol actions for this rule.
        /// </summary>
        private protected abstract void InitializeCore(CompilationStartAnalysisContext ctx);
    }
}
