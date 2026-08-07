using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0025: Reports finalizer declarations in player assemblies (editor assemblies are
    /// skipped by name — see UpaProfile.IsEditorAssembly). Unity's guidance is to not use
    /// C# finalizers in runtime code:
    /// they delay memory reclamation across garbage collections and run on the finalizer
    /// thread. Deterministic cleanup belongs in IDisposable.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0025FinalizerAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0025";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0025Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0025MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0025Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0025.md");

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
                if (UpaProfile.IsEditorAssembly(ctx.Compilation))
                {
                    return;
                }

                ctx.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
            });
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;

            if (method.MethodKind != MethodKind.Destructor)
            {
                return;
            }

            var location = method.Locations.FirstOrDefault();
            if (location is object)
            {
                context.ReportDiagnostic(UpaDiagnostics.Create(Rule, location));
            }
        }
    }
}
