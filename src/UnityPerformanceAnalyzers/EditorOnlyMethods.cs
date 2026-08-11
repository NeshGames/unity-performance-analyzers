using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Unity messages and attributes that run only in the editor. Code inside them is stripped
    /// from a player build, so per-frame cost there costs nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not the same job as <see cref="HotPathDetector"/> and does not overlap it.
    /// <c>OnDrawGizmos</c> has always been outside HOT_MESSAGES, which protects the rules that
    /// are hot-path scoped — but the rules that are not never consult that detector at all, and
    /// so reported freely inside gizmo methods. Measured on real game code: two of UPA0021's
    /// fourteen findings sat in <c>OnDrawGizmosSelected</c>, advising a square-root rewrite on
    /// a path that does not exist in a build.
    /// </para>
    /// <para>
    /// The test is "does not run in a player build", not "runs rarely". <c>OnValidate</c> fires
    /// on every inspector edit and is not rare at all; it belongs here because a build never
    /// calls it. Writing the reason as rarity is how a later correct observation — that
    /// OnValidate is frequent — would produce the wrong conclusion.
    /// </para>
    /// <para>
    /// It is also not the same job as the <c>[**/Editor/**.cs]</c> section in the presets. That
    /// one grades by file location and reaches editor assemblies; this one reaches a method on a
    /// runtime type, which no path-based rule can see.
    /// </para>
    /// </remarks>
    internal static class EditorOnlyMethods
    {
        private static readonly ImmutableHashSet<string> s_messages = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "OnDrawGizmos",
            "OnDrawGizmosSelected",
            "OnValidate",
            "Reset");

        // Full metadata names: a project's own [MenuItem] would otherwise silence rules by
        // accident. ContextMenu is deliberately absent - a method carrying it can still be
        // called from ordinary code, so the attribute does not establish that it is editor-only.
        private static readonly ImmutableHashSet<string> s_attributes = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "UnityEditor.MenuItemAttribute",
            "UnityEditor.InitializeOnLoadMethodAttribute",
            "UnityEditor.Callbacks.DidReloadScriptsAttribute");

        /// <summary>
        /// True when <paramref name="node"/> sits in a method Unity calls only in the editor.
        /// </summary>
        public static bool Contains(
            SyntaxNode node,
            SemanticModel semanticModel,
            INamedTypeSymbol? monoBehaviourType,
            CancellationToken cancellationToken)
        {
            MethodDeclarationSyntax? method = null;

            for (var current = node.Parent; current is object; current = current.Parent)
            {
                if (current is MethodDeclarationSyntax methodDeclaration)
                {
                    method = methodDeclaration;
                    break;
                }

                if (current is BaseTypeDeclarationSyntax)
                {
                    break;
                }
            }

            if (method is null)
            {
                return false;
            }

            var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            if (methodSymbol is null)
            {
                return false;
            }

            foreach (var attribute in methodSymbol.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                if (name is object && s_attributes.Contains(name))
                {
                    return true;
                }
            }

            // A message only counts on a MonoBehaviour: a plain class with a method named
            // OnDrawGizmos is an ordinary method and Unity never calls it.
            return s_messages.Contains(method.Identifier.ValueText)
                && TypeHierarchy.DerivesFrom(methodSymbol.ContainingType, monoBehaviourType);
        }
    }
}
