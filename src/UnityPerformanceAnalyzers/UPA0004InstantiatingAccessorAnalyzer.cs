using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0004: Reports reads of <c>Renderer.material</c>, <c>Renderer.materials</c>, and
    /// <c>MeshFilter.mesh</c> on per-frame hot paths. These accessors return an instantiated
    /// copy the caller must destroy manually, and each access is a native property call
    /// (<c>materials</c> additionally allocates a new array per access). The <c>shared*</c>
    /// accessors are not reported.
    /// </summary>
    [HotPathRule]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0004InstantiatingAccessorAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0004";

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
            var rendererType = ctx.Type("UnityEngine.Renderer");
            var meshFilterType = ctx.Type("UnityEngine.MeshFilter");
            if (rendererType is null && meshFilterType is null)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzePropertyReference(opCtx, rendererType, meshFilterType, hotPathDetector),
                OperationKind.PropertyReference);
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            INamedTypeSymbol? rendererType,
            INamedTypeSymbol? meshFilterType,
            HotPathDetector hotPathDetector)
        {
            var propertyReference = (IPropertyReferenceOperation)context.Operation;
            var property = propertyReference.Property;
            var containingType = property.ContainingType;

            // Derived types (e.g. SkinnedMeshRenderer) resolve these inherited properties to
            // their declaring type, so comparing the declaring type covers the whole hierarchy.
            var isRendererAccessor =
                (property.Name == "material" || property.Name == "materials") &&
                SymbolEqualityComparer.Default.Equals(containingType, rendererType);
            var isMeshFilterAccessor =
                property.Name == "mesh" &&
                SymbolEqualityComparer.Default.Equals(containingType, meshFilterType);

            if (!isRendererAccessor && !isMeshFilterAccessor)
            {
                return;
            }

            if (OperationFacts.IsOverwritten(propertyReference))
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(propertyReference, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                propertyReference.Syntax.GetLocation(),
                property.Name));
        }

    }
}
