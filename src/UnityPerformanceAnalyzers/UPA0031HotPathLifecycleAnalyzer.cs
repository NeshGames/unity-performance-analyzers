using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0031: <c>Instantiate</c> or <c>Destroy</c> on a per-frame path. Two halves of one
    /// problem — creating and discarding objects every frame — and one answer, a pool, so they
    /// share an id and differ only in wording. Splitting them would let a project silence half
    /// and keep the garbage.
    /// </summary>
    /// <remarks>
    /// Matching is on the method symbol's original definition, never on member access syntax.
    /// Inside a MonoBehaviour the overwhelming way this is written is <c>Instantiate(prefab)</c>
    /// with no receiver at all — it is inherited from <c>UnityEngine.Object</c> — so a
    /// syntax-shaped implementation would miss almost every real occurrence, and miss it
    /// silently.
    /// </remarks>
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [HotPathRule]
    public sealed class UPA0031HotPathLifecycleAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0031";

        private const string InstantiateName = "Instantiate";
        private const string DestroyName = "Destroy";

        // Info, not Warning. On three real games the rule produced five findings and not one
        // of them was a per-frame create or destroy -- every one sat behind a one-shot guard,
        // because an unguarded Destroy in an Update would delete its object on the first frame
        // and so cannot survive in working code. It stays enabled because the shape it looks
        // for is real (an Instantiate under a held fire button), but the evidence does not
        // carry a warning. The presets grade it to match; a descriptor left alone here while
        // the presets said warning would have changed nothing for anyone who uses one.
        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DestroyRule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            messageFormatKey: Strings.UPA0031MessageFormatDestroy);

        // One descriptor per message format, one entry here. Roslyn matches a reported
        // diagnostic against the declared set by ID rather than by descriptor identity.
        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var objectType = ctx.Type("UnityEngine.Object");
            if (objectType is null)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;
            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, objectType, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol objectType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            var descriptor = method.Name switch
            {
                InstantiateName => Rule,
                DestroyName => DestroyRule,
                _ => null,
            };

            if (descriptor is null)
            {
                return;
            }

            // OriginalDefinition, so the generic Instantiate<T> resolves to the same declaring
            // type as the non-generic overloads instead of falling through.
            if (!SymbolEqualityComparer.Default.Equals(method.OriginalDefinition.ContainingType, objectType))
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(invocation, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                descriptor,
                // The whole call, not just the name: there is no chaining here to make an
                // outer span misleading, and a receiverless Instantiate(prefab) has no member
                // access to point at anyway -- pointing at the name would give this one rule
                // two different spans depending on how the call happens to be written.
                invocation.Syntax.GetLocation(),
                SubjectName(invocation, method)));
        }

        /// <summary>
        /// What the message names. The argument's type when it is known, and the method
        /// otherwise — <c>Destroy(null)</c> and a <c>var</c> of an unresolved type both reach
        /// here, and neither is a reason to stay quiet.
        /// </summary>
        private static string SubjectName(IInvocationOperation invocation, IMethodSymbol method)
        {
            if (invocation.Arguments.Length > 0)
            {
                var argument = OperationFacts.Unwrap(invocation.Arguments[0].Value);
                if (argument.Type is { } type && type.TypeKind != TypeKind.Error)
                {
                    return type.Name;
                }
            }

            return method.Name;
        }
    }
}
