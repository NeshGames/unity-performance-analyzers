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
    /// Appends <c>.Forget()</c> to an unawaited call that returns a UniTask type, making the
    /// fire-and-forget explicit and routing the task's exceptions to UniTask's unhandled
    /// exception handler instead of dropping them.
    /// </summary>
    /// <remarks>
    /// Only that shape. A call returning <c>Task</c> has no <c>Forget</c> to append — UniTask
    /// defines the extension on its own types — and an <c>async void</c> declaration is fixed
    /// by changing a signature, which reaches every caller. Neither is offered.
    /// <para>
    /// <c>_ = FooAsync();</c> is deliberately not offered either. The rule does not report it,
    /// because writing it says the loss of exceptions was a decision — but a decision is what
    /// it is, and putting it one keystroke away would make silencing cheaper than fixing.
    /// </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA2012ForgetCodeFixProvider))]
    [Shared]
    public sealed class UPA2012ForgetCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Make the fire-and-forget explicit with Forget()";

        private const string UniTaskNamespace = "Cysharp.Threading.Tasks";

        private static readonly SyntaxAnnotation CallAnnotation = new SyntaxAnnotation("upa-forget-call");

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA2012FireAndForgetAnalyzer.DiagnosticId);

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

            // Form A reports on a method declaration; only form B - a call statement - has a
            // rewrite here.
            if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    is InvocationExpressionSyntax invocation) ||
                !(invocation.Parent is ExpressionStatementSyntax))
            {
                return;
            }

            var semanticModel = await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null || !ReturnsUniTask(semanticModel, invocation, context.CancellationToken))
            {
                return;
            }

            if (ForgetIsContested(semanticModel, invocation, context.CancellationToken))
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(AppendForget(context.Document, root, invocation)),
                    equivalenceKey: Title),
                diagnostic);
        }

        private static Document AppendForget(Document document, SyntaxNode root, InvocationExpressionSyntax invocation)
        {
            var forgotten = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    invocation.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("Forget")))
                .WithTriviaFrom(invocation)
                .WithAdditionalAnnotations(CallAnnotation);

            var newRoot = root.ReplaceNode(invocation, forgotten);

            // The node handed to ReplaceNode has no parent in the result, and whether the
            // namespace is imported is a question about where the call ends up. The annotation
            // is how that node is found again.
            var inserted = System.Linq.Enumerable.FirstOrDefault(newRoot.GetAnnotatedNodes(CallAnnotation))
                ?? newRoot;
            return document.WithSyntaxRoot(
                NamespaceImports.EnsureImported(newRoot, inserted, UniTaskNamespace));
        }

        /// <summary>
        /// True when something other than UniTask's own <c>Forget</c> is in scope for this
        /// receiver.
        /// </summary>
        /// <remarks>
        /// Appending a call decides nothing about which method it binds to. Another
        /// <c>Forget(this UniTask)</c> extension that is also imported makes the rewritten call
        /// ambiguous, or silently takes it - and this fix exists to change where exceptions go,
        /// which is exactly what a different Forget would change again. Raised by a pre-push
        /// review.
        /// </remarks>
        private static bool ForgetIsContested(
            SemanticModel semanticModel,
            InvocationExpressionSyntax invocation,
            System.Threading.CancellationToken cancellationToken)
        {
            var receiverType = semanticModel.GetTypeInfo(invocation, cancellationToken).Type;
            if (receiverType is null)
            {
                return true;
            }

            foreach (var candidate in semanticModel.LookupSymbols(
                invocation.SpanStart,
                container: receiverType,
                name: "Forget",
                includeReducedExtensionMethods: true))
            {
                if (candidate.ContainingNamespace?.ToDisplayString() != UniTaskNamespace)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReturnsUniTask(
            SemanticModel semanticModel,
            InvocationExpressionSyntax invocation,
            System.Threading.CancellationToken cancellationToken)
        {
            if (!(semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method))
            {
                return false;
            }

            var returnType = method.ReturnType.OriginalDefinition;
            var name = returnType.ToDisplayString();
            return name == UniTaskNamespace + ".UniTask" ||
                name == UniTaskNamespace + ".UniTask<T>" ||
                name == UniTaskNamespace + ".UniTaskVoid";
        }
    }
}
