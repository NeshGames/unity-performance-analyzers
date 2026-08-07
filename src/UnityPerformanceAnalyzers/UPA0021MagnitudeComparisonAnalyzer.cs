using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0021: Reports relational comparisons where one side is a <c>Vector2/3/4.magnitude</c>
    /// access or <c>Vector2/3/4.Distance(...)</c> call and the other side is not. Both compute a
    /// square root; comparing <c>sqrMagnitude</c> against the squared threshold is equivalent
    /// and much faster. Comparisons with a magnitude on both sides are not reported — the
    /// rewrite brings no gain there.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0021MagnitudeComparisonAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0021";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0021Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0021MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0021Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0021.md");

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
                var vectorTypes = ImmutableArray.CreateRange(
                    new[]
                    {
                        ctx.Compilation.GetTypeByMetadataName("UnityEngine.Vector2"),
                        ctx.Compilation.GetTypeByMetadataName("UnityEngine.Vector3"),
                        ctx.Compilation.GetTypeByMetadataName("UnityEngine.Vector4"),
                    });
                if (vectorTypes[0] is null && vectorTypes[1] is null && vectorTypes[2] is null)
                {
                    return;
                }

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeComparison(opCtx, vectorTypes),
                    OperationKind.BinaryOperator);
            });
        }

        private static void AnalyzeComparison(
            OperationAnalysisContext context,
            ImmutableArray<INamedTypeSymbol?> vectorTypes)
        {
            var binary = (IBinaryOperation)context.Operation;
            switch (binary.OperatorKind)
            {
                case BinaryOperatorKind.LessThan:
                case BinaryOperatorKind.LessThanOrEqual:
                case BinaryOperatorKind.GreaterThan:
                case BinaryOperatorKind.GreaterThanOrEqual:
                    break;
                default:
                    return;
            }

            var leftRoot = FindRootExpression(binary.LeftOperand, vectorTypes);
            var rightRoot = FindRootExpression(binary.RightOperand, vectorTypes);

            // Both sides take a square root: the rewrite preserves the comparison but
            // saves nothing, so stay quiet.
            if ((leftRoot is null) == (rightRoot is null))
            {
                return;
            }

            var rootSyntax = (leftRoot ?? rightRoot)!.Syntax;
            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                binary.Syntax.GetLocation(),
                rootSyntax.ToString()));
        }

        /// <summary>
        /// Returns the magnitude/Distance operation when <paramref name="operand"/> is (after
        /// implicit conversions) a Vector2/3/4 <c>magnitude</c> access or <c>Distance</c> call.
        /// </summary>
        private static IOperation? FindRootExpression(
            IOperation operand,
            ImmutableArray<INamedTypeSymbol?> vectorTypes)
        {
            var current = operand;
            while (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
            }

            if (current is IPropertyReferenceOperation propertyReference &&
                propertyReference.Property.Name == "magnitude" &&
                IsVectorType(propertyReference.Property.ContainingType, vectorTypes))
            {
                return current;
            }

            if (current is IInvocationOperation invocation &&
                invocation.TargetMethod.Name == "Distance" &&
                invocation.TargetMethod.IsStatic &&
                IsVectorType(invocation.TargetMethod.ContainingType, vectorTypes))
            {
                return current;
            }

            return null;
        }

        private static bool IsVectorType(INamedTypeSymbol? type, ImmutableArray<INamedTypeSymbol?> vectorTypes)
        {
            foreach (var vectorType in vectorTypes)
            {
                if (vectorType is object && SymbolEqualityComparer.Default.Equals(type, vectorType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
