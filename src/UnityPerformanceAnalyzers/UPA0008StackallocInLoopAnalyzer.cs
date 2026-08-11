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
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0008StackallocInLoopAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0008";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            ctx.RegisterSyntaxNodeAction(
                AnalyzeStackalloc,
                SyntaxKind.StackAllocArrayCreationExpression,
                SyntaxKind.ImplicitStackAllocArrayCreationExpression);
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
