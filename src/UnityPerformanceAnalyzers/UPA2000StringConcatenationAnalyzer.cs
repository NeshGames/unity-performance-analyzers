using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2000: Reports string building (concatenation, += append, interpolation) on per-frame
    /// hot paths. Registered unconditionally — a referenced ZString assembly only
    /// switches the advice in the message, it never gates the report. Boxing of value-type
    /// operands inside a concatenation is UPA0006's report and deliberately coexists
    /// (docs/rules/UPA2000.md).
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2000StringConcatenationAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2000";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly LocalizableResourceString s_adviceDefault =
            new LocalizableResourceString(Strings.UPA2000AdviceDefault, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceZString =
            new LocalizableResourceString(Strings.UPA2000AdviceZString, Strings.ResourceManager, typeof(Strings));

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var hotPathDetector = ctx.HotPath;
            var profile = ctx.Profile;
            var advice = profile.HasZString ? s_adviceZString : s_adviceDefault;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeOperation(opCtx, hotPathDetector, advice),
                OperationKind.Binary,
                OperationKind.CompoundAssignment,
                OperationKind.InterpolatedString);
        }

        private static void AnalyzeOperation(
            OperationAnalysisContext context,
            HotPathDetector hotPathDetector,
            LocalizableString advice)
        {
            var operation = context.Operation;

            // Compile-time constant folding ("a" + "b", const combinations) allocates nothing.
            if (operation.ConstantValue.HasValue)
            {
                return;
            }

            if (!IsStringBuild(operation))
            {
                return;
            }

            // Report only the outermost build of a chain: "a" + b + c is one allocation site
            // to the reader, and s += "x" + y already reports at the compound assignment.
            if (IsNestedInStringBuild(operation))
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
                advice));
        }

        private static bool IsStringBuild(IOperation operation)
        {
            switch (operation)
            {
                case IBinaryOperation binary:
                    return binary.OperatorKind == BinaryOperatorKind.Add && IsString(binary.Type);
                case ICompoundAssignmentOperation compound:
                    return compound.OperatorKind == BinaryOperatorKind.Add && IsString(compound.Type);
                case IInterpolatedStringOperation interpolated:
                    return IsString(interpolated.Type);
                default:
                    return false;
            }
        }

        private static bool IsNestedInStringBuild(IOperation operation)
        {
            var current = operation.Parent;
            while (current is IParenthesizedOperation || current is IConversionOperation)
            {
                current = current.Parent;
            }

            return current is object && IsStringBuild(current);
        }

        private static bool IsString(ITypeSymbol? type)
        {
            return type?.SpecialType == SpecialType.System_String;
        }
    }
}
