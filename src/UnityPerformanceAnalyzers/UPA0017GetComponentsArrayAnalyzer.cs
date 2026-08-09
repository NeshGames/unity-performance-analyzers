using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0017: Reports the array-returning <c>GetComponents</c>/<c>GetComponentsInChildren</c>/
    /// <c>GetComponentsInParent</c> overloads on per-frame hot paths. Each call allocates a fresh
    /// array; the <c>List&lt;T&gt;</c> overloads fill a caller-provided list instead. The singular
    /// <c>GetComponent</c> lookups are UPA0001's territory.
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0017GetComponentsArrayAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0017";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_pluralLookupNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "GetComponents",
            "GetComponentsInChildren",
            "GetComponentsInParent");

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

            // The List<T> overloads return void — only the array-returning shapes allocate.
            if (!s_pluralLookupNames.Contains(method.Name) ||
                !(method.ReturnType is IArrayTypeSymbol))
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
