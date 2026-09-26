using System;
using System.Collections.Concurrent;
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
    [UpaClaim(UpaClaimKind.PerFrameCost)]
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

            // The wrapper list as it applies to each file, split once per file rather than once
            // per Debug call - in a logging-heavy project that is thousands of splits of the
            // same string. Per file because an .editorconfig section applies to the files it
            // globs; the dictionary lives in this compilation's callbacks and dies with them.
            var wrapperTypesByTree = new ConcurrentDictionary<SyntaxTree, ImmutableArray<string>>();
            Func<SyntaxTree, ImmutableArray<string>> readWrapperTypes =
                tree => ctx.GetList(WrapperTypesOptionKey, tree, ImmutableArray<string>.Empty);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(
                    opCtx, debugType, conditionalAttributeType, wrapperTypesByTree, readWrapperTypes),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol debugType,
            INamedTypeSymbol? conditionalAttributeType,
            ConcurrentDictionary<SyntaxTree, ImmutableArray<string>> wrapperTypesByTree,
            Func<SyntaxTree, ImmutableArray<string>> readWrapperTypes)
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

            if (IsInsideWrapperType(
                context, wrapperTypesByTree.GetOrAdd(invocation.Syntax.SyntaxTree, readWrapperTypes)))
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

        // Through UpaOptions, so the options file works here too. Unity does not pass
        // .editorconfig to the compiler, so a value set only there did nothing in an actual
        // build.
        private static bool IsInsideWrapperType(
            OperationAnalysisContext context,
            ImmutableArray<string> wrapperTypeNames)
        {
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
