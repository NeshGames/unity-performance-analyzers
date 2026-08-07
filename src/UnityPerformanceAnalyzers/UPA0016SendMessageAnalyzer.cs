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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0016SendMessageAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0016";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0016Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0016MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0016Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0016.md");

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
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var componentType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Component");
                var gameObjectType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.GameObject");
                if (componentType is null && gameObjectType is null)
                {
                    return;
                }

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, componentType, gameObjectType),
                    OperationKind.Invocation);
            });
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
