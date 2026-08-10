using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers.CodeFixes
{
    /// <summary>
    /// Replaces a string property name with a <c>static readonly int</c> field on the type
    /// that contains the call, initialized from <c>Shader.PropertyToID</c> or
    /// <c>Animator.StringToHash</c>.
    /// </summary>
    /// <remarks>
    /// The field goes on the containing type rather than into a shared cache class. Both
    /// remove the same work — the string is hashed once instead of on every call — and the
    /// shared-class version would have this fix create a type, choose a file for it and pick a
    /// namespace, then pour every literal in the project into one file nobody opened.
    /// <para>
    /// Fix All shares one field per literal per type. That is not a nicety: the corpus has a
    /// single file with 58 of these diagnostics across a dozen distinct names, and a
    /// per-diagnostic fix would emit 58 fields, most of them duplicates of each other.
    /// </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UPA0003CachePropertyIdCodeFixProvider))]
    [Shared]
    public sealed class UPA0003CachePropertyIdCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Cache the property ID in a static field";

        private const string UnityEngineNamespace = "UnityEngine";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(UPA0003StringPropertyAccessAnalyzer.DiagnosticId);

        /// <inheritdoc/>
        public override FixAllProvider GetFixAllProvider() => new PerDocumentFixAll();

        /// <inheritdoc/>
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics[0];
            var candidates = await FindCandidatesAsync(
                context.Document, ImmutableArray.Create(diagnostic), context.CancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    token => FixAsync(context.Document, ImmutableArray.Create(diagnostic), token),
                    nameof(UPA0003CachePropertyIdCodeFixProvider)),
                diagnostic);
        }

        /// <summary>
        /// Rewrites every reported call in one document at once, which is what lets the
        /// literal-to-field mapping be shared rather than recomputed per diagnostic.
        /// </summary>
        private static async Task<Document> FixAsync(
            Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            var candidates = await FindCandidatesAsync(document, diagnostics, cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                return document;
            }

            var originalRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (originalRoot is null || model is null)
            {
                return document;
            }

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            var newLine = NamespaceImports.EndOfLineIn(originalRoot);

            // Grouped by the type symbol, not by the declaration. A partial type declared
            // twice in one file is two declarations and one type: grouping by syntax gave each
            // its own field, both named after the same literal, and the result did not
            // compile. The symbol also carries the members of declarations in other files,
            // which is what lets an existing cache field be found rather than duplicated.
            foreach (var byType in candidates.GroupBy(
                candidate => candidate.ContainingTypeSymbol, SymbolEqualityComparer.Default))
            {
                var typeSymbol = (INamedTypeSymbol)byType.Key!;
                var containingType = byType.First().ContainingType;
                if (containingType.Members.Count == 0)
                {
                    continue;
                }

                var indent = IndentationOf(containingType.Members[0]);
                var taken = new HashSet<string>(typeSymbol.MemberNames, System.StringComparer.Ordinal);
                taken.Add(typeSymbol.Name);

                var fieldForLiteral = new Dictionary<string, string>(System.StringComparer.Ordinal);
                var added = new List<MemberDeclarationSyntax>();

                foreach (var candidate in byType)
                {
                    if (!fieldForLiteral.TryGetValue(candidate.Literal, out var fieldName))
                    {
                        var existing = ExistingCacheField(typeSymbol, candidate, cancellationToken);
                        if (existing is object)
                        {
                            fieldName = existing;
                        }
                        else
                        {
                            fieldName = UniqueFieldName(
                                candidate.Literal, taken, model, candidate.Invocation.SpanStart);
                            var field = CacheField(fieldName, candidate, indent, newLine);
                            if (field is null)
                            {
                                continue;
                            }

                            added.Add(field);
                        }

                        fieldForLiteral[candidate.Literal] = fieldName;
                    }

                    editor.ReplaceNode(
                        candidate.NameArgument,
                        SyntaxFactory.IdentifierName(fieldName).WithTriviaFrom(candidate.NameArgument));
                }

                if (added.Count > 0)
                {
                    // A blank line after the last one, so the new fields read as a group above
                    // the member they were placed over rather than glued to it.
                    added[added.Count - 1] = added[added.Count - 1].WithTrailingTrivia(newLine, newLine);
                    editor.InsertBefore(containingType.Members[0], added);
                }
            }

            var changed = editor.GetChangedRoot();

            // DocumentEditor marks everything it touches with Formatter.Annotation, and both
            // the IDE and the test harness format annotated nodes afterwards using their own
            // options. That is not a formatting preference to override -- it re-indents the
            // member the field was inserted above as well, so a two-space class comes back
            // four-space. The trivia here is already the file's own, so the annotation is
            // removed rather than obeyed.
            changed = changed.ReplaceNodes(
                changed.GetAnnotatedNodes(Formatter.Annotation),
                (_, rewritten) => rewritten.WithoutAnnotations(Formatter.Annotation));

            // Any, not All. One file can hold two namespace declarations where only one
            // imports UnityEngine -- the other reaching Material through its full name -- and
            // asking whether *every* call site lacked the import answered no, so nothing was
            // added and the field generated in the second namespace could not resolve Shader.
            // Adding at the compilation unit covers both; a namespace that already has the
            // directive keeps working, it just has it twice.
            if (candidates.Any(candidate => !NamespaceImports.IsImportedAt(candidate.Invocation, UnityEngineNamespace)))
            {
                changed = NamespaceImports.EnsureImported(changed, changed, UnityEngineNamespace);
            }

            // No call to Formatter. Its options come from the workspace, which in an IDE means
            // that user's global settings -- and both of the ways this fix has produced
            // mismatched output were exactly that. A stray CRLF in an LF file, because
            // formatting defaults to Environment.NewLine. Then a field indented two spaces
            // inside a four-space class, because Visual Studio was configured for two, which
            // the unit tests could never see: their workspace is an AdhocWorkspace whose
            // defaults happen to match. The trivia is taken from the file instead.
            return document.WithSyntaxRoot(changed);
        }

        private static async Task<List<Candidate>> FindCandidatesAsync(
            Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            var found = new List<Candidate>();

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null)
            {
                return found;
            }

            foreach (var diagnostic in diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = Candidate.From(root, semanticModel, diagnostic, cancellationToken);
                if (candidate is object)
                {
                    found.Add(candidate);
                }
            }

            return found;
        }

        /// <summary>A field name for this property that is free where the call sits.</summary>
        /// <remarks>
        /// Shader property names conventionally lead with an underscore ("_MainTex"), which
        /// makes a poor field name, so the underscore goes and the first letter is raised.
        /// No prefix: Rider's default naming rule for private static readonly fields is
        /// PascalCase, and a fix that silences one inspection while raising another has not
        /// really finished.
        /// <para>
        /// Dropping the prefix is what makes the scope check necessary rather than tidy.
        /// "_Color" is among the most common shader property names, and a field called
        /// <c>Color</c> shadows <see cref="T:UnityEngine.Color"/> for the whole type — so
        /// every other line in that class using <c>Color.white</c> stops compiling. The
        /// semantic model is asked whether the name is already something here, which covers
        /// types, members and locals alike; on a clash the name gains an <c>Id</c>.
        /// </para>
        /// </remarks>
        private static string UniqueFieldName(
            string literal, HashSet<string> taken, SemanticModel semanticModel, int position)
        {
            var core = new StringBuilder();
            foreach (var character in literal.TrimStart('_'))
            {
                if (char.IsLetterOrDigit(character) || character == '_')
                {
                    core.Append(character);
                }
            }

            if (core.Length == 0 || !char.IsLetter(core[0]))
            {
                core.Insert(0, "Property");
            }

            core[0] = char.ToUpperInvariant(core[0]);

            var stem = core.ToString();
            var name = stem;
            for (var attempt = 0; IsTaken(name); attempt++)
            {
                name = attempt == 0
                    ? stem + "Id"
                    : stem + "Id" + (attempt + 1).ToString(CultureInfo.InvariantCulture);
            }

            taken.Add(name);
            return name;

            bool IsTaken(string candidate) =>
                taken.Contains(candidate) ||
                semanticModel.LookupSymbols(position, name: candidate).Length > 0;
        }

        /// <summary>
        /// The whitespace the type's first member sits at. Read from the file rather than left
        /// to the formatter for the same reason the line ending is: the formatter uses this
        /// machine's options, and a field reindented to the IDE's global setting does not line
        /// up with the code it was inserted into.
        /// </summary>
        private static SyntaxTriviaList IndentationOf(MemberDeclarationSyntax anchor)
        {
            foreach (var trivia in anchor.GetLeadingTrivia().Reverse())
            {
                if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                {
                    return SyntaxFactory.TriviaList(trivia);
                }
            }

            return SyntaxFactory.TriviaList();
        }

        /// <summary>
        /// The field, parsed from text rather than assembled from factory calls.
        /// </summary>
        /// <remarks>
        /// A node built with SyntaxFactory carries <em>elastic</em> trivia between its tokens,
        /// which means the formatter decides how it is laid out — and the formatter's options
        /// come from the machine, not the file. That is how a four-space class came back with a
        /// two-space field in Visual Studio. Text parsed with ParseMemberDeclaration has the
        /// trivia it was written with and nothing to negotiate.
        /// </remarks>
        private static FieldDeclarationSyntax? CacheField(
            string name, Candidate candidate, SyntaxTriviaList indent, SyntaxTrivia newLine)
        {
            var literal = SyntaxFactory.Literal(candidate.Literal).ToFullString();
            var text = indent.ToFullString() +
                "private static readonly int " + name + " = " +
                candidate.ConversionType + "." + candidate.ConversionMethod + "(" + literal + ");" +
                newLine.ToFullString();

            return SyntaxFactory.ParseMemberDeclaration(text) as FieldDeclarationSyntax;
        }

        /// <summary>
        /// A field already caching this literal the same way, if the type has one. Reusing it
        /// is what stops a second run of the fix from adding a near-duplicate beside the first.
        /// </summary>
        private static string? ExistingCacheField(
            INamedTypeSymbol typeSymbol, Candidate candidate, CancellationToken cancellationToken)
        {
            var wanted = candidate.ConversionType + "." + candidate.ConversionMethod;

            // Through the symbol, so a partial type's other declarations count too -- including
            // one in a document Fix All has already rewritten, which is what makes "one field
            // per name per type" true rather than "per file".
            foreach (var field in typeSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                foreach (var reference in field.DeclaringSyntaxReferences)
                {
                    if (!(reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax declarator))
                    {
                        continue;
                    }
                    if (!(declarator.Initializer?.Value is InvocationExpressionSyntax invocation) ||
                        !(invocation.Expression is MemberAccessExpressionSyntax access) ||
                        access.ToString() != wanted ||
                        invocation.ArgumentList.Arguments.Count != 1)
                    {
                        continue;
                    }

                    if (invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                        literal.IsKind(SyntaxKind.StringLiteralExpression) &&
                        literal.Token.ValueText == candidate.Literal)
                    {
                        return declarator.Identifier.ValueText;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> MemberNames(MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    return field.Declaration.Variables.Select(variable => variable.Identifier.ValueText);
                case PropertyDeclarationSyntax property:
                    return new[] { property.Identifier.ValueText };
                case MethodDeclarationSyntax method:
                    return new[] { method.Identifier.ValueText };
                case TypeDeclarationSyntax type:
                    return new[] { type.Identifier.ValueText };
                case EventFieldDeclarationSyntax eventField:
                    return eventField.Declaration.Variables.Select(variable => variable.Identifier.ValueText);
                default:
                    return Enumerable.Empty<string>();
            }
        }

        /// <summary>One reported call that can actually be rewritten.</summary>
        private sealed class Candidate
        {
            private Candidate(
                InvocationExpressionSyntax invocation,
                ExpressionSyntax nameArgument,
                string literal,
                string conversionType,
                string conversionMethod,
                TypeDeclarationSyntax containingType,
                INamedTypeSymbol containingTypeSymbol)
            {
                Invocation = invocation;
                NameArgument = nameArgument;
                Literal = literal;
                ConversionType = conversionType;
                ConversionMethod = conversionMethod;
                ContainingType = containingType;
                ContainingTypeSymbol = containingTypeSymbol;
            }

            public InvocationExpressionSyntax Invocation { get; }

            public ExpressionSyntax NameArgument { get; }

            public string Literal { get; }

            public string ConversionType { get; }

            public string ConversionMethod { get; }

            public TypeDeclarationSyntax ContainingType { get; }

            /// <summary>The type the field goes on, across every declaration of a partial.</summary>
            public INamedTypeSymbol ContainingTypeSymbol { get; }

            public static Candidate? From(
                SyntaxNode root, SemanticModel semanticModel, Diagnostic diagnostic, CancellationToken cancellationToken)
            {
                if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                        is InvocationExpressionSyntax invocation))
                {
                    return null;
                }

                if (!(semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation operation))
                {
                    return null;
                }

                var method = operation.TargetMethod;
                if (method.Parameters.Length == 0)
                {
                    return null;
                }

                var nameArgument = operation.Arguments.FirstOrDefault(
                    argument => SymbolEqualityComparer.Default.Equals(argument.Parameter, method.Parameters[0]));

                // A compile-time constant that is not a literal is reported but not fixed:
                // replacing the constant's name with a new field would declare the same name
                // twice, and that constant may be the project's own way of centralizing it.
                if (!(nameArgument?.Value.Syntax is LiteralExpressionSyntax literalSyntax) ||
                    !literalSyntax.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return null;
                }

                if (!HasIntOverload(method))
                {
                    return null;
                }

                var containingType = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (containingType is null ||
                    !(semanticModel.GetDeclaredSymbol(containingType, cancellationToken) is INamedTypeSymbol containingTypeSymbol))
                {
                    return null;
                }

                var isAnimator = method.ContainingType.ToDisplayString() == "UnityEngine.Animator";

                return new Candidate(
                    invocation,
                    literalSyntax,
                    literalSyntax.Token.ValueText,
                    isAnimator ? "Animator" : "Shader",
                    isAnimator ? "StringToHash" : "PropertyToID",
                    containingType,
                    containingTypeSymbol);
            }

            /// <summary>
            /// Whether the same call exists taking an <see langword="int"/> name. Without this
            /// the rewrite can produce code that does not compile — and the analyzer cannot
            /// answer it, because it only ever looks at the string overload.
            /// </summary>
            private static bool HasIntOverload(IMethodSymbol method)
            {
                foreach (var candidate in method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>())
                {
                    if (candidate.Parameters.Length != method.Parameters.Length ||
                        candidate.Parameters[0].Type.SpecialType != SpecialType.System_Int32)
                    {
                        continue;
                    }

                    var matches = true;
                    for (var index = 1; index < candidate.Parameters.Length; index++)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(
                                candidate.Parameters[index].Type, method.Parameters[index].Type))
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Fixes a whole document in one pass rather than merging per-diagnostic fixes.
        /// </summary>
        /// <remarks>
        /// The batch fixer cannot do this: each of its actions is computed against the
        /// unchanged document, so fifty calls naming the same property would each add their own
        /// field, all with the same name.
        /// </remarks>
        private sealed class PerDocumentFixAll : FixAllProvider
        {
            public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
            {
                var documents = new List<Document>();
                switch (fixAllContext.Scope)
                {
                    case FixAllScope.Document:
                        documents.Add(fixAllContext.Document!);
                        break;
                    case FixAllScope.Project:
                        documents.AddRange(fixAllContext.Project.Documents);
                        break;
                    case FixAllScope.Solution:
                        foreach (var project in fixAllContext.Solution.Projects)
                        {
                            documents.AddRange(project.Documents);
                        }

                        break;
                    default:
                        return null;
                }

                // Diagnostics are not enough to offer on. UPA0003 reports compile-time
                // constants that are not literals, which this fix deliberately does not
                // rewrite, so a scope holding only those would have offered "Fix all
                // occurrences", changed nothing, and left every one of them in place.
                var work = new List<(DocumentId Id, ImmutableArray<Diagnostic> Diagnostics)>();
                foreach (var document in documents)
                {
                    var diagnostics = await fixAllContext
                        .GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);
                    if (diagnostics.Length == 0)
                    {
                        continue;
                    }

                    var candidates = await FindCandidatesAsync(
                        document, diagnostics, fixAllContext.CancellationToken).ConfigureAwait(false);
                    if (candidates.Count > 0)
                    {
                        work.Add((document.Id, diagnostics));
                    }
                }

                if (work.Count == 0)
                {
                    return null;
                }

                var solution = fixAllContext.Solution;
                return CodeAction.Create(
                    Title,
                    async cancellationToken =>
                    {
                        var updated = solution;
                        foreach (var (id, diagnostics) in work)
                        {
                            var document = updated.GetDocument(id);
                            if (document is null)
                            {
                                continue;
                            }

                            var fixedDocument = await FixAsync(document, diagnostics, cancellationToken)
                                .ConfigureAwait(false);
                            var root = await fixedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                            if (root is object)
                            {
                                updated = updated.WithDocumentSyntaxRoot(id, root);
                            }
                        }

                        return updated;
                    },
                    nameof(UPA0003CachePropertyIdCodeFixProvider));
            }
        }
    }
}
