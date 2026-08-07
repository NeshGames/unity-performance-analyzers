using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0008: Reports <c>stackalloc</c> expressions inside a loop of the same method body.
    /// Stack memory is only reclaimed when the method returns, so every iteration reserves a
    /// new region while previous ones stay live — a stack-overflow risk. A stackalloc at the
    /// top level of a local function or lambda invoked from a loop is not reported: that
    /// memory is reclaimed when the nested function returns.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0008StackallocInLoopAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0008";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0008Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0008MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0008Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0008.md");

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
                ctx.RegisterSyntaxNodeAction(
                    AnalyzeStackalloc,
                    SyntaxKind.StackAllocArrayCreationExpression,
                    SyntaxKind.ImplicitStackAllocArrayCreationExpression));
        }

        private static void AnalyzeStackalloc(SyntaxNodeAnalysisContext context)
        {
            for (var current = context.Node.Parent; current is object; current = current.Parent)
            {
                switch (current)
                {
                    case ForStatementSyntax _:
                    case ForEachStatementSyntax _:
                    case CommonForEachStatementSyntax _:
                    case WhileStatementSyntax _:
                    case DoStatementSyntax _:
                        context.ReportDiagnostic(UpaDiagnostics.Create(Rule, context.Node.GetLocation()));
                        return;

                    // Function boundaries reset the lifetime: stack memory reserved inside a
                    // nested function is reclaimed when that function returns.
                    case LocalFunctionStatementSyntax _:
                    case AnonymousFunctionExpressionSyntax _:
                    case BaseMethodDeclarationSyntax _:
                    case AccessorDeclarationSyntax _:
                        return;
                }
            }
        }
    }
}
