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
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var materialType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Material");
            var propertyBlockType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.MaterialPropertyBlock");
            var shaderType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Shader");
            var animatorType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Animator");
            if (materialType is null && propertyBlockType is null && shaderType is null && animatorType is null)
            {
                return;
            }

            var hotPathOnly = ResolveHotPathOnlyOption(ctx.Compilation, ctx.Options);
            var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(
                    opCtx, materialType, propertyBlockType, shaderType, animatorType, hotPathOnly, hotPathDetector),
                OperationKind.Invocation);
        }

        private static bool ResolveHotPathOnlyOption(Compilation compilation, AnalyzerOptions analyzerOptions)
        {
            var firstTree = compilation.SyntaxTrees.FirstOrDefault();
            if (firstTree is null)
            {
                return false;
            }

            var options = analyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(firstTree);
            return options.TryGetValue(HotPathOnlyOptionKey, out var raw) &&
                bool.TryParse(raw.Trim(), out var parsed) &&
                parsed;
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? materialType,
            INamedTypeSymbol? propertyBlockType,
            INamedTypeSymbol? shaderType,
            INamedTypeSymbol? animatorType,
            bool hotPathOnly,
            HotPathDetector hotPathDetector)
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
