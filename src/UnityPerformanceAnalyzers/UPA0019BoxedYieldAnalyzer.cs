using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0019: Reports <c>yield return</c> of a value type from a MonoBehaviour coroutine
    /// (a method returning the non-generic <c>IEnumerator</c>). The value is boxed on every
    /// resume and Unity treats any boxed non-YieldInstruction value the same as <c>null</c>,
    /// so <c>yield return null</c> is the allocation-free equivalent. Enumerator methods on
    /// non-MonoBehaviour types are not reported — there the yielded values carry meaning.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0019BoxedYieldAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0019";

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
            var monoBehaviourType = ctx.Type("UnityEngine.MonoBehaviour");
            var enumeratorType = ctx.Type("System.Collections.IEnumerator");
            if (monoBehaviourType is null || enumeratorType is null)
            {
                return;
            }

            ctx.RegisterOperationAction(
                opCtx => AnalyzeYieldReturn(opCtx, monoBehaviourType, enumeratorType),
                OperationKind.YieldReturn);
        }

        private static void AnalyzeYieldReturn(
            OperationAnalysisContext context,
            INamedTypeSymbol monoBehaviourType,
            INamedTypeSymbol enumeratorType)
        {
            if (!(context.ContainingSymbol is IMethodSymbol method) ||
                !SymbolEqualityComparer.Default.Equals(method.ReturnType, enumeratorType))
            {
                return;
            }

            if (!TypeHierarchy.DerivesFrom(context.ContainingSymbol.ContainingType, monoBehaviourType))
            {
                return;
            }

            var returned = ((IReturnOperation)context.Operation).ReturnedValue;
            if (!(returned is IConversionOperation conversion) ||
                conversion.Type?.SpecialType != SpecialType.System_Object ||
                conversion.Operand.Type?.IsValueType != true)
            {
                return;
            }

            var operandSyntax = conversion.Operand.Syntax;
            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                operandSyntax.GetLocation(),
                operandSyntax.ToString()));
        }

    }
}
