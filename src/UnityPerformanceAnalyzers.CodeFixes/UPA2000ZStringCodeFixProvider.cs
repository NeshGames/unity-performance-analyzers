using System.Collections.Generic;
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
    /// Rewrites a hot-path string concatenation to <c>ZString.Concat(...)</c> — but only where
    /// that was measured to buy something.
    /// </summary>
    /// <remarks>
    /// On IL2CPP, <c>"score: " + score</c> collects every ~1,000 iterations against ~1,700 for
    /// <c>ZString.Concat("score: ", score)</c>: the <c>+</c> form binds to the
    /// <c>string + object</c> operator, which boxes the operand and builds an intermediate
    /// string, and ZString's generic overloads do neither.
    /// <para>
    /// With every operand already a string the two measured the same — <c>"a" + "b"</c>
    /// compiles to <c>string.Concat(string, string)</c> and allocates once, which is what
    /// ZString does too. The fix is not offered there. A bulb that rewrites code into
    /// something that allocates the same amount is noise, and noise is what retired UPA0022.
    /// </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA2000ZStringCodeFixProvider))]
    [Shared]
    public sealed class UPA2000ZStringCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Concatenate with ZString";

        private const string ZStringNamespace = "Cysharp.Text";

        private const string ZStringMetadataName = "Cysharp.Text.ZString";

        /// <summary>ZString.Concat's widest overload. Longer chains get no fix.</summary>
        private const int MaximumOperands = 16;

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA2000StringConcatenationAnalyzer.DiagnosticId);

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

            // The rule also reports += and interpolation. Neither is a concatenation this fix
            // can rewrite: the first wants a builder to mean anything, and the second can carry
            // format specifiers.
            if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    is BinaryExpressionSyntax concatenation) ||
                !concatenation.IsKind(SyntaxKind.AddExpression))
            {
                return;
            }

            var semanticModel = await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                return;
            }

            // The analyzer decides ZString is present by assembly name, which is right for
            // choosing advice. The fix needs the type itself: without it the rewrite does not
            // compile.
            var zstring = semanticModel.Compilation.GetTypeByMetadataName(ZStringMetadataName);
            if (zstring is null)
            {
                return;
            }

            // And it needs the name ZString to still mean that type where the call is written.
            // A local, parameter or field called ZString shadows it, and the added using does
            // not help: name resolution reaches lexical scope first. Found by a pre-push review.
            if (IsShadowed(semanticModel, concatenation.SpanStart, zstring, context.CancellationToken))
            {
                return;
            }

            var operands = new List<ExpressionSyntax>();
            Flatten(concatenation, semanticModel, operands, context.CancellationToken);

            if (operands.Count < 2 || operands.Count > MaximumOperands)
            {
                return;
            }

            var kinds = OperandKinds(operands, semanticModel, context.CancellationToken);
            if (kinds == OperandShape.Unresolvable || kinds == OperandShape.AllStrings)
            {
                return;
            }

            var call = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("ZString"),
                    SyntaxFactory.IdentifierName("Concat")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(
                        operands.ConvertAll(operand => SyntaxFactory.Argument(operand.WithoutTrivia())))))
                .WithTriviaFrom(concatenation)
                .WithAdditionalAnnotations(CallAnnotation);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(
                        ImportAndReplace(root, concatenation, call))),
                    equivalenceKey: Title),
                diagnostic);
        }

        /// <summary>
        /// The operands of the chain, left to right. Only <c>+</c> nodes that are themselves
        /// strings are flattened, so the integer addition in <c>"v: " + (a + b)</c> stays one
        /// argument rather than becoming two.
        /// </summary>
        private static void Flatten(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            List<ExpressionSyntax> operands,
            CancellationToken cancellationToken)
        {
            if (expression is BinaryExpressionSyntax binary &&
                binary.IsKind(SyntaxKind.AddExpression) &&
                semanticModel.GetTypeInfo(binary, cancellationToken).Type?.SpecialType == SpecialType.System_String)
            {
                Flatten(binary.Left, semanticModel, operands, cancellationToken);
                Flatten(binary.Right, semanticModel, operands, cancellationToken);
                return;
            }

            operands.Add(expression);
        }

        private enum OperandShape
        {
            /// <summary>At least one operand is not a string: the case ZString helps with.</summary>
            Mixed,

            /// <summary>Every operand is already a string, where the rewrite measured the same.</summary>
            AllStrings,

            /// <summary>An operand has no type — a null literal, say — so no overload resolves.</summary>
            Unresolvable,
        }

        /// <summary>
        /// The rewritten root, with the ZString namespace imported where the call now sits.
        /// </summary>
        private static SyntaxNode ImportAndReplace(
            SyntaxNode root,
            BinaryExpressionSyntax concatenation,
            InvocationExpressionSyntax call)
        {
            var rewritten = root.ReplaceNode(concatenation, call);
            var inserted = rewritten.GetAnnotatedNodes(CallAnnotation);
            var context = System.Linq.Enumerable.FirstOrDefault(inserted) ?? rewritten;
            return NamespaceImports.EnsureImported(rewritten, context, ZStringNamespace);
        }

        private static readonly SyntaxAnnotation CallAnnotation = new SyntaxAnnotation("upa-zstring-call");

        /// <summary>
        /// True when the identifier <c>ZString</c> would bind to something other than the type
        /// at this position.
        /// </summary>
        private static bool IsShadowed(
            SemanticModel semanticModel,
            int position,
            INamedTypeSymbol zstring,
            CancellationToken cancellationToken)
        {
            foreach (var candidate in semanticModel.LookupSymbols(position, name: "ZString"))
            {
                if (!SymbolEqualityComparer.Default.Equals(candidate, zstring))
                {
                    return true;
                }
            }

            return false;
        }

        private static OperandShape OperandKinds(
            List<ExpressionSyntax> operands,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var allStrings = true;
            foreach (var operand in operands)
            {
                var type = semanticModel.GetTypeInfo(operand, cancellationToken).Type;
                if (type is null || type.TypeKind == TypeKind.Error)
                {
                    return OperandShape.Unresolvable;
                }

                if (type.SpecialType != SpecialType.System_String)
                {
                    allStrings = false;
                }
            }

            return allStrings ? OperandShape.AllStrings : OperandShape.Mixed;
        }
    }
}
