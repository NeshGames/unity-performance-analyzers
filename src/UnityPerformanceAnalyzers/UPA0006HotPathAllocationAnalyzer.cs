using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0006: Reports managed heap allocations on per-frame hot paths — object creations of
    /// reference types, array creations, boxing conversions, and value-type interpolation
    /// holes (which box when the interpolation lowers to string.Format but never produce a
    /// conversion in the operation tree). Delegate creations are left to UPA0007 (capturing
    /// lambdas), string allocations to UPA2000, and allocations on throw paths are
    /// deliberately ignored (docs/rules/UPA0006.md).
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0006HotPathAllocationAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0006";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly SymbolDisplayFormat s_typeDisplayFormat =
            SymbolDisplayFormat.MinimallyQualifiedFormat;

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeOperation(opCtx, hotPathDetector),
                OperationKind.ObjectCreation,
                OperationKind.ArrayCreation,
                OperationKind.Conversion,
                OperationKind.Interpolation);
        }

        private static void AnalyzeOperation(OperationAnalysisContext context, HotPathDetector hotPathDetector)
        {
            var operation = context.Operation;

            string? allocationDescription;
            switch (operation)
            {
                case IObjectCreationOperation objectCreation:
                    allocationDescription = DescribeObjectCreation(objectCreation);
                    break;
                case IArrayCreationOperation arrayCreation:
                    // Implicit array creations are params expansions — excluded in v0.1.
                    allocationDescription = arrayCreation.IsImplicit
                        ? null
                        : arrayCreation.Type?.ToDisplayString(s_typeDisplayFormat);
                    break;
                case IConversionOperation conversion:
                    allocationDescription = DescribeBoxing(conversion);
                    break;
                case IInterpolationOperation interpolation:
                    allocationDescription = DescribeInterpolationHoleBoxing(interpolation);
                    break;
                default:
                    return;
            }

            if (allocationDescription is null)
            {
                return;
            }

            if (IsOnThrowPath(operation))
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(operation, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                operation.Syntax.GetLocation(),
                allocationDescription));
        }

        private static string? DescribeObjectCreation(IObjectCreationOperation objectCreation)
        {
            var type = objectCreation.Type;
            if (type is null || !type.IsReferenceType)
            {
                return null;
            }

            // Delegate allocations are UPA0007's territory (capturing lambdas) or deliberately
            // out of scope in v0.1 (method groups); strings never come through object creation.
            if (type.TypeKind == TypeKind.Delegate)
            {
                return null;
            }

            return type.ToDisplayString(s_typeDisplayFormat);
        }

        private static string? DescribeBoxing(IConversionOperation conversion)
        {
            if (!conversion.GetConversion().IsBoxing)
            {
                return null;
            }

            var operandType = conversion.Operand.Type;
            return operandType is null
                ? "boxed value"
                : $"boxed {operandType.ToDisplayString(s_typeDisplayFormat)}";
        }

        // A value-type interpolation hole boxes when the interpolation lowers to
        // string.Format(string, object), but the operation tree keeps the hole at its original
        // type with no conversion node — so the boxing is invisible to the Conversion action
        // and must be reported here. The string allocation itself stays UPA2000's territory.
        private static string? DescribeInterpolationHoleBoxing(IInterpolationOperation interpolation)
        {
            var expression = interpolation.Expression;

            // An explicit boxing conversion inside the hole (e.g. $"{(object)x}") is already
            // reported by the Conversion action — do not report it twice.
            if (expression is IConversionOperation)
            {
                return null;
            }

            var type = expression.Type;
            if (type is null || !type.IsValueType || type is ITypeParameterSymbol)
            {
                return null;
            }

            return $"boxed {type.ToDisplayString(s_typeDisplayFormat)}";
        }

        // `throw new Exception(...)` and allocations feeding directly into a throw are excluded:
        // exceptional paths are expected to be rare, and per-frame throwing is a different bug.
        private static bool IsOnThrowPath(IOperation operation)
        {
            for (var current = operation.Parent; current is object; current = current.Parent)
            {
                if (current is IThrowOperation)
                {
                    return true;
                }

                if (!(current is IConversionOperation) && !(current is IObjectCreationOperation) &&
                    !(current is IArgumentOperation))
                {
                    return false;
                }
            }

            return false;
        }
    }
}
