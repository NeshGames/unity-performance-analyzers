using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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

        /// <summary>The Unity messages treated as per-frame work when nothing overrides them.
        /// Exposed so the option catalog's stated default can be checked against the real one.</summary>
        internal static ImmutableHashSet<string> DefaultHotMessages => s_defaultHotMessages;

        /// <summary>The attribute short names that mark a method hot when nothing overrides them.</summary>
        internal static ImmutableHashSet<string> DefaultHotAttributes => s_defaultHotAttributes;

        private readonly INamedTypeSymbol? _monoBehaviourType;
        private readonly UpaOptions _options;
        private readonly AnalyzerConfigOptionsProvider _provider;

        // Per tree because an .editorconfig section applies to the files it globs. Resolving
        // once from the first syntax tree gave every file whatever that one was configured
        // with, so the answer depended on the order the host listed the files in. The
        // dictionary lives on this instance, which lives exactly as long as one compilation.
        private readonly ConcurrentDictionary<SyntaxTree, Settings> _settingsByTree =
            new ConcurrentDictionary<SyntaxTree, Settings>();

        private readonly Func<SyntaxTree, Settings> _resolveSettings;

        private HotPathDetector(
            INamedTypeSymbol? monoBehaviourType,
            UpaOptions options,
            AnalyzerConfigOptionsProvider provider)
        {
            _monoBehaviourType = monoBehaviourType;
            _options = options;
            _provider = provider;
            _resolveSettings = ResolveSettings;
        }

        /// <summary>
        /// Parses the options file and builds a detector. For callers outside the analyzer
        /// base class; the analyzers go through <see cref="UpaCompilationContext.HotPath"/>,
        /// which reuses the parse the rest of the compilation context already did.
        /// </summary>
        public static HotPathDetector Create(Compilation compilation, AnalyzerOptions analyzerOptions)
            => Create(compilation, UpaOptions.Resolve(analyzerOptions), analyzerOptions.AnalyzerConfigOptionsProvider);

        /// <summary>
        /// One detector per compilation. Options come from the universal options file first,
        /// then the .editorconfig section that applies to the file being asked about, then the
        /// built-in defaults (see UpaOptions); missing or unreadable values fall back and
        /// resolution never throws.
        /// </summary>
        public static HotPathDetector Create(
            Compilation compilation,
            UpaOptions options,
            AnalyzerConfigOptionsProvider provider)
            => new HotPathDetector(
                compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour"),
                options,
                provider);

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

            var settings = _settingsByTree.GetOrAdd(method.SyntaxTree, _resolveSettings);

            if (sawNestedFunction && !settings.IncludeLambdas)
            {
                return false;
            }

            if (HasHotPathAttribute(method, settings.HotAttributes))
            {
                return true;
            }

            if (!settings.HotMessages.Contains(method.Identifier.ValueText))
            {
                return false;
            }

            var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            return methodSymbol is object && TypeHierarchy.DerivesFrom(methodSymbol.ContainingType, _monoBehaviourType);
        }

        private Settings ResolveSettings(SyntaxTree tree)
        {
            var hotMessages = ToNameSet(
                _options.GetList(MessagesOptionKey, tree, _provider, ImmutableArray<string>.Empty),
                s_defaultHotMessages,
                stripAttributeSuffix: false);
            var hotAttributes = ToNameSet(
                _options.GetList(AttributesOptionKey, tree, _provider, ImmutableArray<string>.Empty),
                s_defaultHotAttributes,
                stripAttributeSuffix: true);
            var includeLambdas = _options.GetBool(IncludeLambdasOptionKey, tree, _provider, fallback: true);

            return new Settings(hotMessages, hotAttributes, includeLambdas);
        }

        private static bool HasHotPathAttribute(MethodDeclarationSyntax method, ImmutableHashSet<string> hotAttributes)
        {
            foreach (var attributeList in method.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (hotAttributes.Contains(StripAttributeSuffix(GetShortName(attribute.Name))))
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

        private sealed class Settings
        {
            public Settings(
                ImmutableHashSet<string> hotMessages,
                ImmutableHashSet<string> hotAttributes,
                bool includeLambdas)
            {
                HotMessages = hotMessages;
                HotAttributes = hotAttributes;
                IncludeLambdas = includeLambdas;
            }

            public ImmutableHashSet<string> HotMessages { get; }

            public ImmutableHashSet<string> HotAttributes { get; }

            public bool IncludeLambdas { get; }
        }
    }
}
