using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// It is also not the same job as the <c>editor-relaxed</c> ruleset preset. That one grades a
    /// whole editor assembly; this one reaches a method on a runtime type, which no
    /// assembly-wide setting can see.
    /// </para>
    /// </remarks>
    internal sealed class EditorOnlyMethods
    {
        // Reset is deliberately absent although Unity only ever calls it from the editor.
        // The name is not Unity's alone: Reset() is the conventional name for returning a
        // pooled object to its initial state, and game code calls its own Reset every frame.
        // Nothing at a report site can tell the two apart - it would take proving that no code
        // anywhere in the compilation calls the method - so exempting the name silenced real
        // per-frame findings with nothing in the output to show it. A rule reporting in an
        // editor-only Reset costs a pragma; a rule silenced in a runtime one costs the frame.
        private static readonly ImmutableHashSet<string> s_messages = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "OnDrawGizmos",
            "OnDrawGizmosSelected",
            "OnValidate");

        private readonly INamedTypeSymbol? _monoBehaviourType;

        // Resolved symbols, compared by identity: a project's own MenuItemAttribute in some
        // other namespace would otherwise silence rules by accident. Unity names two of these
        // without the Attribute suffix (MenuItem, DidReloadScripts), which a comparison against
        // the conventional suffixed spelling never matched. ContextMenu is deliberately absent
        // - a method carrying it can still be called from ordinary code, so the attribute does
        // not establish that it is editor-only.
        private readonly ImmutableArray<INamedTypeSymbol> _editorOnlyAttributes;

        private EditorOnlyMethods(
            INamedTypeSymbol? monoBehaviourType,
            ImmutableArray<INamedTypeSymbol> editorOnlyAttributes)
        {
            _monoBehaviourType = monoBehaviourType;
            _editorOnlyAttributes = editorOnlyAttributes;
        }

        /// <summary>Resolves the types this needs, once per compilation.</summary>
        public static EditorOnlyMethods Create(Compilation compilation)
        {
            var attributes = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            foreach (var metadataName in new[]
            {
                "UnityEditor.MenuItem",
                "UnityEditor.InitializeOnLoadMethodAttribute",
                "UnityEditor.Callbacks.DidReloadScripts",
            })
            {
                if (compilation.GetTypeByMetadataName(metadataName) is { } type)
                {
                    attributes.Add(type);
                }
            }

            return new EditorOnlyMethods(
                compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour"),
                attributes.ToImmutable());
        }

        /// <summary>
        /// True when <paramref name="node"/> sits in a method Unity calls only in the editor.
        /// </summary>
        public bool Contains(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
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

            // Neither test can succeed without these, and asking for the symbol is the
            // expensive part - so a method with no attributes whose name is not a message
            // never asks. A partial method can carry its attributes on the other declaration,
            // which this syntax does not show.
            var isMessageName = s_messages.Contains(method.Identifier.ValueText);
            var mayCarryEditorAttribute = !_editorOnlyAttributes.IsEmpty
                && (method.AttributeLists.Count > 0 || method.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!isMessageName && !mayCarryEditorAttribute)
            {
                return false;
            }

            var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            if (methodSymbol is null)
            {
                return false;
            }

            if (mayCarryEditorAttribute)
            {
                foreach (var attribute in methodSymbol.GetAttributes())
                {
                    foreach (var editorOnly in _editorOnlyAttributes)
                    {
                        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, editorOnly))
                        {
                            return true;
                        }
                    }
                }
            }

            // A message only counts on a MonoBehaviour: a plain class with a method named
            // OnDrawGizmos is an ordinary method and Unity never calls it.
            return isMessageName
                && TypeHierarchy.DerivesFrom(methodSymbol.ContainingType, _monoBehaviourType);
        }
    }
}
