using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0012: Reports assignments to <c>TMPro.TMP_Text.text</c> (including derived types such
    /// as <c>TextMeshProUGUI</c>) on per-frame hot paths. Each assignment stores a new string
    /// and dirties the text; <c>SetText</c> with format arguments writes into the internal
    /// buffer without the intermediate string. The rule registers only when TMP_Text exists in
    /// the compilation — projects without TextMeshPro pay nothing.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0012TmpTextAssignmentAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0012";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0012Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0012MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA0012Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0012.md");

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
                var tmpTextType = ctx.Compilation.GetTypeByMetadataName("TMPro.TMP_Text");
                if (tmpTextType is null)
                {
                    return;
                }

                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzePropertyReference(opCtx, tmpTextType, hotPathDetector),
                    OperationKind.PropertyReference);
            });
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            INamedTypeSymbol tmpTextType,
            HotPathDetector hotPathDetector)
        {
            var propertyReference = (IPropertyReferenceOperation)context.Operation;
            var property = propertyReference.Property;

            if (property.Name != "text" ||
                !SymbolEqualityComparer.Default.Equals(property.ContainingType, tmpTextType))
            {
                return;
            }

            // Only setter usage is reported — reads do not dirty the text.
            if (!IsAssignmentTarget(propertyReference))
            {
                return;
            }

            var semanticModel = propertyReference.SemanticModel;
            if (semanticModel is null ||
                !hotPathDetector.IsInHotPath(propertyReference.Syntax, semanticModel, context.CancellationToken))
            {
                return;
            }

            var receiverTypeName = propertyReference.Instance?.Type?.Name ?? tmpTextType.Name;
            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                propertyReference.Syntax.GetLocation(),
                receiverTypeName));
        }

        private static bool IsAssignmentTarget(IPropertyReferenceOperation propertyReference)
        {
            return propertyReference.Parent is IAssignmentOperation assignment &&
                ReferenceEquals(assignment.Target, propertyReference);
        }
    }
}
