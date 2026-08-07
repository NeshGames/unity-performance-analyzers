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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2030TweenCreationAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2030";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2030Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2030MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2030Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2030.md");

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

                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, tweenType, hotPathDetector),
                    OperationKind.Invocation);
            });
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol tweenType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!DerivesFromTween(method.ReturnType, tweenType))
            {
                return;
            }

            if (IsTweenConfigurationCall(invocation, method, tweenType))
            {
                return;
            }

            var semanticModel = invocation.SemanticModel;
            if (semanticModel is null ||
                !hotPathDetector.IsInHotPath(invocation.Syntax, semanticModel, context.CancellationToken))
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
                return DerivesFromTween(invocation.Instance.Type, tweenType);
            }

            if (method.IsExtensionMethod && invocation.Arguments.Length > 0)
            {
                return DerivesFromTween(invocation.Arguments[0].Value.Type, tweenType);
            }

            return false;
        }

        private static bool DerivesFromTween(ITypeSymbol? type, INamedTypeSymbol tweenType)
        {
            for (var current = type; current is object; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, tweenType))
                {
                    return true;
                }

                // Generic tween cores constrain T : Tween; a type parameter return
                // (e.g. SetLoops<T>) resolves through its constraints.
                if (current is ITypeParameterSymbol typeParameter)
                {
                    foreach (var constraint in typeParameter.ConstraintTypes)
                    {
                        if (DerivesFromTween(constraint, tweenType))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            return false;
        }
    }
}
