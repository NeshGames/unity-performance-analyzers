using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
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
    /// Fixes UPA0021 when the non-magnitude side is a plain numeric literal (which is always
    /// non-negative — a negative threshold parses as unary minus and gets no fix, because
    /// squaring it would flip the comparison's meaning). Rewrites <c>v.magnitude</c> to
    /// <c>v.sqrMagnitude</c>, <c>Vector*.Distance(a, b)</c> to <c>(a - b).sqrMagnitude</c>,
    /// and squares the literal.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0021MagnitudeComparisonCodeFixProvider))]
    [Shared]
    public sealed class UPA0021MagnitudeComparisonCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Compare sqrMagnitude against the squared threshold";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA0021MagnitudeComparisonAnalyzer.DiagnosticId);

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
            if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is BinaryExpressionSyntax comparison))
            {
                return;
            }

            var leftRewrite = TryRewriteMagnitudeSide(comparison.Left);
            var rightRewrite = TryRewriteMagnitudeSide(comparison.Right);
            if ((leftRewrite is null) == (rightRewrite is null))
            {
                return;
            }

            var literalSide = leftRewrite is null ? comparison.Left : comparison.Right;
            var squaredLiteral = TrySquareLiteral(literalSide);
            if (squaredLiteral is null)
            {
                return;
            }

            ExpressionSyntax newLeft;
            ExpressionSyntax newRight;
            if (leftRewrite is object)
            {
                newLeft = leftRewrite.WithTriviaFrom(comparison.Left);
                newRight = squaredLiteral.WithTriviaFrom(comparison.Right);
            }
            else
            {
                newLeft = squaredLiteral.WithTriviaFrom(comparison.Left);
                newRight = rightRewrite!.WithTriviaFrom(comparison.Right);
            }

            var newComparison = comparison.WithLeft(newLeft).WithRight(newRight);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(
                        root.ReplaceNode(comparison, newComparison))),
                    equivalenceKey: Title),
                diagnostic);
        }

        /// <summary>
        /// Returns the sqrMagnitude rewrite of a magnitude/Distance expression, or null when
        /// the expression is not one this fix understands (the analyzer's semantic checks are
        /// stricter; shape-matching here is safe because the analyzer already reported).
        /// </summary>
        private static ExpressionSyntax? TryRewriteMagnitudeSide(ExpressionSyntax expression)
        {
            if (expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "magnitude")
            {
                return memberAccess.WithName(SyntaxFactory.IdentifierName("sqrMagnitude"));
            }

            if (expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax method &&
                method.Name.Identifier.ValueText == "Distance" &&
                invocation.ArgumentList.Arguments.Count == 2)
            {
                var difference = SyntaxFactory.BinaryExpression(
                    SyntaxKind.SubtractExpression,
                    invocation.ArgumentList.Arguments[0].Expression,
                    invocation.ArgumentList.Arguments[1].Expression);
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParenthesizedExpression(difference),
                    SyntaxFactory.IdentifierName("sqrMagnitude"))
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }

            return null;
        }

        private static ExpressionSyntax? TrySquareLiteral(ExpressionSyntax expression)
        {
            if (!(expression is LiteralExpressionSyntax literal) ||
                !literal.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                return null;
            }

            var token = literal.Token;
            switch (token.Value)
            {
                case int intValue:
                {
                    var squared = (long)intValue * intValue;
                    return SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(squared.ToString(CultureInfo.InvariantCulture), squared));
                }

                case float floatValue:
                {
                    var squared = floatValue * floatValue;
                    var text = squared.ToString("R", CultureInfo.InvariantCulture) + "f";
                    return SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(text, squared));
                }

                case double doubleValue:
                {
                    var squared = doubleValue * doubleValue;
                    var text = squared.ToString("R", CultureInfo.InvariantCulture);
                    return SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(text, squared));
                }

                default:
                    return null;
            }
        }
    }
}
