using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0022 (deprecated): reported <c>System.Enum.HasFlag</c> calls on per-frame hot
    /// paths, on the grounds that the call boxes its operands.
    /// </summary>
    /// <remarks>
    /// It does not. Mono has special-cased the same-type call since 4.0 and IL2CPP since
    /// Unity 2021.2, both replacing it with a bitwise AND: measured at 0.00 B/op on every
    /// supported configuration, and on IL2CPP the call runs faster than the rewrite this
    /// rule's code fix used to produce. The premise came from ".NET 5+ intrinsics do not
    /// apply to Unity Mono", which is true and does not imply what it was read to imply.
    ///
    /// The one allocation on that line is the argument box — <c>HasFlag</c> takes a
    /// <c>System.Enum</c> — and UPA0006 reports it at the conversion, where it belongs.
    ///
    /// Left registered and disabled: ids are never recycled, and a project that has the
    /// rule in its ruleset keeps the behaviour it had.
    /// </remarks>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0022HasFlagAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0022";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var enumType = ctx.Compilation.GetSpecialType(SpecialType.System_Enum);
            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, enumType, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol enumType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (method.Name != "HasFlag" ||
                !SymbolEqualityComparer.Default.Equals(method.ContainingType, enumType))
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(invocation, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation()));
        }
    }
}
