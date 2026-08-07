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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2031DiscardedInfiniteTweenAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2031";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2031Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2031MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2031Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2031.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var profile = UpaProfile.Resolve(ctx.Compilation, ctx.Options);
                if (!profile.HasDOTween)
                {
                    return;
                }

                var tweenType = ctx.Compilation.GetTypeByMetadataName("DG.Tweening.Tween");
                if (tweenType is null)
                {
                    return;
                }

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, tweenType),
                    OperationKind.Invocation);
            });
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol tweenType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (method.Name != "SetLoops" || !ReturnsTween(method.ReturnType, tweenType))
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

        private static bool ReturnsTween(ITypeSymbol? type, INamedTypeSymbol tweenType)
        {
            if (type is ITypeParameterSymbol typeParameter)
            {
                foreach (var constraint in typeParameter.ConstraintTypes)
                {
                    if (ReturnsTween(constraint, tweenType))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (var current = type; current is object; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, tweenType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
