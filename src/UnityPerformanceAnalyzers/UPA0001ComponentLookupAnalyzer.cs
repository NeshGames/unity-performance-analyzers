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
    /// <c>Awake</c>/<c>Start</c> and cached in a field.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0001ComponentLookupAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0001";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0001Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0001MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0001Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0001.md");

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

                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, componentType, gameObjectType, hotPathDetector),
                    OperationKind.Invocation);
            });
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

            var containingType = method.ContainingType;
            if (!SymbolEqualityComparer.Default.Equals(containingType, componentType) &&
                !SymbolEqualityComparer.Default.Equals(containingType, gameObjectType))
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
    }
}
