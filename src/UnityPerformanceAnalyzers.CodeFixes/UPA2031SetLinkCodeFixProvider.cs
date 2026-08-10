using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityPerformanceAnalyzers.CodeFixes
{
    /// <summary>
    /// Appends <c>.SetLink(gameObject)</c> to a discarded infinite tween, binding its lifetime
    /// to the GameObject so it dies with its target.
    /// </summary>
    /// <remarks>
    /// The rewrite is pure addition: not a character of the existing expression changes, and
    /// the chain's value was already unused — UPA2031 only reports when the outermost call is
    /// an expression statement — so appending a call that returns the same type cannot affect
    /// anything else.
    /// <para>
    /// Not offered where <c>gameObject</c> is not in scope: outside a <c>Component</c>, or in a
    /// static method. There the question of which GameObject owns the tween is one only the
    /// author can answer, and the diagnostic stands unfixed.
    /// </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA2031SetLinkCodeFixProvider))]
    [Shared]
    public sealed class UPA2031SetLinkCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Bind the tween's lifetime with SetLink(gameObject)";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA2031DiscardedInfiniteTweenAnalyzer.DiagnosticId);

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
                    is InvocationExpressionSyntax setLoops))
            {
                return;
            }

            // The same walk the analyzer does: SetLink goes on the outer end of the chain,
            // which is the one position that needs no existing call to move.
            SyntaxNode outermost = setLoops;
            while (outermost.Parent is MemberAccessExpressionSyntax ||
                   outermost.Parent is InvocationExpressionSyntax)
            {
                outermost = outermost.Parent;
            }

            if (!(outermost is ExpressionSyntax chain) || !(outermost.Parent is ExpressionStatementSyntax))
            {
                return;
            }

            var semanticModel = await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null ||
                !GameObjectIsInScope(semanticModel, outermost.SpanStart, context.CancellationToken))
            {
                return;
            }

            var linked = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    chain.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("SetLink")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName("gameObject")))))
                .WithTriviaFrom(chain);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(chain, linked))),
                    equivalenceKey: Title),
                diagnostic);
        }

        /// <summary>
        /// True when the bare identifier <c>gameObject</c> written here would bind to
        /// <c>UnityEngine.Component.gameObject</c>.
        /// </summary>
        /// <remarks>
        /// Deriving from <c>Component</c> in a non-static method is necessary and not
        /// sufficient, which a pre-push review caught: a local, parameter or field named
        /// <c>gameObject</c> shadows the property, and the rewrite then either fails to compile
        /// or - worse - links the tween to something else entirely. So the identifier is looked
        /// up rather than assumed, and the fix is withheld when it resolves to anything but
        /// that property.
        /// </remarks>
        private static bool GameObjectIsInScope(
            SemanticModel semanticModel,
            int position,
            CancellationToken cancellationToken)
        {
            var enclosing = semanticModel.GetEnclosingSymbol(position, cancellationToken);
            if (enclosing is null || enclosing.IsStatic)
            {
                return false;
            }

            var component = semanticModel.Compilation.GetTypeByMetadataName("UnityEngine.Component");
            if (component is null)
            {
                return false;
            }

            // What the emitted identifier will actually bind to, in this scope, with whatever
            // locals and members are in the way.
            var candidates = semanticModel.LookupSymbols(position, name: "gameObject");
            if (candidates.Length != 1)
            {
                return false;
            }

            return candidates[0] is IPropertySymbol property &&
                !property.IsStatic &&
                SymbolEqualityComparer.Default.Equals(property.ContainingType, component);
        }
    }
}
