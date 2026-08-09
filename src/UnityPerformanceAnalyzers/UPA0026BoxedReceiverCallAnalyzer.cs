using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0026: Reports hot-path calls that box a value-type receiver because they resolve to
    /// a method inherited from <c>Object</c>/<c>ValueType</c>/<c>Enum</c> —
    /// <c>ToString</c>/<c>GetHashCode</c>/<c>Equals(object)</c> the value type does not
    /// override, plus <c>GetType()</c> (never overridable, always boxes a value type). Types
    /// that override the member resolve to their own implementation and are naturally
    /// excluded; type-parameter receivers are skipped because boxing there depends on the
    /// instantiation. The .NET 5+ optimizations that remove some of this boxing do not apply
    /// to Unity's Mono runtime.
    /// </summary>
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

            var advice = method.Name == "GetType"
                ? Strings.ResourceManager.GetString(Strings.UPA0026AdviceGetType)
                : receiverType.TypeKind == TypeKind.Enum
                    ? Strings.ResourceManager.GetString(Strings.UPA0026AdviceEnum)
                    : Strings.ResourceManager.GetString(Strings.UPA0026AdviceStruct);

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                method.Name,
                method.ContainingType.Name,
                advice));
        }

        /// <summary>
        /// True for ToString()/GetHashCode()/Equals(object)/GetType() declared on
        /// Object, ValueType, or Enum. A value type that overrides one of these resolves to
        /// its own declaration, whose containing type is the value type itself — so matching
        /// on the containing type excludes overridden members without extra work.
        /// </summary>
        private static bool IsInheritedObjectMethod(IMethodSymbol method)
        {
            var containerSpecialType = method.ContainingType.SpecialType;
            if (containerSpecialType != SpecialType.System_Object &&
                containerSpecialType != SpecialType.System_ValueType &&
                containerSpecialType != SpecialType.System_Enum)
            {
                return false;
            }

            switch (method.Name)
            {
                case "ToString":
                case "GetHashCode":
                case "GetType":
                    return method.Parameters.IsEmpty;
                case "Equals":
                    return method.Parameters.Length == 1 &&
                        method.Parameters[0].Type.SpecialType == SpecialType.System_Object;
                default:
                    return false;
            }
        }
    }
}
