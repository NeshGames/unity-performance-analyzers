using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0026: Reports hot-path <c>GetType()</c> calls on a value-type receiver. The method
    /// is declared on <c>Object</c> and cannot be overridden, so the receiver is boxed to
    /// reach it — and the answer was known when the code was compiled.
    /// </summary>
    /// <remarks>
    /// The rule used to name <c>ToString</c>, <c>GetHashCode</c> and <c>Equals(object)</c>
    /// as well. Measured on IL2CPP with the argument boxed once outside the loop, so the
    /// receiver was the only thing left that could allocate, all three read 0.00 B/op on
    /// both supported editors: there is no receiver box to report. What did allocate in the
    /// Equals case was the argument, which is a different conversion in a different place,
    /// and UPA0006 reports it there.
    ///
    /// Type-parameter receivers are still skipped: whether a box happens depends on the
    /// instantiation, which one method body cannot see.
    /// </remarks>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0026BoxedReceiverCallAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0026";

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
            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context, HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            if (!IsInheritedObjectMethod(method))
            {
                return;
            }

            var receiverType = invocation.Instance?.Type;
            if (receiverType is null ||
                !receiverType.IsValueType ||
                receiverType is ITypeParameterSymbol)
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(invocation, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation()));
        }

        /// <summary>
        /// True for <c>GetType()</c> as declared on <c>Object</c>. The containing-type check
        /// is kept even though <c>GetType</c> cannot be overridden, so that a user-defined
        /// method of the same name on some other type is not matched by name alone.
        /// </summary>
        private static bool IsInheritedObjectMethod(IMethodSymbol method)
        {
            return method.Name == "GetType" &&
                method.Parameters.IsEmpty &&
                method.ContainingType.SpecialType == SpecialType.System_Object;
        }
    }
}
