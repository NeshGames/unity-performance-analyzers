using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityPerformanceAnalyzers.CodeFixes
{
    /// <summary>
    /// Fixes UPA0019 by replacing the boxed yield value with <c>null</c> — Unity treats both
    /// as a one-frame wait, and <c>yield return null</c> does not allocate.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0019BoxedYieldCodeFixProvider))]
    [Shared]
    public sealed class UPA0019BoxedYieldCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Yield null instead";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA0019BoxedYieldAnalyzer.DiagnosticId);

        /// <inheritdoc/>
        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc/>
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return;
            }

            var diagnostic = context.Diagnostics[0];
            if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is ExpressionSyntax expression))
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ =>
                    {
                        var nullLiteral = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                            .WithTriviaFrom(expression);
                        var newRoot = root.ReplaceNode(expression, nullLiteral);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: Title),
                diagnostic);
        }
    }
}
