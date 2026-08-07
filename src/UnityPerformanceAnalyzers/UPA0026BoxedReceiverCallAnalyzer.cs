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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0026BoxedReceiverCallAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0026";

        // RS1032 objects to a format string ending in a placeholder; here the final
        // sentence (the advice, itself period-terminated) arrives as a localized argument.
#pragma warning disable RS1032
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0026Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0026MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0026Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0026.md");
#pragma warning restore RS1032

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, hotPathDetector),
                    OperationKind.Invocation);
            });
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

            var semanticModel = invocation.SemanticModel;
            if (semanticModel is null ||
                !hotPathDetector.IsInHotPath(invocation.Syntax, semanticModel, context.CancellationToken))
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
