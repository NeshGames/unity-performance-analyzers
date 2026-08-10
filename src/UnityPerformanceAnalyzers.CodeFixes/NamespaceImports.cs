using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityPerformanceAnalyzers.CodeFixes
{
    /// <summary>
    /// Adds a <c>using</c> a rewrite needs, when the file does not already have it.
    /// </summary>
    /// <remarks>
    /// Two fixes call into a package's static or extension members — <c>ZString</c> and
    /// UniTask's <c>Forget</c> — and a call site can easily never have named the package
    /// itself: the type comes back from a method, or the extension is found through a using
    /// that a later edit removed. Emitting the call without the import produces code that does
    /// not compile, which is a worse outcome than not offering the fix.
    /// </remarks>
    internal static class NamespaceImports
    {
        /// <summary>
        /// The root with <paramref name="namespaceName"/> imported where
        /// <paramref name="context"/> sits, or the same root when it already is.
        /// </summary>
        /// <remarks>
        /// Scope is the whole point, and an earlier version got it wrong by scanning every
        /// descendant <c>using</c> in the file. A review supplied the counter-example:
        /// <c>namespace A { using Cysharp.Text; }</c> next to <c>namespace B</c> made the
        /// helper believe B had the import, so it added nothing and the rewrite did not
        /// compile. Only the directives that actually reach the rewrite count - the
        /// compilation unit's, and those of the namespaces enclosing it.
        /// </remarks>
        public static SyntaxNode EnsureImported(SyntaxNode root, SyntaxNode context, string namespaceName)
        {
            if (!(root is CompilationUnitSyntax compilationUnit))
            {
                return root;
            }

            if (IsImportedAt(context, namespaceName))
            {
                return root;
            }

            var directiveToAdd = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
                .WithTrailingTrivia(EndOfLineIn(compilationUnit));

            // Last among the file's usings: an addition there disturbs whatever ordering the
            // file already has the least.
            return compilationUnit.Usings.Count > 0
                ? compilationUnit.InsertNodesAfter(compilationUnit.Usings.Last(), new[] { directiveToAdd })
                : compilationUnit.AddUsings(directiveToAdd);
        }

        /// <summary>
        /// Whether the namespace is imported at this node: the enclosing namespace
        /// declarations first, then the compilation unit.
        /// </summary>
        private static bool IsImportedAt(SyntaxNode context, string namespaceName)
        {
            for (var node = context; node is object; node = node.Parent)
            {
                SyntaxList<UsingDirectiveSyntax> usings;
                switch (node)
                {
                    case CompilationUnitSyntax compilationUnit:
                        usings = compilationUnit.Usings;
                        break;
                    case NamespaceDeclarationSyntax namespaceDeclaration:
                        usings = namespaceDeclaration.Usings;
                        break;
                    default:
                        continue;
                }

                foreach (var directive in usings)
                {
                    if (directive.Alias is null && directive.Name?.ToString() == namespaceName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The line break this file already uses, falling back to the platform's.</summary>
        public static SyntaxTrivia EndOfLineIn(SyntaxNode root)
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
    }
}
