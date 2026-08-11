using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0013: Reports calls to extension methods declared on System.Linq.Enumerable or
    /// System.Linq.Queryable on per-frame hot paths, at the call site — query syntax
    /// compiles into the same methods and triggers too. Same-named extensions on user types
    /// are excluded by declaring-type comparison. An opinionated, off-by-default rule for
    /// low-allocation codebases (docs/rules/UPA0013.md).
    /// </summary>
    [HotPathRule]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0013LinqUsageAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0013";

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
            var enumerableType = ctx.Type("System.Linq.Enumerable");
            var queryableType = ctx.Type("System.Linq.Queryable");
            if (enumerableType is null && queryableType is null)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, enumerableType, queryableType, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? enumerableType,
            INamedTypeSymbol? queryableType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            var containingType = method.ContainingType;

            var isLinq =
                (enumerableType is object && SymbolEqualityComparer.Default.Equals(containingType, enumerableType)) ||
                (queryableType is object && SymbolEqualityComparer.Default.Equals(containingType, queryableType));
            if (!isLinq)
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(invocation, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                OperationFacts.MemberNameLocation(invocation.Syntax),
                $"{containingType.Name}.{method.Name}"));
        }
    }
}
