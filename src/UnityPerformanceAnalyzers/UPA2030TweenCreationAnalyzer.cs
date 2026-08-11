using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2030: Reports tween-creating calls (any method whose return type derives from
    /// <c>DG.Tweening.Tween</c>) on per-frame hot paths. Creating a tween every frame allocates
    /// and churns DOTween's active list; the tween should be created once and reused via
    /// <c>SetAutoKill(false)</c> with <c>Restart</c>/<c>ChangeEndValue</c>. Configuration calls
    /// whose receiver is already a Tween (<c>SetLoops</c>, <c>SetEase</c>, ...) are not
    /// reported — a fluent chain reports once, at its creation root. Registered only when the
    /// compilation references the DOTween assembly.
    /// </summary>
    [HotPathRule]
    [ConditionalRule("DOTween")]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2030TweenCreationAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2030";

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

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, tweenType, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol tweenType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!TypeHierarchy.DerivesFrom(method.ReturnType, tweenType))
            {
                return;
            }

            if (IsTweenConfigurationCall(invocation, method, tweenType))
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(invocation, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                method.Name));
        }

        /// <summary>
        /// True when the call configures an existing tween instead of creating one: the
        /// receiver (instance, or first argument of an extension method) is itself a Tween.
        /// A fluent chain reports once, at its creation root.
        /// </summary>
        private static bool IsTweenConfigurationCall(
            IInvocationOperation invocation,
            IMethodSymbol method,
            INamedTypeSymbol tweenType)
        {
            if (invocation.Instance is object)
            {
                return TypeHierarchy.DerivesFrom(invocation.Instance.Type, tweenType);
            }

            if (method.IsExtensionMethod && invocation.Arguments.Length > 0)
            {
                return TypeHierarchy.DerivesFrom(invocation.Arguments[0].Value.Type, tweenType);
            }

            return false;
        }

    }
}
