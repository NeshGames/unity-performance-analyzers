using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0020: Reports <c>new WaitUntil(...)</c>/<c>new WaitWhile(...)</c> constructions whose
    /// predicate argument is a lambda or anonymous method. Each construction allocates the
    /// delegate and any captured closure; Unity's coroutine guidance recommends caching the
    /// yield instruction and its predicate in fields. No flow analysis is performed, so a
    /// construction that provably runs once still reports — disabled by default.
    /// </summary>
    [UpaClaim(UpaClaimKind.PerFrameCost)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0020WaitPredicateLambdaAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0020";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var waitUntilType = ctx.Type("UnityEngine.WaitUntil");
            var waitWhileType = ctx.Type("UnityEngine.WaitWhile");
            if (waitUntilType is null && waitWhileType is null)
            {
                return;
            }

            ctx.RegisterOperationAction(
                opCtx => AnalyzeObjectCreation(opCtx, waitUntilType, waitWhileType),
                OperationKind.ObjectCreation);
        }

        private static void AnalyzeObjectCreation(
            OperationAnalysisContext context,
            INamedTypeSymbol? waitUntilType,
            INamedTypeSymbol? waitWhileType)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            var createdType = creation.Type;

            if (!SymbolEqualityComparer.Default.Equals(createdType, waitUntilType) &&
                !SymbolEqualityComparer.Default.Equals(createdType, waitWhileType))
            {
                return;
            }

            if (!HasAnonymousFunctionArgument(creation))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                creation.Syntax.GetLocation(),
                createdType!.Name));
        }

        private static bool HasAnonymousFunctionArgument(IObjectCreationOperation creation)
        {
            foreach (var argument in creation.Arguments)
            {
                var value = OperationFacts.Unwrap(argument.Value);

                if (value is IDelegateCreationOperation delegateCreation &&
                    delegateCreation.Target is IAnonymousFunctionOperation)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
