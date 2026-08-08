using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace UnityPerformanceAnalyzers.CodeFixes
{
    /// <summary>
    /// Fixes UPA0029 by collapsing the copy loop into a single <c>AddRange</c> call.
    ///
    /// This provider does no safety analysis of its own, and does not need to: the analyzer
    /// only reports loops whose body is one Add of the element itself, whose source and target
    /// are both stable references, and which are not the same collection. Anything with a
    /// projection, a filter, an extra statement, a re-evaluated receiver, or a self-copy never
    /// reports, so it never reaches this fix.
    ///
    /// The one behaviour difference that remains is deliberate: an indexed loop evaluates the
    /// source's bound and element access per iteration, and the rewrite evaluates the source
    /// once. Since the analyzer restricted both to stable references, that is the same
    /// collection either way.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0029SequentialAddCodeFixProvider))]
    [Shared]
    public sealed class UPA0029SequentialAddCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Use AddRange";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA0029SequentialAddAnalyzer.DiagnosticId);

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
            var loop = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            if (!TryDescribeRewrite(loop, out var target, out var source))
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(ApplyFix(context.Document, root, loop, target!, source!)),
                    equivalenceKey: Title),
                diagnostic);
        }

        private static bool TryDescribeRewrite(
            SyntaxNode loop,
            out ExpressionSyntax? target,
            out ExpressionSyntax? source)
        {
            target = null;
            source = null;

            switch (loop)
            {
                case ForEachStatementSyntax forEach:
                    source = forEach.Expression;
                    return TryGetAddTarget(forEach.Statement, out target);

                case ForStatementSyntax forStatement:
                    if (!TryGetAddTarget(forStatement.Statement, out target))
                    {
                        return false;
                    }

                    source = GetIndexedSource(forStatement.Statement);
                    return source is object;

                default:
                    return false;
            }
        }

        private static bool TryGetAddTarget(StatementSyntax body, out ExpressionSyntax? target)
        {
            target = null;
            var statement = Unwrap(body);
            if (!(statement is ExpressionStatementSyntax expressionStatement) ||
                !(expressionStatement.Expression is InvocationExpressionSyntax invocation) ||
                !(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            target = memberAccess.Expression;
            return true;
        }

        private static ExpressionSyntax? GetIndexedSource(StatementSyntax body)
        {
            var statement = Unwrap(body);
            if (!(statement is ExpressionStatementSyntax expressionStatement) ||
                !(expressionStatement.Expression is InvocationExpressionSyntax invocation) ||
                invocation.ArgumentList.Arguments.Count != 1)
            {
                return null;
            }

            return invocation.ArgumentList.Arguments[0].Expression is ElementAccessExpressionSyntax elementAccess
                ? elementAccess.Expression
                : null;
        }

        private static StatementSyntax Unwrap(StatementSyntax body) =>
            body is BlockSyntax block && block.Statements.Count == 1 ? block.Statements[0] : body;

        private static Document ApplyFix(
            Document document,
            SyntaxNode root,
            SyntaxNode loop,
            ExpressionSyntax target,
            ExpressionSyntax source)
        {
            var addRange = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        target.WithoutTrivia(),
                        SyntaxFactory.IdentifierName("AddRange")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(source.WithoutTrivia())))))
                .WithTriviaFrom(loop)
                .WithAdditionalAnnotations(Formatter.Annotation);

            return document.WithSyntaxRoot(root.ReplaceNode(loop, addRange));
        }
    }
}
