using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Shared infrastructure that decides whether a syntax node sits on a per-frame hot path
    /// Only the enclosing method body is considered — no cross-method
    /// analysis, deliberately trading missed reports for a low false-positive rate.
    /// </summary>
    internal sealed class HotPathDetector
    {
        internal const string MessagesOptionKey = "upa_hot_path_messages";
        internal const string AttributesOptionKey = "upa_hot_path_attributes";
        internal const string IncludeLambdasOptionKey = "upa_hot_path_include_lambdas";

        private const string AttributeSuffix = "Attribute";

        private static readonly ImmutableHashSet<string> s_defaultHotMessages = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Update",
            "FixedUpdate",
            "LateUpdate",
            "OnGUI",
            "OnAnimatorMove",
            "OnAnimatorIK",
            "OnPreCull",
            "OnPreRender",
            "OnPostRender",
            "OnRenderObject",
            "OnWillRenderObject",
            "OnRenderImage",
            "OnTriggerStay",
            "OnTriggerStay2D",
            "OnCollisionStay",
            "OnCollisionStay2D",
            "OnParticleUpdateJobScheduled");

        // Stored without the "Attribute" suffix; lookups normalize the same way.
        private static readonly ImmutableHashSet<string> s_defaultHotAttributes = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "HotPath",
            "PerformanceCritical");

        private readonly INamedTypeSymbol? _monoBehaviourType;
        private readonly ImmutableHashSet<string> _hotMessages;
        private readonly ImmutableHashSet<string> _hotAttributes;
        private readonly bool _includeLambdas;

        private HotPathDetector(
            INamedTypeSymbol? monoBehaviourType,
            ImmutableHashSet<string> hotMessages,
            ImmutableHashSet<string> hotAttributes,
            bool includeLambdas)
        {
            _monoBehaviourType = monoBehaviourType;
            _hotMessages = hotMessages;
            _hotAttributes = hotAttributes;
            _includeLambdas = includeLambdas;
        }

        /// <summary>
        /// Resolves configuration once per compilation. Options come from the universal options
        /// file first, then .editorconfig, then the built-in defaults (see UpaOptions); missing
        /// or unreadable values fall back and resolution never throws.
        /// </summary>
        public static HotPathDetector Create(Compilation compilation, AnalyzerOptions analyzerOptions)
        {
            var monoBehaviourType = compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour");

            var upaOptions = UpaOptions.Resolve(analyzerOptions);
            var firstTree = compilation.SyntaxTrees.FirstOrDefault();
            var provider = analyzerOptions.AnalyzerConfigOptionsProvider;

            var hotMessages = ToNameSet(
                upaOptions.GetList(MessagesOptionKey, firstTree, provider, ImmutableArray<string>.Empty),
                s_defaultHotMessages,
                stripAttributeSuffix: false);
            var hotAttributes = ToNameSet(
                upaOptions.GetList(AttributesOptionKey, firstTree, provider, ImmutableArray<string>.Empty),
                s_defaultHotAttributes,
                stripAttributeSuffix: true);
            var includeLambdas = upaOptions.GetBool(IncludeLambdasOptionKey, firstTree, provider, fallback: true);

            return new HotPathDetector(monoBehaviourType, hotMessages, hotAttributes, includeLambdas);
        }

        /// <summary>
        /// The standard per-operation guard: true when the operation should not report
        /// because it has no semantic model or sits outside every hot path. Wraps
        /// <see cref="IsInHotPath"/> so the seventeen hot-path rules share one guard.
        /// </summary>
        public bool IsOutsideHotPath(IOperation operation, CancellationToken cancellationToken)
        {
            var semanticModel = operation.SemanticModel;
            return semanticModel is null ||
                !IsInHotPath(operation.Syntax, semanticModel, cancellationToken);
        }

        /// <summary>
        /// True when the nearest enclosing method declaration is a hot-path method: either a
        /// hot Unity message on a MonoBehaviour-derived type, or a method carrying a hot-path
        /// attribute (matched by short name, no type resolution). Nodes inside lambdas or local
        /// functions of such a method count as hot unless upa_hot_path_include_lambdas is false.
        /// </summary>
        public bool IsInHotPath(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            var sawNestedFunction = false;
            MethodDeclarationSyntax? method = null;

            for (var current = node.Parent; current is object; current = current.Parent)
            {
                if (current is AnonymousFunctionExpressionSyntax || current is LocalFunctionStatementSyntax)
                {
                    sawNestedFunction = true;
                }
                else if (current is MethodDeclarationSyntax methodDeclaration)
                {
                    method = methodDeclaration;
                    break;
                }
                else if (current is BaseTypeDeclarationSyntax)
                {
                    break;
                }
            }

            if (method is null)
            {
                return false;
            }

            if (sawNestedFunction && !_includeLambdas)
            {
                return false;
            }

            if (HasHotPathAttribute(method))
            {
                return true;
            }

            if (!_hotMessages.Contains(method.Identifier.ValueText))
            {
                return false;
            }

            var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            return methodSymbol is object && TypeHierarchy.DerivesFrom(methodSymbol.ContainingType, _monoBehaviourType);
        }

        private bool HasHotPathAttribute(MethodDeclarationSyntax method)
        {
            foreach (var attributeList in method.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (_hotAttributes.Contains(StripAttributeSuffix(GetShortName(attribute.Name))))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static ImmutableHashSet<string> ToNameSet(
            ImmutableArray<string> names,
            ImmutableHashSet<string> defaults,
            bool stripAttributeSuffix)
        {
            if (names.IsEmpty)
            {
                return defaults;
            }

            var builder = ImmutableHashSet.CreateBuilder(StringComparer.Ordinal);
            foreach (var name in names)
            {
                builder.Add(stripAttributeSuffix ? StripAttributeSuffix(name) : name);
            }

            return builder.ToImmutable();
        }

        private static string GetShortName(NameSyntax name)
        {
            switch (name)
            {
                case SimpleNameSyntax simple:
                    return simple.Identifier.ValueText;
                case QualifiedNameSyntax qualified:
                    return qualified.Right.Identifier.ValueText;
                case AliasQualifiedNameSyntax aliasQualified:
                    return aliasQualified.Name.Identifier.ValueText;
                default:
                    return name.ToString();
            }
        }

        private static string StripAttributeSuffix(string name)
        {
            return name.Length > AttributeSuffix.Length && name.EndsWith(AttributeSuffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - AttributeSuffix.Length)
                : name;
        }
    }
}
