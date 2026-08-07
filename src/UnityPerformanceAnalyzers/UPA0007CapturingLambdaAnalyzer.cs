using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0007: Reports lambdas and anonymous methods on per-frame hot paths whose capture set
    /// is non-empty (locals, parameters, or <c>this</c>). Capturing functions allocate a closure
    /// and a fresh delegate every time the enclosing code runs; capture-free lambdas are cached
    /// by the compiler and are not reported.
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0007CapturingLambdaAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0007";

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
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeAnonymousFunction(opCtx, hotPathDetector),
                OperationKind.AnonymousFunction);
        }

        private static void AnalyzeAnonymousFunction(OperationAnalysisContext context, HotPathDetector hotPathDetector)
        {
            var lambda = (IAnonymousFunctionOperation)context.Operation;

            if (!CapturesState(lambda))
            {
                return;
            }

            // The allocation happens where the lambda expression is evaluated, so the lambda
            // node itself must sit on a hot path (its own body being "inside a lambda" is
            // irrelevant here — upa_hot_path_include_lambdas governs nodes inside bodies).
            if (hotPathDetector.IsOutsideHotPath(lambda, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(Rule, lambda.Syntax.GetLocation()));
        }

        private static bool CapturesState(IAnonymousFunctionOperation lambda)
        {
            var lambdaSymbol = lambda.Symbol;

            foreach (var descendant in lambda.Body.Descendants())
            {
                switch (descendant)
                {
                    case IInstanceReferenceOperation instanceReference
                        when instanceReference.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance:
                        return true;

                    case ILocalReferenceOperation localReference
                        when !localReference.Local.IsConst &&
                            IsDeclaredOutside(localReference.Local, lambdaSymbol):
                        return true;

                    case IParameterReferenceOperation parameterReference
                        when IsDeclaredOutside(parameterReference.Parameter, lambdaSymbol):
                        return true;
                }
            }

            return false;
        }

        private static bool IsDeclaredOutside(ISymbol symbol, IMethodSymbol lambdaSymbol)
        {
            for (var containing = symbol.ContainingSymbol; containing is object; containing = containing.ContainingSymbol)
            {
                if (SymbolEqualityComparer.Default.Equals(containing, lambdaSymbol))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
