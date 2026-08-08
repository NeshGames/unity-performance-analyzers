using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0030: Reports hot-path calls to a closed list of BCL members that allocate on every
    /// call. Whether an arbitrary method allocates needs escape analysis across method
    /// boundaries, which is expensive on this Roslyn version and cannot be made accurate
    /// enough to act on; a curated list trades coverage for zero false positives instead.
    ///
    /// Every entry was measured in the sandbox across Mono and IL2CPP before being listed,
    /// and the list stays closed: entries are added one at a time, after measuring, rather
    /// than by widening a pattern.
    ///
    /// UnityEngine members with the same problem belong to UPA0018 — that split is what keeps
    /// a single call from being reported twice.
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0030KnownAllocatingBclApiAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0030";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <summary>One list entry: a method on a BCL type, what it allocates, and the advice.</summary>
        private readonly struct KnownAllocatingMethod
        {
            public KnownAllocatingMethod(
                INamedTypeSymbol containingType,
                string methodName,
                string allocatedThing,
                string advice)
            {
                ContainingType = containingType;
                MethodName = methodName;
                AllocatedThing = allocatedThing;
                Advice = advice;
            }

            public INamedTypeSymbol ContainingType { get; }
            public string MethodName { get; }
            public string AllocatedThing { get; }
            public string Advice { get; }
        }

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var known = ResolveList(ctx.Compilation);
            if (known.IsEmpty)
            {
                return;
            }

            var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, known, hotPathDetector),
                OperationKind.Invocation);
        }

        private static ImmutableArray<KnownAllocatingMethod> ResolveList(Compilation compilation)
        {
            var builder = ImmutableArray.CreateBuilder<KnownAllocatingMethod>();

            const string spanAdvice = "Use ReadOnlySpan<char> slicing, or cache the result.";
            const string caseAdvice =
                "Compare with StringComparison.OrdinalIgnoreCase instead of changing case.";
            const string trimAdvice =
                "Use ReadOnlySpan<char>.Trim, or move the call off the hot path. Note that this "
                + "call allocates only when there is something to trim.";

            Add(builder, compilation, "System.String", "Split", "array of strings", spanAdvice);
            Add(builder, compilation, "System.String", "ToCharArray", "char[]", spanAdvice);
            Add(builder, compilation, "System.String", "Substring", "string", spanAdvice);
            Add(builder, compilation, "System.String", "ToLower", "string", caseAdvice);
            Add(builder, compilation, "System.String", "ToLowerInvariant", "string", caseAdvice);
            Add(builder, compilation, "System.String", "ToUpper", "string", caseAdvice);
            Add(builder, compilation, "System.String", "ToUpperInvariant", "string", caseAdvice);
            Add(builder, compilation, "System.String", "Trim", "string", trimAdvice);
            Add(builder, compilation, "System.String", "TrimStart", "string", trimAdvice);
            Add(builder, compilation, "System.String", "TrimEnd", "string", trimAdvice);

            const string enumAdvice = "Cache the array in a static readonly field.";
            Add(builder, compilation, "System.Enum", "GetValues", "array", enumAdvice);
            Add(builder, compilation, "System.Enum", "GetNames", "string[]", enumAdvice);

            return builder.ToImmutable();
        }

        private static void Add(
            ImmutableArray<KnownAllocatingMethod>.Builder builder,
            Compilation compilation,
            string metadataName,
            string methodName,
            string allocatedThing,
            string advice)
        {
            var type = compilation.GetTypeByMetadataName(metadataName);
            if (type is object)
            {
                builder.Add(new KnownAllocatingMethod(type, methodName, allocatedThing, advice));
            }
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            ImmutableArray<KnownAllocatingMethod> known,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            var entryIndex = -1;
            for (var i = 0; i < known.Length; i++)
            {
                if (method.Name == known[i].MethodName &&
                    SymbolEqualityComparer.Default.Equals(method.ContainingType, known[i].ContainingType))
                {
                    entryIndex = i;
                    break;
                }
            }

            if (entryIndex < 0)
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(invocation, context.CancellationToken))
            {
                return;
            }

            var entry = known[entryIndex];
            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                entry.ContainingType.Name + "." + entry.MethodName,
                entry.AllocatedThing,
                entry.Advice));
        }
    }
}
