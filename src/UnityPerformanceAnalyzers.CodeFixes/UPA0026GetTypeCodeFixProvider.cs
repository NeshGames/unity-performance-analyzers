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
    /// Rewrites <c>x.GetType()</c> to <c>typeof(T)</c> on a value-type receiver. A value type
    /// cannot be derived from, so its runtime type is its static type and both expressions
    /// return the same <see cref="System.Type"/> instance — the rewrite removes the receiver
    /// box and changes nothing else.
    /// </summary>
    /// <remarks>
    /// Offered only when the receiver is a reference the rewrite can drop without changing
    /// what runs: a local, a parameter, a field, <c>this</c>, or an implicit <c>this</c>.
    /// A property or a method call is evaluated once today and not at all afterwards, and an
    /// element access can throw. <see cref="System.Nullable{T}"/> is excluded outright: on a
    /// nullable with a value <c>GetType()</c> returns the underlying type, and on an empty one
    /// it throws, so no <c>typeof</c> spelling preserves both.
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0026GetTypeCodeFixProvider))]
    [Shared]
    public sealed class UPA0026GetTypeCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Use typeof instead";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA0026BoxedReceiverCallAnalyzer.DiagnosticId);

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
                    is InvocationExpressionSyntax invocation))
            {
                return;
            }

            var semanticModel = await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                return;
            }

            ExpressionSyntax? receiver;
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    receiver = memberAccess.Expression;
                    break;

                // GetType() with no receiver at all, inside the value type's own method.
                case IdentifierNameSyntax _:
                    receiver = null;
                    break;

                default:
                    return;
            }

            if (receiver is object && !IsDroppableReference(receiver, semanticModel, context.CancellationToken))
            {
                return;
            }

            var type = receiver is object
                ? semanticModel.GetTypeInfo(receiver, context.CancellationToken).Type
                : semanticModel.GetEnclosingSymbol(invocation.SpanStart, context.CancellationToken)?.ContainingType;

            if (!IsRewritableValueType(type))
            {
                return;
            }

            var typeName = SyntaxFactory.ParseTypeName(
                type!.ToMinimalDisplayString(semanticModel, invocation.SpanStart));
            var replacement = SyntaxFactory.TypeOfExpression(typeName).WithTriviaFrom(invocation);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(
                        root.ReplaceNode(invocation, replacement))),
                    equivalenceKey: Title),
                diagnostic);
        }

        /// <summary>
        /// True for the value types whose <c>GetType()</c> is exactly <c>typeof</c> of their
        /// static type. Type parameters are already outside what the analyzer reports; nullable
        /// value types are the one shape where the two expressions genuinely differ.
        /// </summary>
        private static bool IsRewritableValueType(ITypeSymbol? type)
        {
            return type is object &&
                type.IsValueType &&
                !(type is ITypeParameterSymbol) &&
                type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
        }

        /// <summary>
        /// True when evaluating the receiver has no effect the rewrite would remove: a bare
        /// local, parameter or field, <c>this</c>, or a field reached through <c>this</c>.
        /// </summary>
        /// <remarks>
        /// Chains through anything else are rejected, and a pre-push review is why. An earlier
        /// version walked field chains recursively, which accepted <c>holder.Value.GetType()</c>
        /// — and dropping that expression removes the <see cref="System.NullReferenceException"/>
        /// thrown when <c>holder</c> is null. <c>StaticHolder.Value.GetType()</c> is the same
        /// shape with a type initializer instead of an exception: the rewrite would stop
        /// running it.
        ///
        /// The diagnostic still reports on those; only the automatic rewrite is withheld,
        /// because what it would delete is not always nothing.
        /// </remarks>
        private static bool IsDroppableReference(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            switch (expression)
            {
                case ThisExpressionSyntax _:
                    return true;

                case ParenthesizedExpressionSyntax parenthesized:
                    return IsDroppableReference(parenthesized.Expression, semanticModel, cancellationToken);

                case IdentifierNameSyntax identifier:
                {
                    var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    return symbol is ILocalSymbol ||
                        symbol is IParameterSymbol ||
                        (symbol is IFieldSymbol field && !field.IsStatic);
                }

                // Only through this: this.field cannot dereference null and cannot trigger a
                // type initializer, which is what makes it droppable when a.b is not.
                case MemberAccessExpressionSyntax memberAccess:
                {
                    var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
                    return symbol is IFieldSymbol instanceField &&
                        !instanceField.IsStatic &&
                        memberAccess.Expression is ThisExpressionSyntax;
                }

                default:
                    return false;
            }
        }
    }
}
