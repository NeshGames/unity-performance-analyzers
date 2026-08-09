using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0002: Reports reads of <c>UnityEngine.Object.name</c>, <c>UnityEngine.GameObject.tag</c>,
    /// and <c>UnityEngine.Component.tag</c> on per-frame hot paths. Both getters call into native
    /// code and allocate a fresh string per access. String equality comparisons are deliberately
    /// not reported — UNT0002 (Microsoft.Unity.Analyzers) owns that case.
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0002NameTagAccessAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0002";

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
            var objectType = ctx.Type("UnityEngine.Object");
            var gameObjectType = ctx.Type("UnityEngine.GameObject");
            var componentType = ctx.Type("UnityEngine.Component");
            if (objectType is null && gameObjectType is null && componentType is null)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzePropertyReference(opCtx, objectType, gameObjectType, componentType, hotPathDetector),
                OperationKind.PropertyReference);
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            INamedTypeSymbol? objectType,
            INamedTypeSymbol? gameObjectType,
            INamedTypeSymbol? componentType,
            HotPathDetector hotPathDetector)
        {
            var propertyReference = (IPropertyReferenceOperation)context.Operation;
            var property = propertyReference.Property;
            var containingType = property.ContainingType;

            var isNameProperty = property.Name == "name" &&
                SymbolEqualityComparer.Default.Equals(containingType, objectType);
            var isTagProperty = property.Name == "tag" &&
                (SymbolEqualityComparer.Default.Equals(containingType, gameObjectType) ||
                 SymbolEqualityComparer.Default.Equals(containingType, componentType));

            if (!isNameProperty && !isTagProperty)
            {
                return;
            }

            if (OperationFacts.IsOverwritten(propertyReference))
            {
                return;
            }

            if (IsStringEqualityOperand(propertyReference))
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

        // A read that is an operand of == / != against a string is UNT0002's territory
        // (docs/rules/UPA0002.md — deliberate division of labor).
        private static bool IsStringEqualityOperand(IPropertyReferenceOperation propertyReference)
        {
            IOperation current = propertyReference;
            var parent = current.Parent;
            while (parent is IConversionOperation conversion)
            {
                current = conversion;
                parent = conversion.Parent;
            }

            if (parent is IBinaryOperation binary &&
                (binary.OperatorKind == BinaryOperatorKind.Equals ||
                 binary.OperatorKind == BinaryOperatorKind.NotEquals))
            {
                var other = ReferenceEquals(binary.LeftOperand, current)
                    ? binary.RightOperand
                    : binary.LeftOperand;
                other = OperationFacts.Unwrap(other);

                return other.Type?.SpecialType == SpecialType.System_String;
            }

            return false;
        }
    }
}
