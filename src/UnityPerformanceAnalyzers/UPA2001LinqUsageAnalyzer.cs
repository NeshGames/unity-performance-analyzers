using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2001: Reports calls to extension methods declared on System.Linq.Enumerable or
    /// System.Linq.Queryable on per-frame hot paths, at the call site — query syntax
    /// compiles into the same methods and triggers too. Same-named extensions on user types
    /// are excluded by declaring-type comparison. An opinionated, off-by-default rule for
    /// low-allocation codebases (docs/rules/UPA2001.md).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2001LinqUsageAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2001";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2001Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2001MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2001Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2001.md");

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
                var enumerableType = ctx.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
                var queryableType = ctx.Compilation.GetTypeByMetadataName("System.Linq.Queryable");
                if (enumerableType is null && queryableType is null)
                {
                    return;
                }

                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, enumerableType, queryableType, hotPathDetector),
                    OperationKind.Invocation);
            });
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? enumerableType,
            INamedTypeSymbol? queryableType,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            var containingType = method.ContainingType;

            var isLinq =
                (enumerableType is object && SymbolEqualityComparer.Default.Equals(containingType, enumerableType)) ||
                (queryableType is object && SymbolEqualityComparer.Default.Equals(containingType, queryableType));
            if (!isLinq)
            {
                return;
            }

            var semanticModel = invocation.SemanticModel;
            if (semanticModel is null ||
                !hotPathDetector.IsInHotPath(invocation.Syntax, semanticModel, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                GetReportLocation(invocation.Syntax),
                $"{containingType.Name}.{method.Name}"));
        }

        // Chained calls nest syntactically (list.Where(...).ToList() spans the whole chain);
        // reporting on the method name keeps each diagnostic distinct and readable.
        private static Location GetReportLocation(SyntaxNode syntax)
        {
            if (syntax is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.GetLocation();
            }

            return syntax.GetLocation();
        }
    }
}
