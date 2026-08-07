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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2011CoroutineAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2011";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2011Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2011MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2011Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2011.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var profile = UpaProfile.Resolve(ctx.Compilation, ctx.Options);
                if (!profile.HasUniTask)
                {
                    return;
                }

                var monoBehaviourType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour");
                var enumeratorType = ctx.Compilation.GetTypeByMetadataName("System.Collections.IEnumerator");
                if (monoBehaviourType is null || enumeratorType is null)
                {
                    return;
                }

                ctx.RegisterSyntaxNodeAction(
                    nodeCtx => AnalyzeMethod(nodeCtx, monoBehaviourType, enumeratorType),
                    SyntaxKind.MethodDeclaration);
            });
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

            if (!InheritsFrom(method.ContainingType, monoBehaviourType))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                declaration.Identifier.GetLocation(),
                method.Name));
        }

        private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
        {
            for (var current = type; current is object; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
