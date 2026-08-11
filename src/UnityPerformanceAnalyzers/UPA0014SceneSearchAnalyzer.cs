using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0014: Reports calls to the scene-search APIs (<c>GameObject.Find</c> family and the
    /// <c>Object.Find*ObjectOfType/ByType</c> family) on per-frame hot paths. Each call walks
    /// the scene or all loaded objects; the reference should be resolved once in
    /// <c>Awake</c>/<c>Start</c> and cached in a field.
    /// </summary>
    [HotPathRule]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0014SceneSearchAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0014";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_gameObjectSearchNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Find",
            "FindWithTag",
            "FindGameObjectWithTag",
            "FindGameObjectsWithTag");

        private static readonly ImmutableHashSet<string> s_objectSearchNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "FindObjectOfType",
            "FindObjectsOfType",
            "FindFirstObjectByType",
            "FindAnyObjectByType",
            "FindObjectsByType");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var gameObjectType = ctx.Type("UnityEngine.GameObject");
            var objectType = ctx.Type("UnityEngine.Object");
            if (gameObjectType is null && objectType is null)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, gameObjectType, objectType, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? gameObjectType,
            INamedTypeSymbol? objectType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;
            var containingType = method.ContainingType;

            var isSearch =
                (s_gameObjectSearchNames.Contains(method.Name) &&
                 SymbolEqualityComparer.Default.Equals(containingType, gameObjectType)) ||
                (s_objectSearchNames.Contains(method.Name) &&
                 SymbolEqualityComparer.Default.Equals(containingType, objectType));
            if (!isSearch)
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
                containingType.Name + "." + method.Name));
        }
    }
}
