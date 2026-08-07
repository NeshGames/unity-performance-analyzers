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
    public sealed class UPA0019BoxedYieldAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0019";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0019Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0019MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: new LocalizableResourceString(Strings.UPA0019Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0019.md");

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
                var monoBehaviourType = ctx.Compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour");
                var enumeratorType = ctx.Compilation.GetTypeByMetadataName("System.Collections.IEnumerator");
                if (monoBehaviourType is null || enumeratorType is null)
                {
                    return;
                }

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeYieldReturn(opCtx, monoBehaviourType, enumeratorType),
                    OperationKind.YieldReturn);
            });
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

            if (!InheritsFrom(context.ContainingSymbol.ContainingType, monoBehaviourType))
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

        private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
        {
            for (var current = type; current is object; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
