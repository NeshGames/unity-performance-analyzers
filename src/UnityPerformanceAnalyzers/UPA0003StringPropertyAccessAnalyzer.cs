using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0003: Reports string-based property and state access on <c>Material</c>,
    /// <c>MaterialPropertyBlock</c>, <c>Shader</c>, and <c>Animator</c> when the name argument
    /// is a string literal or compile-time constant. The string is hashed to an ID on every
    /// call; caching the ID via <c>Shader.PropertyToID</c> / <c>Animator.StringToHash</c> in a
    /// static class removes the repeated conversion. Not hot-path-limited by default —
    /// centralizing property IDs is a project-structure concern — but can be narrowed with
    /// <c>upa_shader_property_hot_path_only = true</c>.
    /// </summary>
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0003StringPropertyAccessAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0003";

        internal const string HotPathOnlyOptionKey = "upa_shader_property_hot_path_only";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var materialType = ctx.Type("UnityEngine.Material");
            var propertyBlockType = ctx.Type("UnityEngine.MaterialPropertyBlock");
            var shaderType = ctx.Type("UnityEngine.Shader");
            var animatorType = ctx.Type("UnityEngine.Animator");
            if (materialType is null && propertyBlockType is null && shaderType is null && animatorType is null)
            {
                return;
            }

            // Needed to tell a MonoBehaviour's Awake from any other method with that name.
            var monoBehaviourType = ctx.Type("UnityEngine.MonoBehaviour");
            var scriptableObjectType = ctx.Type("UnityEngine.ScriptableObject");

            var options = UpaOptions.Resolve(ctx.Options);
            var configProvider = ctx.Options.AnalyzerConfigOptionsProvider;
            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(
                    opCtx,
                    materialType,
                    propertyBlockType,
                    shaderType,
                    animatorType,
                    // Read at the call site, not once for the compilation: an .editorconfig
                    // section applies to the files it globs, and answering from the first
                    // syntax tree gave every file whatever the first one was configured with.
                    options.GetBool(
                        HotPathOnlyOptionKey, opCtx.Operation.Syntax.SyntaxTree, configProvider, fallback: false),
                    hotPathDetector,
                    monoBehaviourType,
                    scriptableObjectType),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? materialType,
            INamedTypeSymbol? propertyBlockType,
            INamedTypeSymbol? shaderType,
            INamedTypeSymbol? animatorType,
            bool hotPathOnly,
            HotPathDetector hotPathDetector,
            INamedTypeSymbol? monoBehaviourType,
            INamedTypeSymbol? scriptableObjectType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!IsStringNameMethod(method, materialType, propertyBlockType, shaderType, animatorType))
            {
                return;
            }

            var nameParameter = method.Parameters[0];
            var nameArgument = invocation.Arguments.FirstOrDefault(
                argument => SymbolEqualityComparer.Default.Equals(argument.Parameter, nameParameter));
            if (nameArgument is null)
            {
                return;
            }

            var constantValue = nameArgument.Value.ConstantValue;
            if (!constantValue.HasValue || !(constantValue.Value is string nameValue))
            {
                return;
            }

            // The claim - the string is resolved on every call - holds in a constructor too. It
            // simply buys nothing there, and eleven of this rule's fourteen findings on real
            // game code were exactly that: setup that runs once, where caching the id into a
            // new static field is code churn for no gain.
            if (IsOneShotInitialisation(context.ContainingSymbol, monoBehaviourType, scriptableObjectType))
            {
                return;
            }

            if (hotPathOnly)
            {
                var semanticModel = invocation.SemanticModel;
                if (semanticModel is null ||
                    !hotPathDetector.IsInHotPath(invocation.Syntax, semanticModel, context.CancellationToken))
                {
                    return;
                }
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                $"{method.ContainingType.Name}.{method.Name}",
                nameValue));
        }

        /// <summary>
        /// True for the places that run once and cannot repay a cached property id: constructors
        /// and field initialisers, Awake and Start, and methods invoked from the inspector.
        /// </summary>
        /// <remarks>
        /// <c>OnEnable</c> is deliberately absent. It looks like a sibling of <c>Awake</c> and is
        /// not one: <c>Awake</c> runs once in an object's life, <c>OnEnable</c> runs on every
        /// reactivation, and pooling - which UPA0031 in this same package recommends - reactivates
        /// objects constantly. Excluding it would let one rule's advice create a shape another
        /// rule can no longer see, and the suppression would leave no trace. <c>OnDisable</c> and
        /// <c>OnDestroy</c> are absent for the matching reason: the test is "runs once in the
        /// object's life", not "is named like a lifecycle method".
        /// </remarks>
        private static bool IsOneShotInitialisation(
            ISymbol? containingSymbol,
            INamedTypeSymbol? monoBehaviourType,
            INamedTypeSymbol? scriptableObjectType)
        {
            if (!(containingSymbol is IMethodSymbol method))
            {
                // A field initialiser is attributed to the field itself.
                return containingSymbol is IFieldSymbol;
            }

            switch (method.MethodKind)
            {
                case MethodKind.Constructor:
                case MethodKind.StaticConstructor:
                    return true;
            }

            // Full metadata names. A short-name match would let any project that happens to
            // define its own MenuItemAttribute silence this rule in ordinary runtime code, and
            // the silence would leave nothing behind to notice.
            foreach (var attribute in method.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                if (name == "UnityEngine.ContextMenu" || name == "UnityEditor.MenuItem")
                {
                    return true;
                }
            }

            return (method.Name == "Awake" || method.Name == "Start")
                && (TypeHierarchy.DerivesFrom(method.ContainingType, monoBehaviourType)
                    || TypeHierarchy.DerivesFrom(method.ContainingType, scriptableObjectType));
        }

        private static bool IsStringNameMethod(
            IMethodSymbol method,
            INamedTypeSymbol? materialType,
            INamedTypeSymbol? propertyBlockType,
            INamedTypeSymbol? shaderType,
            INamedTypeSymbol? animatorType)
        {
            // The name must be the leading string parameter — this also naturally excludes the
            // conversion functions themselves being flagged for their non-name signatures.
            if (method.Parameters.Length == 0 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            var containingType = method.ContainingType;
            var name = method.Name;

            if (SymbolEqualityComparer.Default.Equals(containingType, materialType))
            {
                return name == "HasProperty" || StartsWith(name, "Set") || StartsWith(name, "Get");
            }

            if (SymbolEqualityComparer.Default.Equals(containingType, propertyBlockType))
            {
                return StartsWith(name, "Set") || StartsWith(name, "Get");
            }

            if (SymbolEqualityComparer.Default.Equals(containingType, shaderType))
            {
                // Shader.PropertyToID does not match either prefix, so it is never reported.
                return StartsWith(name, "SetGlobal") || StartsWith(name, "GetGlobal");
            }

            if (SymbolEqualityComparer.Default.Equals(containingType, animatorType))
            {
                // Animator.StringToHash does not match any of these, so it is never reported.
                return name == "Play" ||
                    name == "CrossFade" ||
                    name == "CrossFadeInFixedTime" ||
                    name == "ResetTrigger" ||
                    StartsWith(name, "Set") ||
                    StartsWith(name, "Get");
            }

            return false;
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.Ordinal);
        }
    }
}
