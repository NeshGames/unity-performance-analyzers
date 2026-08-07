using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2021: Reports public event field declarations whose delegate type is the
    /// System.Action family — the idiomatic "state changed" notification shape that R3's
    /// ReactiveProperty/Observable replaces with composable, disposable subscriptions.
    /// Registered only when the compilation references R3. EventHandler
    /// conventions, non-public events, custom delegate types, and plain Action fields are
    /// deliberately excluded — the heuristic stays narrow (docs/rules/UPA2021.md).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2021ActionEventAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2021";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2021Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2021MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2021Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2021.md");

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
                if (!profile.HasR3)
                {
                    return;
                }

                ctx.RegisterSymbolAction(AnalyzeEvent, SymbolKind.Event);
            });
        }

        private static void AnalyzeEvent(SymbolAnalysisContext context)
        {
            var eventSymbol = (IEventSymbol)context.Symbol;
            if (eventSymbol.DeclaredAccessibility != Accessibility.Public)
            {
                return;
            }

            if (!IsActionFamily(eventSymbol.Type))
            {
                return;
            }

            // Field-like event declarations only; custom add/remove events may carry an
            // established API contract.
            var syntaxReference = eventSymbol.DeclaringSyntaxReferences.IsEmpty
                ? null
                : eventSymbol.DeclaringSyntaxReferences[0];
            if (!(syntaxReference?.GetSyntax(context.CancellationToken) is VariableDeclaratorSyntax))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                eventSymbol.Locations[0],
                eventSymbol.Name));
        }

        private static bool IsActionFamily(ITypeSymbol type)
        {
            var definition = type.OriginalDefinition;
            if (definition.Name != "Action" || definition.TypeKind != TypeKind.Delegate)
            {
                return false;
            }

            var ns = definition.ContainingNamespace;
            return ns is object && ns.Name == "System" &&
                   ns.ContainingNamespace is object && ns.ContainingNamespace.IsGlobalNamespace;
        }
    }
}
