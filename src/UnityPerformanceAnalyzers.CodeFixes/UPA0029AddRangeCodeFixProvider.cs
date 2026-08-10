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
    /// Replaces a copy loop with <c>target.AddRange(source)</c> — but only when the source is
    /// an array.
    /// </summary>
    /// <remarks>
    /// The fix was withdrawn before v0.6 shipped because two references can point at the same
    /// <c>List&lt;T&gt;</c> at runtime and no symbol comparison rules it out: copying a list
    /// into itself throws or never ends today, and AddRange would quietly do neither. An array
    /// cannot be the <c>List&lt;T&gt;</c> being appended to, so that case does not exist for
    /// this subset — which is the withdrawal's own reasoning, run the other way.
    /// <para>
    /// One difference remains and belongs on the rule page: a null source throws
    /// NullReferenceException from the loop today and ArgumentNullException from AddRange
    /// afterwards. Both crash where they stood; neither turns into a third behaviour.
    /// </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0029AddRangeCodeFixProvider))]
    [Shared]
    public sealed class UPA0029AddRangeCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Replace the copy loop with AddRange";

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

            var rewrite = loop switch
            {
                ForEachStatementSyntax forEach => TryDescribeForEach(forEach),
                ForStatementSyntax forStatement => TryDescribeIndexedFor(forStatement),
                _ => null,
            };

            if (rewrite is null)
            {
                return;
            }

            var semanticModel = await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                return;
            }

            // The whole safety argument for offering this at all.
            if (!(semanticModel.GetTypeInfo(rewrite.Value.Source, context.CancellationToken).Type
                    is IArrayTypeSymbol))
            {
                return;
            }

            var call = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        rewrite.Value.Target.WithoutTrivia(),
                        SyntaxFactory.IdentifierName("AddRange")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(rewrite.Value.Source.WithoutTrivia())))))
                .WithTriviaFrom(loop);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(loop, call))),
                    equivalenceKey: Title),
                diagnostic);
        }

        private readonly struct Rewrite
        {
            public Rewrite(ExpressionSyntax target, ExpressionSyntax source)
            {
                Target = target;
                Source = source;
            }

            public ExpressionSyntax Target { get; }

            public ExpressionSyntax Source { get; }
        }

        /// <summary>foreach (var item in source) target.Add(item);</summary>
        private static Rewrite? TryDescribeForEach(ForEachStatementSyntax forEach)
        {
            if (!(SingleAddCall(forEach.Statement) is (ExpressionSyntax target, ExpressionSyntax added)))
            {
                return null;
            }

            return added is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == forEach.Identifier.ValueText
                ? new Rewrite(target, forEach.Expression)
                : (Rewrite?)null;
        }

        /// <summary>for (int i = 0; i &lt; source.Length; i++) target.Add(source[i]);</summary>
        private static Rewrite? TryDescribeIndexedFor(ForStatementSyntax forStatement)
        {
            if (!(SingleAddCall(forStatement.Statement) is (ExpressionSyntax target, ExpressionSyntax added)))
            {
                return null;
            }

            return added is ElementAccessExpressionSyntax elementAccess
                ? new Rewrite(target, elementAccess.Expression)
                : (Rewrite?)null;
        }

        /// <summary>
        /// The receiver and the argument of the body's only statement, when that statement is a
        /// single <c>Add</c> call. Anything else — a filter, a second statement, a member being
        /// added rather than the element — is a shape the analyzer does not report, so a match
        /// here is only confirming what it already decided.
        /// </summary>
        private static (ExpressionSyntax Target, ExpressionSyntax Added)? SingleAddCall(StatementSyntax body)
        {
            var statement = body is BlockSyntax block
                ? block.Statements.Count == 1 ? block.Statements[0] : null
                : body;

            if (!(statement is ExpressionStatementSyntax expressionStatement) ||
                !(expressionStatement.Expression is InvocationExpressionSyntax invocation) ||
                !(invocation.Expression is MemberAccessExpressionSyntax memberAccess) ||
                memberAccess.Name.Identifier.ValueText != "Add" ||
                invocation.ArgumentList.Arguments.Count != 1)
            {
                return null;
            }

            return (memberAccess.Expression, invocation.ArgumentList.Arguments[0].Expression);
        }
    }
}
