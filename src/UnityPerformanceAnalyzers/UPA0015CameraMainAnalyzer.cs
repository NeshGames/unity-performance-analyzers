using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0015: Reports <c>Camera.main</c> accesses on per-frame hot paths. Each access performs
    /// a lookup comparable to <c>GetComponent</c>. Unity 2020.2+ caches the result internally,
    /// so the residual cost is small — the rule reports at Info level.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0015CameraMainAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0015";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0015Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0015MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0015Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0015.md");

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
                var cameraType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Camera");
                if (cameraType is null)
                {
                    return;
                }

                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzePropertyReference(opCtx, cameraType, hotPathDetector),
                    OperationKind.PropertyReference);
            });
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            INamedTypeSymbol cameraType,
            HotPathDetector hotPathDetector)
        {
            var propertyReference = (IPropertyReferenceOperation)context.Operation;
            var property = propertyReference.Property;

            if (property.Name != "main" ||
                !SymbolEqualityComparer.Default.Equals(property.ContainingType, cameraType))
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
                propertyReference.Syntax.GetLocation()));
        }
    }
}
