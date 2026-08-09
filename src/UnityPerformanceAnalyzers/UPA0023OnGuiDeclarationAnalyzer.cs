using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0023: Reports <c>OnGUI</c> declarations on MonoBehaviour-derived types in player
    /// assemblies (editor assemblies are skipped by name — see UpaProfile.IsEditorAssembly).
    /// IMGUI runs every frame and
    /// is not intended for in-game interfaces — its supported uses are debug overlays and
    /// editor tooling. Info severity and disabled by default: development-time overlays are
    /// legitimate and common.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0023OnGuiDeclarationAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0023";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Info,
            isEnabledByDefault: false);

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

            var monoBehaviourType = ctx.Type("UnityEngine.MonoBehaviour");
            if (monoBehaviourType is null)
            {
                return;
            }

            ctx.RegisterSymbolAction(
                symbolCtx => AnalyzeMethod(symbolCtx, monoBehaviourType),
                SymbolKind.Method);
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context, INamedTypeSymbol monoBehaviourType)
        {
            var method = (IMethodSymbol)context.Symbol;

            if (method.MethodKind != MethodKind.Ordinary ||
                method.Name != "OnGUI" ||
                method.IsStatic ||
                !method.ReturnsVoid ||
                !method.Parameters.IsEmpty)
            {
                return;
            }

            if (!TypeHierarchy.DerivesFrom(method.ContainingType, monoBehaviourType))
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
