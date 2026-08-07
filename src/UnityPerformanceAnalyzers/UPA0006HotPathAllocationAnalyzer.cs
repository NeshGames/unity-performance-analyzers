using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0006: Reports managed heap allocations on per-frame hot paths — object creations of
    /// reference types, array creations, and boxing conversions. Delegate creations are left to
    /// UPA0007 (capturing lambdas), strings to UPA2000, and allocations on throw paths are
    /// deliberately ignored (docs/rules/UPA0006.md).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0006HotPathAllocationAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0006";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0006Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0006MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0006Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0006.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly SymbolDisplayFormat s_typeDisplayFormat =
            SymbolDisplayFormat.MinimallyQualifiedFormat;

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeOperation(opCtx, hotPathDetector),
                    OperationKind.ObjectCreation,
                    OperationKind.ArrayCreation,
                    OperationKind.Conversion);
            });
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

            var semanticModel = operation.SemanticModel;
            if (semanticModel is null ||
                !hotPathDetector.IsInHotPath(operation.Syntax, semanticModel, context.CancellationToken))
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
