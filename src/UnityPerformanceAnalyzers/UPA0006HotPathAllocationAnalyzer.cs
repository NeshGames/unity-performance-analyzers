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
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var hotPathDetector = ctx.HotPath;

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
                    // Implicit array creations are params expansions — UPA0027's business.
                    allocationDescription = arrayCreation.IsImplicit
                        ? null
                        : arrayCreation.Type?.ToDisplayString(s_typeDisplayFormat);
                    break;
                case IConversionOperation conversion:
                    // The boxing inside a params expansion belongs to the same cost as the
                    // array around it, and UPA0027 already reports both together while naming
                    // the call. Reporting it here too would put two diagnostics on one line
                    // for one allocation.
                    allocationDescription = IsInsideParamsExpansion(conversion) ||
                            IsElidedHasFlagArgument(conversion)
                        ? null
                        : DescribeBoxing(conversion);
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

        /// <summary>
        /// True when this conversion is an element of the array the compiler synthesized for
        /// an expanded-form params call. The element sits inside the array initializer, so the
        /// initializer's parent is the implicit creation.
        /// </summary>
        private static bool IsInsideParamsExpansion(IConversionOperation conversion) =>
            conversion.Parent is IArrayInitializerOperation initializer &&
            initializer.Parent is IArrayCreationOperation arrayCreation &&
            arrayCreation.IsImplicit;

        /// <summary>
        /// True when this conversion is the argument box of an <c>Enum.HasFlag</c> call that
        /// the runtime removes along with the call itself.
        /// </summary>
        /// <remarks>
        /// <c>HasFlag</c> takes a <c>System.Enum</c>, so IL boxes the argument at every call
        /// site; Mono since 4.0 and IL2CPP since Unity 2021.2 replace the same-type call with a
        /// bitwise AND and the box goes with it. Measured on IL2CPP against a control that
        /// allocates in the same loop — so the zeros are not a hoisted box — the elision holds
        /// for a constant, a local, a parameter, a field and a property argument on both
        /// supported editors, and fails for a conditional written inline at the call site,
        /// which allocates on both.
        ///
        /// The line is drawn exactly where the measurement is. Shapes nobody has measured, an
        /// argument that is itself a call among them, keep being reported: a report that is
        /// wrong can be removed by a later measurement, and a silence that is wrong cannot be
        /// noticed at all.
        /// </remarks>
        private static bool IsElidedHasFlagArgument(IConversionOperation conversion)
        {
            if (!(conversion.Parent is IArgumentOperation argument) ||
                !(argument.Parent is IInvocationOperation invocation))
            {
                return false;
            }

            var method = invocation.TargetMethod;
            if (method.Name != "HasFlag" ||
                method.ContainingType?.SpecialType != SpecialType.System_Enum)
            {
                return false;
            }

            return IsSimpleLoad(conversion.Operand);
        }

        /// <summary>
        /// True for the operand shapes the measurement covered: the argument is loaded and
        /// nothing branches between the load and the call.
        /// </summary>
        private static bool IsSimpleLoad(IOperation operand)
        {
            if (operand.ConstantValue.HasValue)
            {
                return true;
            }

            switch (operand)
            {
                case ILocalReferenceOperation _:
                case IParameterReferenceOperation _:
                case IFieldReferenceOperation _:
                case IPropertyReferenceOperation _:
                case IInstanceReferenceOperation _:
                    return true;
                default:
                    return false;
            }
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
