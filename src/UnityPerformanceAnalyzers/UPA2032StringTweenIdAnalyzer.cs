using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2032: Reports string tween IDs — a <c>string</c> argument to <c>SetId(...)</c> or to
    /// a <c>DOTween</c> static filtered operation (<c>Kill</c>, <c>Play</c>, ...). Per DOTween's
    /// own guidance, ID filtering is the fast path but string IDs are the slowest of the fast
    /// options; int IDs (or filtering by target reference) are faster. Info severity.
    /// Registered only when the compilation references the DOTween assembly.
    /// </summary>
    [ConditionalRule("DOTween")]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2032StringTweenIdAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2032";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Info,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_filteredOperationNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Kill",
            "Play",
            "Pause",
            "Restart",
            "Rewind",
            "Complete",
            "Flip",
            "Goto",
            "TogglePause",
            "IsTweening");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var profile = UpaProfile.Resolve(ctx.Compilation, ctx.Options);
            if (!profile.HasDOTween)
            {
                return;
            }

            var tweenType = ctx.Compilation.GetTypeByMetadataName("DG.Tweening.Tween");
            var doTweenType = ctx.Compilation.GetTypeByMetadataName("DG.Tweening.DOTween");
            if (tweenType is null && doTweenType is null)
            {
                return;
            }

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, tweenType, doTweenType),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? tweenType,
            INamedTypeSymbol? doTweenType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            var isSetId = method.Name == "SetId" &&
                tweenType is object &&
                TypeHierarchy.DerivesFrom(method.ReturnType, tweenType);
            var isFilteredOperation = s_filteredOperationNames.Contains(method.Name) &&
                doTweenType is object &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, doTweenType);

            if (!isSetId && !isFilteredOperation)
            {
                return;
            }

            if (!HasStringIdArgument(invocation))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation()));
        }

        private static bool HasStringIdArgument(IInvocationOperation invocation)
        {
            foreach (var argument in invocation.Arguments)
            {
                // The receiver of a reduced extension method is not an ID argument.
                if (argument.Parameter?.Ordinal == 0 &&
                    invocation.TargetMethod.IsExtensionMethod &&
                    invocation.Instance is null)
                {
                    continue;
                }

                var value = argument.Value;
                while (value is IConversionOperation conversion)
                {
                    value = conversion.Operand;
                }

                if (value.Type?.SpecialType == SpecialType.System_String)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
