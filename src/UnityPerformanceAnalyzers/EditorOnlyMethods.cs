using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Unity messages and attributes that run only in the editor. A player build never calls
    /// them, so per-frame cost there costs nothing.
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
    internal sealed class EditorOnlyMethods
    {
        // Messages Unity calls only in the editor. Reset is deliberately absent: Unity sends
        // it when a component is added in the inspector, but Reset() is also the conventional
        // name for a pooled object's runtime reinitialisation, and a matching name says nothing
        // about which of the two a given method is. The pooled one runs in the build - as often
        // as objects are recycled - and silencing it would leave nothing behind to notice. A
        // finding in a genuine editor-only Reset is visible and costs a suppression; a missed
        // one in a pool costs a frame. The same reasoning keeps ContextMenu out of the
        // attribute list below. The names that remain are not ones runtime code calls.
        private static readonly ImmutableHashSet<string> s_messages = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "OnDrawGizmos",
            "OnDrawGizmosSelected",
            "OnValidate");

        // Resolved to symbols, never matched by display string: a project's own MenuItem
        // attribute in another namespace would otherwise silence rules by accident, and a
        // misspelt name matches nothing and fails silently - which is how "MenuItemAttribute"
        // shipped for a class Unity calls MenuItem. ContextMenu is deliberately absent - a
        // method carrying it can still be called from ordinary code, so the attribute does
        // not establish that it is editor-only.
        private static readonly ImmutableArray<string> s_attributeMetadataNames = ImmutableArray.Create(
            "UnityEditor.MenuItem",
            "UnityEditor.InitializeOnLoadMethodAttribute",
            "UnityEditor.Callbacks.DidReloadScripts");

        private readonly INamedTypeSymbol? _monoBehaviourType;
        private readonly ImmutableHashSet<INamedTypeSymbol> _attributeTypes;

        private EditorOnlyMethods(INamedTypeSymbol? monoBehaviourType, ImmutableHashSet<INamedTypeSymbol> attributeTypes)
        {
            _monoBehaviourType = monoBehaviourType;
            _attributeTypes = attributeTypes;
        }

        /// <summary>
        /// Resolves the types once per compilation. One that UnityEditor does not provide -
        /// a runtime assembly built outside Unity, say - is left out and matches nothing.
        /// </summary>
        public static EditorOnlyMethods Create(Compilation compilation)
        {
            var attributeTypes = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var metadataName in s_attributeMetadataNames)
            {
                var type = compilation.GetTypeByMetadataName(metadataName);
                if (type is object)
                {
                    attributeTypes.Add(type);
                }
            }

            return new EditorOnlyMethods(
                compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour"),
                attributeTypes.ToImmutable());
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

            var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            if (methodSymbol is null)
            {
                return false;
            }

            if (!_attributeTypes.IsEmpty)
            {
                foreach (var attribute in methodSymbol.GetAttributes())
                {
                    if (attribute.AttributeClass is object && _attributeTypes.Contains(attribute.AttributeClass))
                    {
                        return true;
                    }
                }
            }

            // A message only counts on a MonoBehaviour: a plain class with a method named
            // OnDrawGizmos is an ordinary method and Unity never calls it.
            return s_messages.Contains(method.Identifier.ValueText)
                && TypeHierarchy.DerivesFrom(methodSymbol.ContainingType, _monoBehaviourType);
        }
    }
}
