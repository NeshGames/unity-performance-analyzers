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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2000StringConcatenationAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2000";

        // RS1032 objects to a format string ending in a placeholder; here the final
        // sentence (the advice, itself period-terminated) arrives as a localized argument.
#pragma warning disable RS1032
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2000Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2000MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2000Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2000.md");
#pragma warning restore RS1032

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly LocalizableResourceString s_adviceDefault =
            new LocalizableResourceString(Strings.UPA2000AdviceDefault, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceZString =
            new LocalizableResourceString(Strings.UPA2000AdviceZString, Strings.ResourceManager, typeof(Strings));

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
                var profile = UpaProfile.Resolve(ctx.Compilation, ctx.Options);
                var advice = profile.HasZString ? s_adviceZString : s_adviceDefault;

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeOperation(opCtx, hotPathDetector, advice),
                    OperationKind.Binary,
                    OperationKind.CompoundAssignment,
                    OperationKind.InterpolatedString);
            });
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

            var semanticModel = operation.SemanticModel;
            if (semanticModel is null ||
                !hotPathDetector.IsInHotPath(operation.Syntax, semanticModel, context.CancellationToken))
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
