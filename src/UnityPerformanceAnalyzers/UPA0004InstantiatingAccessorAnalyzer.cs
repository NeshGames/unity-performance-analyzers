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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0004InstantiatingAccessorAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0004";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0004Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0004MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0004Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0004.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var rendererType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Renderer");
                var meshFilterType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.MeshFilter");
                if (rendererType is null && meshFilterType is null)
                {
                    return;
                }

                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzePropertyReference(opCtx, rendererType, meshFilterType, hotPathDetector),
                    OperationKind.PropertyReference);
            });
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

            if (IsAssignmentTarget(propertyReference))
            {
                return;
            }

            var semanticModel = propertyReference.SemanticModel;
            if (semanticModel is null ||
                !hotPathDetector.IsInHotPath(propertyReference.Syntax, semanticModel, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                propertyReference.Syntax.GetLocation(),
                property.Name));
        }

        private static bool IsAssignmentTarget(IPropertyReferenceOperation propertyReference)
        {
            return propertyReference.Parent is ISimpleAssignmentOperation assignment &&
                ReferenceEquals(assignment.Target, propertyReference);
        }
    }
}
