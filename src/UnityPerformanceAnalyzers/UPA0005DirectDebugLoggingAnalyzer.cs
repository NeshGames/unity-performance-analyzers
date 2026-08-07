using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0005: Reports direct calls to <c>UnityEngine.Debug</c> logging methods so projects can
    /// route logging through a wrapper marked with <c>[System.Diagnostics.Conditional]</c>,
    /// letting the compiler strip the calls and their argument expressions from release builds.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0005DirectDebugLoggingAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0005";

        internal const string WrapperTypesOptionKey = "upa_log_wrapper_types";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0005Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0005MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA0005Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0005.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_loggingMethodNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Log",
            "LogWarning",
            "LogError",
            "LogFormat",
            "LogWarningFormat",
            "LogErrorFormat",
            "LogException",
            "LogAssertion",
            "LogAssertionFormat");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var debugType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.Debug");
                if (debugType is null)
                {
                    return;
                }

                var conditionalAttributeType =
                    ctx.Compilation.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute");

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, debugType, conditionalAttributeType),
                    OperationKind.Invocation);
            });
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol debugType,
            INamedTypeSymbol? conditionalAttributeType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!s_loggingMethodNames.Contains(method.Name) ||
                !SymbolEqualityComparer.Default.Equals(method.ContainingType, debugType))
            {
                return;
            }

            if (IsInsideConditionalMethod(context.ContainingSymbol, conditionalAttributeType))
            {
                return;
            }

            if (IsInsideWrapperType(context, invocation))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                $"{debugType.Name}.{method.Name}"));
        }

        private static bool IsInsideConditionalMethod(
            ISymbol containingSymbol,
            INamedTypeSymbol? conditionalAttributeType)
        {
            if (conditionalAttributeType is null)
            {
                return false;
            }

            for (var symbol = containingSymbol; symbol is IMethodSymbol method; symbol = symbol.ContainingSymbol)
            {
                foreach (var attribute in method.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, conditionalAttributeType))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsInsideWrapperType(OperationAnalysisContext context, IInvocationOperation invocation)
        {
            var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(invocation.Syntax.SyntaxTree);
            if (!options.TryGetValue(WrapperTypesOptionKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var wrapperTypeNames = raw.Split(',');
            for (var type = context.ContainingSymbol.ContainingType; type is object; type = type.ContainingType)
            {
                foreach (var wrapperTypeName in wrapperTypeNames)
                {
                    if (string.Equals(type.Name, wrapperTypeName.Trim(), StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
