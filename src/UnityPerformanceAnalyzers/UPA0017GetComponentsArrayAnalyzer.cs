using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0017: Reports the array-returning <c>GetComponents</c>/<c>GetComponentsInChildren</c>/
    /// <c>GetComponentsInParent</c> overloads on per-frame hot paths. Each call allocates a fresh
    /// array; the <c>List&lt;T&gt;</c> overloads fill a caller-provided list instead. The singular
    /// <c>GetComponent</c> lookups and the <c>List&lt;T&gt;</c> overloads are UPA0001's territory.
    /// </summary>
    [HotPathRule]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
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

        /// <summary>
        /// The calls this rule owns. UPA0001 asks the same question to stand aside, so the
        /// boundary between the two is one predicate rather than two lists that can drift.
        /// </summary>
        internal static bool IsArrayReturningPluralLookup(IMethodSymbol method)
        {
            // The List<T> overloads return void — only the array-returning shapes allocate.
            return s_pluralLookupNames.Contains(method.Name) && method.ReturnType is IArrayTypeSymbol;
        }

        /// <summary>
        /// Whether this rule's findings in <paramref name="tree"/> are switched off by
        /// configuration: a ruleset or <c>/nowarn</c> (compilation-wide), an
        /// <c>.editorconfig</c> or global config <c>dotnet_diagnostic.UPA0017.severity</c>, or a
        /// ruleset's <c>IncludeAll</c>. UPA0001 only stands aside for calls UPA0017 will report.
        /// </summary>
        /// <remarks>
        /// The lookup order is the compiler's (Roslyn 3.8, checked against its output): the
        /// tree's own entry, then the compilation-wide map, then the global config, then the
        /// descriptor's default under the general option. A <c>#pragma warning disable</c> is
        /// not configuration an analyzer can see; it is applied after the diagnostic exists.
        /// </remarks>
        internal static bool IsSwitchedOff(Compilation compilation, SyntaxTree tree, CancellationToken cancellationToken)
        {
            var options = compilation.Options;
            var provider = options.SyntaxTreeOptionsProvider;
            if ((provider is object && provider.TryGetDiagnosticValue(tree, DiagnosticId, cancellationToken, out var severity)) ||
                options.SpecificDiagnosticOptions.TryGetValue(DiagnosticId, out severity) ||
                (provider is object && provider.TryGetGlobalDiagnosticValue(DiagnosticId, cancellationToken, out severity)))
            {
                return severity == ReportDiagnostic.Suppress ||
                    (severity == ReportDiagnostic.Default && !Rule.IsEnabledByDefault);
            }

            return Rule.GetEffectiveSeverity(options) == ReportDiagnostic.Suppress;
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? componentType,
            INamedTypeSymbol? gameObjectType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!IsArrayReturningPluralLookup(method))
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
