using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0001: Reports <c>GetComponent</c>-family lookups (including <c>TryGetComponent</c> and
    /// the non-generic overloads) on <c>UnityEngine.Component</c> or <c>UnityEngine.GameObject</c>
    /// when they run on a per-frame hot path. The result should be resolved once in
    /// <c>Awake</c>/<c>Start</c> and cached in a field. The array-returning
    /// <c>GetComponents*</c> overloads are UPA0017's; the <c>List&lt;T&gt;</c> overloads stay here.
    /// </summary>
    [HotPathRule]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0001ComponentLookupAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0001";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_lookupMethodNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "GetComponent",
            "GetComponents",
            "GetComponentInChildren",
            "GetComponentsInChildren",
            "GetComponentInParent",
            "GetComponentsInParent",
            "TryGetComponent");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var componentType = ctx.Type("UnityEngine.Component");
            var gameObjectType = ctx.Type("UnityEngine.GameObject");
            if (componentType is null && gameObjectType is null)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, componentType, gameObjectType, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? componentType,
            INamedTypeSymbol? gameObjectType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!s_lookupMethodNames.Contains(method.Name))
            {
                return;
            }

            // An array-returning GetComponents* call pays for the lookup too, but what it
            // costs every frame is the fresh array, and UPA0017 says so with the fix that
            // removes it. Reporting it here as well gave one line two warnings with two
            // different remedies. The List<T> overloads allocate nothing, so the native
            // lookup is all that is left to say about them, and that stays this rule's.
            if (UPA0017GetComponentsArrayAnalyzer.IsArrayReturningPluralLookup(method))
            {
                return;
            }

            var containingType = method.ContainingType;
            if (!SymbolEqualityComparer.Default.Equals(containingType, componentType) &&
                !SymbolEqualityComparer.Default.Equals(containingType, gameObjectType))
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
    }
}
