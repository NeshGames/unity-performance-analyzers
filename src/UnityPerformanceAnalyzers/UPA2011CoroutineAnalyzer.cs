using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2011: Reports methods declared on MonoBehaviour-derived types that return the
    /// non-generic System.Collections.IEnumerator — the coroutine shape, including coroutine
    /// Unity messages like IEnumerator Start(). Registered only when UniTask is referenced:
    /// without it there is no allocation-free replacement to point at, and advice the user
    /// cannot act on is worse than staying silent. Reported at the declaration; StartCoroutine
    /// call sites are deliberately not reported to avoid duplicate diagnostics per coroutine.
    /// IEnumerator&lt;T&gt; methods are typical data iteration and excluded (docs/rules/UPA2011.md).
    /// </summary>
    [ConditionalRule("UniTask")]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2011CoroutineAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2011";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var profile = ctx.Profile;
            if (!profile.HasUniTask)
            {
                return;
            }

            var monoBehaviourType = ctx.Type("UnityEngine.MonoBehaviour");
            var enumeratorType = ctx.Type("System.Collections.IEnumerator");
            if (monoBehaviourType is null || enumeratorType is null)
            {
                return;
            }

            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeMethod(nodeCtx, monoBehaviourType, enumeratorType),
                SyntaxKind.MethodDeclaration);
        }

        private static void AnalyzeMethod(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol monoBehaviourType,
            INamedTypeSymbol enumeratorType)
        {
            var declaration = (MethodDeclarationSyntax)context.Node;
            var method = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
            if (method is null)
            {
                return;
            }

            // Exactly the non-generic IEnumerator — IEnumerator<T> is data iteration, not a coroutine.
            if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, enumeratorType))
            {
                return;
            }

            if (!TypeHierarchy.DerivesFrom(method.ContainingType, monoBehaviourType))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                declaration.Identifier.GetLocation(),
                method.Name));
        }

    }
}
