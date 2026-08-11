using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0025: Reports finalizer declarations in player assemblies (editor assemblies are
    /// skipped by name — see UpaProfile.IsEditorAssembly). Unity's guidance is to not use
    /// C# finalizers in runtime code:
    /// they delay memory reclamation across garbage collections and run on the finalizer
    /// thread. Deterministic cleanup belongs in IDisposable.
    /// </summary>
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0025FinalizerAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0025";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            if (ctx.IsEditorAssembly)
            {
                return;
            }

            ctx.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;

            if (method.MethodKind != MethodKind.Destructor)
            {
                return;
            }

            var location = method.Locations.FirstOrDefault();
            if (location is object)
            {
                context.ReportDiagnostic(UpaDiagnostics.Create(Rule, location));
            }
        }
    }
}
