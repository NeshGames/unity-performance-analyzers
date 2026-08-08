using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0027: Reports hot-path calls that resolve to a <c>params</c> overload in expanded
    /// form, where the compiler creates the array at the call site. The allocation is real but
    /// invisible in source — <c>Mathf.Max(a, b, c)</c> looks like <c>Mathf.Max(a, b)</c> and
    /// costs a <c>float[]</c> per call, because Unity only ships arity-2 overloads.
    ///
    /// When the parameter is <c>params object[]</c> and value-type arguments go into it, each
    /// one also boxes; the message says how many. UPA0006 deliberately leaves both the array
    /// and those conversions alone so one cost yields one diagnostic.
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0027ParamsArrayAllocationAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0027";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor BoxingRule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            messageFormatKey: Strings.UPA0027MessageFormatBoxing);

        // One descriptor per message format, one entry in SupportedDiagnostics. Roslyn matches
        // a reported diagnostic against the declared set by ID, not by descriptor identity, so
        // the alternate formats are supported by the entry below. Verified rather than assumed:
        // emptying this array makes every test in this analyzer's suite fail on an unsupported
        // diagnostic, and the alternate-format tests pass with it as written.
        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, hotPathDetector),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context, HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;

            var arrayCreation = FindExpandedParamsArray(invocation);
            if (arrayCreation is null)
            {
                return;
            }

            // A zero-argument params call compiles to Array.Empty<T>(), which allocates
            // nothing — the implicit creation is still in the tree, so the count is what
            // separates a real allocation from a free one.
            var elementCount = arrayCreation.Initializer?.ElementValues.Length ?? 0;
            if (elementCount == 0)
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(invocation, context.CancellationToken))
            {
                return;
            }

            var methodName = DescribeMethod(invocation.TargetMethod);
            var boxedCount = CountBoxedElements(arrayCreation);

            if (boxedCount > 0)
            {
                context.ReportDiagnostic(UpaDiagnostics.Create(
                    BoxingRule,
                    invocation.Syntax.GetLocation(),
                    methodName,
                    boxedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                return;
            }

            var elementType = (arrayCreation.Type as IArrayTypeSymbol)?.ElementType;
            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                methodName,
                elementType?.ToDisplayString() ?? "element"));
        }

        /// <summary>
        /// Returns the array the compiler synthesized for an expanded-form params call, or
        /// null when this call is not one. The implicit flag is the reliable signal: passing
        /// an existing array (normal form) produces the same parameter shape but leaves the
        /// creation explicit, and comparing argument counts would misread both.
        /// </summary>
        private static IArrayCreationOperation? FindExpandedParamsArray(IInvocationOperation invocation)
        {
            var parameters = invocation.TargetMethod.Parameters;
            if (parameters.Length == 0 || !parameters[parameters.Length - 1].IsParams)
            {
                return null;
            }

            var paramsParameter = parameters[parameters.Length - 1];
            foreach (var argument in invocation.Arguments)
            {
                if (!SymbolEqualityComparer.Default.Equals(argument.Parameter, paramsParameter))
                {
                    continue;
                }

                return argument.Value is IArrayCreationOperation arrayCreation && arrayCreation.IsImplicit
                    ? arrayCreation
                    : null;
            }

            return null;
        }

        /// <summary>
        /// Counts the elements that box on their way into the array. Only params object[]
        /// (and interface-typed params) can do this; a params float[] holds its values
        /// directly.
        /// </summary>
        private static int CountBoxedElements(IArrayCreationOperation arrayCreation)
        {
            var initializer = arrayCreation.Initializer;
            if (initializer is null)
            {
                return 0;
            }

            var count = 0;
            foreach (var element in initializer.ElementValues)
            {
                if (element is IConversionOperation conversion && conversion.GetConversion().IsBoxing)
                {
                    count++;
                }
            }

            return count;
        }

        private static string DescribeMethod(IMethodSymbol method) =>
            method.ContainingType is null ? method.Name : method.ContainingType.Name + "." + method.Name;
    }
}
