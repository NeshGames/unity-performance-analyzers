using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0024: Reports <c>Resources.Load</c>/<c>LoadAll</c>/<c>LoadAsync</c> calls on per-frame
    /// hot paths. Every call performs an asset lookup and load; the asset should be loaded once
    /// outside the hot path and the reference cached. Disabled by default — the general guidance
    /// against the Resources system is broader than the per-frame case this rule enforces.
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0024ResourcesLoadAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0024";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_loadMethodNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Load",
            "LoadAll",
            "LoadAsync");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var resourcesType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Resources");
            if (resourcesType is null)
            {
                return;
            }

            var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, resourcesType, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol resourcesType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!s_loadMethodNames.Contains(method.Name) ||
                !SymbolEqualityComparer.Default.Equals(method.ContainingType, resourcesType))
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
                "Resources." + method.Name));
        }
    }
}
