using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0023: Reports <c>OnGUI</c> declarations on MonoBehaviour-derived types in player
    /// assemblies (editor assemblies are skipped by name — see UpaProfile.IsEditorAssembly).
    /// IMGUI runs every frame and
    /// is not intended for in-game interfaces — its supported uses are debug overlays and
    /// editor tooling. Info severity and disabled by default: development-time overlays are
    /// legitimate and common.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0023OnGuiDeclarationAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0023";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0023Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0023MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Info,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA0023Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0023.md");

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

                var monoBehaviourType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour");
                if (monoBehaviourType is null)
                {
                    return;
                }

                ctx.RegisterSymbolAction(
                    symbolCtx => AnalyzeMethod(symbolCtx, monoBehaviourType),
                    SymbolKind.Method);
            });
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context, INamedTypeSymbol monoBehaviourType)
        {
            var method = (IMethodSymbol)context.Symbol;

            if (method.MethodKind != MethodKind.Ordinary ||
                method.Name != "OnGUI" ||
                method.IsStatic ||
                !method.ReturnsVoid ||
                !method.Parameters.IsEmpty)
            {
                return;
            }

            if (!InheritsFrom(method.ContainingType, monoBehaviourType))
            {
                return;
            }

            var location = method.Locations.FirstOrDefault();
            if (location is object)
            {
                context.ReportDiagnostic(UpaDiagnostics.Create(Rule, location));
            }
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
