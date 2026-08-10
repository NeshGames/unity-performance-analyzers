using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Threading;
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
    /// Hoists the reported <c>List&lt;T&gt;.Count</c> read into a local declared just before
    /// the loop, and points the loop condition at it.
    /// </summary>
    /// <remarks>
    /// Whether that is safe is the analyzer's judgement, not this provider's: UPA0009 only
    /// reports when nothing in the loop body could reach the collection except by reading it,
    /// which is exactly the condition under which one read stands in for all of them. Deciding
    /// it twice, in two places, with two implementations, is how the two come apart.
    /// <para>
    /// Not offered when the loop is an embedded statement, because inserting a declaration
    /// before it would mean synthesising a block around it — a change to the shape of the
    /// code rather than to the expression that was reported.
    /// </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0009HoistCountCodeFixProvider))]
    [Shared]
    public sealed class UPA0009HoistCountCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Hoist Count into a local before the loop";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA0009ListCountInForLoopAnalyzer.DiagnosticId);

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
            if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    is MemberAccessExpressionSyntax countAccess))
            {
                return;
            }

            var forStatement = countAccess.FirstAncestorOrSelf<ForStatementSyntax>();
            if (forStatement?.Condition is null ||
                !forStatement.Condition.Span.Contains(countAccess.Span) ||
                !(forStatement.Parent is BlockSyntax block))
            {
                return;
            }

            var semanticModel = await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                return;
            }

            var localName = PickLocalName(countAccess, semanticModel, forStatement.SpanStart, context.CancellationToken);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(Hoist(context.Document, root, block, forStatement, countAccess, localName)),
                    equivalenceKey: Title),
                diagnostic);
        }

        private static Document Hoist(
            Document document,
            SyntaxNode root,
            BlockSyntax block,
            ForStatementSyntax forStatement,
            MemberAccessExpressionSyntax countAccess,
            string localName)
        {
            var reference = SyntaxFactory.IdentifierName(localName);

            // Every syntactically identical read in the condition, not just the reported one:
            // one hoisted local is the answer to all of them, and leaving the others behind
            // would report again on the code this fix just produced.
            var text = countAccess.ToString();
            var newCondition = forStatement.Condition!.ReplaceNodes(
                forStatement.Condition!.DescendantNodesAndSelf()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(node => node.ToString() == text),
                (original, _) => reference.WithTriviaFrom(original));

            // The line break is taken from the file rather than from the platform: a fix that
            // emits CRLF into an LF file leaves a diff on a line nobody edited.
            var declaration = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(localName))
                            .WithInitializer(SyntaxFactory.EqualsValueClause(countAccess.WithoutTrivia())))))
                .WithLeadingTrivia(forStatement.GetLeadingTrivia())
                .WithTrailingTrivia(EndOfLineIn(root));

            var newFor = forStatement.WithCondition(newCondition);

            var newBlock = block.ReplaceNode(forStatement, new StatementSyntax[] { declaration, newFor });
            return document.WithSyntaxRoot(root.ReplaceNode(block, newBlock));
        }

        /// <summary>The line break this file already uses, falling back to the platform's.</summary>
        private static SyntaxTrivia EndOfLineIn(SyntaxNode root)
        {
            foreach (var trivia in root.DescendantTrivia())
            {
                if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    return trivia;
                }
            }

            return SyntaxFactory.EndOfLine(System.Environment.NewLine);
        }

        /// <summary>
        /// A name derived from the receiver — <c>items</c> becomes <c>itemsCount</c> — with a
        /// number appended if anything by that name is already visible where the loop starts.
        /// </summary>
        private static string PickLocalName(
            MemberAccessExpressionSyntax countAccess,
            SemanticModel semanticModel,
            int position,
            CancellationToken cancellationToken)
        {
            var receiver = countAccess.Expression is MemberAccessExpressionSyntax member
                ? member.Name.Identifier.ValueText
                : countAccess.Expression.ToString();

            var trimmed = receiver.TrimStart('_');
            var stem = trimmed.Length == 0
                ? "count"
                : char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1) + "Count";

            var candidate = stem;
            for (var suffix = 2; !semanticModel.LookupSymbols(position, name: candidate).IsEmpty; suffix++)
            {
                candidate = stem + suffix.ToString(CultureInfo.InvariantCulture);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return candidate;
        }
    }
}
