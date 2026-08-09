using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2031: Reports fluent chains that create an infinite tween (<c>SetLoops</c> with a
    /// negative constant loop count), discard the result (the chain is a bare expression
    /// statement), and carry no <c>SetLink</c>. Auto-kill only fires on completion, which an
    /// infinite tween never reaches, so the discarded tween can never be killed and outlives
    /// its target. Registered only when the compilation references the DOTween assembly.
    /// </summary>
    [ConditionalRule("DOTween")]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2031DiscardedInfiniteTweenAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2031";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var profile = ctx.Profile;
            if (!profile.HasDOTween)
            {
                return;
            }

            var tweenType = ctx.Type("DG.Tweening.Tween");
            if (tweenType is null)
            {
                return;
            }

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, tweenType),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol tweenType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (method.Name != "SetLoops" || !TypeHierarchy.DerivesFrom(method.ReturnType, tweenType))
            {
                return;
            }

            if (!HasNegativeConstantLoopCount(invocation))
            {
                return;
            }

            // Walk to the outer end of the fluent chain. If the chain's value is consumed
            // (assigned, passed, returned, yielded), the caller can kill the tween itself.
            var outermost = (SyntaxNode)invocation.Syntax;
            while (outermost.Parent is MemberAccessExpressionSyntax ||
                   outermost.Parent is InvocationExpressionSyntax)
            {
                outermost = outermost.Parent;
            }

            if (!(outermost.Parent is ExpressionStatementSyntax))
            {
                return;
            }

            if (ChainContainsSetLink(outermost))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation()));
        }

        private static bool HasNegativeConstantLoopCount(IInvocationOperation invocation)
        {
            foreach (var argument in invocation.Arguments)
            {
                var parameter = argument.Parameter;
                if (parameter is null || parameter.Type.SpecialType != SpecialType.System_Int32)
                {
                    continue;
                }

                var constant = argument.Value.ConstantValue;
                return constant.HasValue && constant.Value is int loops && loops < 0;
            }

            return false;
        }

        private static bool ChainContainsSetLink(SyntaxNode outermostChainNode)
        {
            foreach (var node in outermostChainNode.DescendantNodesAndSelf())
            {
                if (node is InvocationExpressionSyntax chained &&
                    chained.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.ValueText == "SetLink")
                {
                    return true;
                }
            }

            return false;
        }

    }
}
