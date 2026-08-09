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
    public sealed class UPA0005DirectDebugLoggingAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0005";

        internal const string WrapperTypesOptionKey = "upa_log_wrapper_types";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

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
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var debugType = ctx.Type("UnityEngine.Debug");
            if (debugType is null)
            {
                return;
            }

            var conditionalAttributeType =
                ctx.Type("System.Diagnostics.ConditionalAttribute");

            // Resolved here, not in the callback. UpaOptions.Resolve reads and parses the
            // options file, and the callback runs once per Debug call -- in a logging-heavy
            // project that is the same file parsed thousands of times, against the
            // once-per-compilation contract the type documents.
            var options = UpaOptions.Resolve(ctx.Options);
            var configProvider = ctx.Options.AnalyzerConfigOptionsProvider;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, debugType, conditionalAttributeType, options, configProvider),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol debugType,
            INamedTypeSymbol? conditionalAttributeType,
            UpaOptions options,
            AnalyzerConfigOptionsProvider configProvider)
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

            if (IsInsideWrapperType(context, invocation, options, configProvider))
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

        private static bool IsInsideWrapperType(
            OperationAnalysisContext context,
            IInvocationOperation invocation,
            UpaOptions options,
            AnalyzerConfigOptionsProvider configProvider)
        {
            // Through UpaOptions, so the options file works here too. .editorconfig alone is
            // IDE-only: Unity does not pass it to the compiler, so a value set there did
            // nothing in an actual build.
            // The per-tree lookup stays here -- an .editorconfig section applies to the file
            // the call is in -- but the options file behind it was parsed once.
            var wrapperTypeNames = options.GetList(
                WrapperTypesOptionKey,
                invocation.Syntax.SyntaxTree,
                configProvider,
                ImmutableArray<string>.Empty);
            if (wrapperTypeNames.IsEmpty)
            {
                return false;
            }

            for (var type = context.ContainingSymbol.ContainingType; type is object; type = type.ContainingType)
            {
                foreach (var wrapperTypeName in wrapperTypeNames)
                {
                    if (string.Equals(type.Name, wrapperTypeName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
