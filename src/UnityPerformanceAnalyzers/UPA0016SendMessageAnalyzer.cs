using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0016: Reports every call to <c>SendMessage</c>, <c>SendMessageUpwards</c>, and
    /// <c>BroadcastMessage</c> on <c>UnityEngine.Component</c>/<c>GameObject</c>. Reflection-based
    /// dispatch is orders of magnitude slower than a direct call and defers errors to runtime,
    /// so this rule reports everywhere, not just on hot paths.
    /// </summary>
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0016SendMessageAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0016";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_messageMethodNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "SendMessage",
            "SendMessageUpwards",
            "BroadcastMessage");

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

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, componentType, gameObjectType),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? componentType,
            INamedTypeSymbol? gameObjectType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!s_messageMethodNames.Contains(method.Name))
            {
                return;
            }

            var containingType = method.ContainingType;
            if (!SymbolEqualityComparer.Default.Equals(containingType, componentType) &&
                !SymbolEqualityComparer.Default.Equals(containingType, gameObjectType))
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
