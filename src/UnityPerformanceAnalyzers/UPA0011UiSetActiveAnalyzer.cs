using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0011: Reports <c>uiComponent.gameObject.SetActive(...)</c> where the receiver is
    /// statically typed as a <c>UnityEngine.UI.Graphic</c> or <c>TMPro.TMP_Text</c> derivative.
    /// The heuristic is deliberately narrow: toggling a whole panel via SetActive is often
    /// intentional and cannot be judged statically, so plain GameObject receivers are never
    /// reported. Each UI branch registers only when its type exists in the compilation.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0011UiSetActiveAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0011";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var gameObjectType = ctx.Type("UnityEngine.GameObject");
            var graphicType = ctx.Type("UnityEngine.UI.Graphic");
            var tmpTextType = ctx.Type("TMPro.TMP_Text");
            if (gameObjectType is null || (graphicType is null && tmpTextType is null))
            {
                return;
            }

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, gameObjectType, graphicType, tmpTextType),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol gameObjectType,
            INamedTypeSymbol? graphicType,
            INamedTypeSymbol? tmpTextType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (method.Name != "SetActive" ||
                !SymbolEqualityComparer.Default.Equals(method.ContainingType, gameObjectType))
            {
                return;
            }

            // Match the exact shape <uiComponent>.gameObject.SetActive(...): the receiver must
            // be the gameObject accessor whose own receiver is statically a UI component.
            if (!(invocation.Instance is IPropertyReferenceOperation gameObjectAccess) ||
                gameObjectAccess.Property.Name != "gameObject")
            {
                return;
            }

            var uiComponentType = gameObjectAccess.Instance?.Type;
            if (uiComponentType is null ||
                (!TypeHierarchy.DerivesFrom(uiComponentType, graphicType) && !TypeHierarchy.DerivesFrom(uiComponentType, tmpTextType)))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                uiComponentType.Name));
        }

    }
}
