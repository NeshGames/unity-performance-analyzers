using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0010: Reports <c>Physics.Raycast</c> / <c>RaycastAll</c> / <c>RaycastNonAlloc</c>
    /// calls whose chosen overload lacks a <c>maxDistance</c> or <c>layerMask</c> parameter
    /// (matched by parameter name; Ray-based overloads included). Unbounded raycasts scan the
    /// whole scene across all layers — a performance and correctness hazard.
    /// </summary>
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0010UnboundedRaycastAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0010";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_raycastMethodNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Raycast",
            "RaycastAll",
            "RaycastNonAlloc");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var physicsType = ctx.Type("UnityEngine.Physics");
            if (physicsType is null)
            {
                return;
            }

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, physicsType),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol physicsType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!s_raycastMethodNames.Contains(method.Name) ||
                !SymbolEqualityComparer.Default.Equals(method.ContainingType, physicsType))
            {
                return;
            }

            var hasMaxDistance = false;
            var hasLayerMask = false;
            foreach (var parameter in method.Parameters)
            {
                if (parameter.Name == "maxDistance")
                {
                    hasMaxDistance = true;
                }
                else if (parameter.Name == "layerMask")
                {
                    hasLayerMask = true;
                }
            }

            if (hasMaxDistance && hasLayerMask)
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                $"{physicsType.Name}.{method.Name}"));
        }
    }
}
