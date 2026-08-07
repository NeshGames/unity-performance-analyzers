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
    /// Fixes UPA0022 by rewriting <c>x.HasFlag(y)</c> to the allocation-free bitwise check
    /// <c>(x &amp; y) == y</c>. The rewrite duplicates the flag expression, so the fix is
    /// offered only when that expression is provably single-evaluation-safe (a constant,
    /// local, parameter, or field) — a method call or property getter could observe the
    /// second evaluation.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0022HasFlagCodeFixProvider))]
    [Shared]
    public sealed class UPA0022HasFlagCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Use a bitwise check";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA0022HasFlagAnalyzer.DiagnosticId);

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
            if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is InvocationExpressionSyntax invocation) ||
                !(invocation.Expression is MemberAccessExpressionSyntax memberAccess) ||
                invocation.ArgumentList.Arguments.Count != 1)
            {
                return;
            }

            var flagExpression = invocation.ArgumentList.Arguments[0].Expression;
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null ||
                !IsSingleEvaluationSafe(flagExpression, semanticModel, context.CancellationToken))
            {
                return;
            }

            var operand = memberAccess.Expression.WithoutTrivia();
            var flag = flagExpression.WithoutTrivia();

            var bitwiseCheck = SyntaxFactory.BinaryExpression(
                SyntaxKind.EqualsExpression,
                SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.BinaryExpression(SyntaxKind.BitwiseAndExpression, operand, flag)),
                flag)
                .WithTriviaFrom(invocation)
                .WithAdditionalAnnotations(Formatter.Annotation);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(
                        root.ReplaceNode(invocation, bitwiseCheck))),
                    equivalenceKey: Title),
                diagnostic);
        }

        // Constants (enum members, const fields, literals) and plain storage reads cannot
        // observe being evaluated twice; method calls and property getters could.
        private static bool IsSingleEvaluationSafe(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (semanticModel.GetConstantValue(expression, cancellationToken).HasValue)
            {
                return true;
            }

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            return symbol is ILocalSymbol || symbol is IParameterSymbol || symbol is IFieldSymbol;
        }
    }
}
