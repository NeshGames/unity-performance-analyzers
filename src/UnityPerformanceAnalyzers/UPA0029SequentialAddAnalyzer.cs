using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0029: Reports loops that copy a collection into another one element by element,
    /// where <c>AddRange</c> would size the destination once.
    ///
    /// <c>List&lt;T&gt;</c> grows by doubling, so adding N elements one at a time can
    /// reallocate and copy the backing array O(log N) times. <c>AddRange</c> reserves the
    /// space up front — but only when the source implements <c>ICollection&lt;T&gt;</c>, since
    /// that is what lets it ask how many elements are coming. For a plain
    /// <c>IEnumerable&lt;T&gt;</c> (a LINQ query, an iterator) AddRange falls back to adding
    /// one at a time and there is nothing to gain, so this rule checks for that interface
    /// rather than suggesting the swap unconditionally.
    ///
    /// Global by default; <c>upa_addrange_hot_path_only</c> narrows it to hot paths for
    /// projects that only care there.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0029SequentialAddAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0029";

        /// <summary>Option key that narrows this rule to per-frame code.</summary>
        internal const string HotPathOnlyOptionKey = "upa_addrange_hot_path_only";

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
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var collectionInterface =
                ctx.Compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1");
            var enumerableInterface =
                ctx.Compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
            if (collectionInterface is null || enumerableInterface is null)
            {
                return;
            }

            var hotPathOnly = ReadHotPathOnlyOption(ctx.Compilation, ctx.Options);
            var hotPathDetector = hotPathOnly ? HotPathDetector.Create(ctx.Compilation, ctx.Options) : null;

            ctx.RegisterOperationAction(
                opCtx => AnalyzeLoop(opCtx, collectionInterface, enumerableInterface, hotPathDetector),
                OperationKind.Loop);
        }

        private static void AnalyzeLoop(
            OperationAnalysisContext context,
            INamedTypeSymbol collectionInterface,
            INamedTypeSymbol enumerableInterface,
            HotPathDetector? hotPathDetector)
        {
            switch (context.Operation)
            {
                case IForEachLoopOperation:
                    AnalyzeForEach(context, collectionInterface, enumerableInterface, hotPathDetector);
                    break;
                case IForLoopOperation forLoop:
                    AnalyzeIndexedFor(context, forLoop, collectionInterface, enumerableInterface, hotPathDetector);
                    break;
            }
        }

        private static bool ReadHotPathOnlyOption(Compilation compilation, AnalyzerOptions analyzerOptions)
        {
            return UpaOptions.Resolve(analyzerOptions).GetBool(
                HotPathOnlyOptionKey,
                compilation.SyntaxTrees.FirstOrDefault(),
                analyzerOptions.AnalyzerConfigOptionsProvider,
                fallback: false);
        }

        private static void AnalyzeForEach(
            OperationAnalysisContext context,
            INamedTypeSymbol collectionInterface,
            INamedTypeSymbol enumerableInterface,
            HotPathDetector? hotPathDetector)
        {
            if (!(context.Operation is IForEachLoopOperation loop))
            {
                return;
            }

            var addCall = FindSoleAddCall(loop.Body);
            if (addCall is null)
            {
                return;
            }

            if (!AddsTheLoopVariableUnchanged(addCall, loop))
            {
                return;
            }

            var targetType = addCall.Instance?.Type as INamedTypeSymbol;
            if (targetType is null || !HasAddRangeTakingEnumerable(targetType, enumerableInterface))
            {
                return;
            }

            // The whole point of the suggestion: AddRange can only pre-size when it can count
            // the source, which is what ICollection<T> provides.
            var source = Unwrap(loop.Collection);
            var sourceType = source?.Type;
            if (sourceType is null || !ImplementsCollectionInterface(sourceType, collectionInterface))
            {
                return;
            }

            if (!IsRewritableCopy(addCall.Instance, source))
            {
                return;
            }

            if (hotPathDetector is object && hotPathDetector.IsOutsideHotPath(loop, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(Rule, loop.Syntax.GetLocation()));
        }

        /// <summary>
        /// The indexed equivalent: <c>for (int i = 0; i &lt; source.Count; i++)
        /// target.Add(source[i]);</c>. Narrower than the foreach form on purpose — the
        /// bounds, the step and the single use of the index all have to line up, or the loop
        /// is doing something AddRange would not reproduce.
        /// </summary>
        private static void AnalyzeIndexedFor(
            OperationAnalysisContext context,
            IForLoopOperation loop,
            INamedTypeSymbol collectionInterface,
            INamedTypeSymbol enumerableInterface,
            HotPathDetector? hotPathDetector)
        {
            var indexSymbol = GetZeroInitializedIndex(loop);
            if (indexSymbol is null || !IsSimpleIncrementOf(loop, indexSymbol))
            {
                return;
            }

            var addCall = FindSoleAddCall(loop.Body);
            if (addCall is null)
            {
                return;
            }

            var argument = Unwrap(addCall.Arguments[0].Value);
            if (!(argument is IArrayElementReferenceOperation || argument is IPropertyReferenceOperation))
            {
                return;
            }

            var source = argument is IArrayElementReferenceOperation arrayElement
                ? arrayElement.ArrayReference
                : ((IPropertyReferenceOperation)argument).Instance;
            var indexArguments = argument is IArrayElementReferenceOperation element
                ? element.Indices
                : ((IPropertyReferenceOperation)argument).Arguments.Select(a => a.Value).ToImmutableArray();

            if (source?.Type is null ||
                indexArguments.Length != 1 ||
                !IsReferenceTo(indexArguments[0], indexSymbol))
            {
                return;
            }

            if (!ConditionBoundsBySourceLength(loop.Condition, indexSymbol, source))
            {
                return;
            }

            var targetType = addCall.Instance?.Type as INamedTypeSymbol;
            if (targetType is null ||
                !HasAddRangeTakingEnumerable(targetType, enumerableInterface) ||
                !ImplementsCollectionInterface(source.Type, collectionInterface))
            {
                return;
            }

            if (!IsRewritableCopy(addCall.Instance, source))
            {
                return;
            }

            if (hotPathDetector is object && hotPathDetector.IsOutsideHotPath(loop, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(Rule, loop.Syntax.GetLocation()));
        }

        private static ILocalSymbol? GetZeroInitializedIndex(IForLoopOperation loop)
        {
            if (loop.Before.Length != 1 ||
                !(loop.Before[0] is IVariableDeclarationGroupOperation group) ||
                group.Declarations.Length != 1 ||
                group.Declarations[0].Declarators.Length != 1)
            {
                return null;
            }

            var declarator = group.Declarations[0].Declarators[0];
            var initializer = declarator.Initializer?.Value;
            return initializer is ILiteralOperation literal &&
                literal.ConstantValue.HasValue &&
                literal.ConstantValue.Value is int start &&
                start == 0
                ? declarator.Symbol
                : null;
        }

        private static bool IsSimpleIncrementOf(IForLoopOperation loop, ILocalSymbol index)
        {
            if (loop.AtLoopBottom.Length != 1 ||
                !(loop.AtLoopBottom[0] is IExpressionStatementOperation statement) ||
                !(statement.Operation is IIncrementOrDecrementOperation increment) ||
                increment.Kind != OperationKind.Increment)
            {
                return false;
            }

            return IsReferenceTo(increment.Target, index);
        }

        /// <summary>
        /// True when the condition is <c>index &lt; source.Count</c> (or Length) for the same
        /// source the body indexes into. A different bound means the loop is not a full copy.
        /// </summary>
        private static bool ConditionBoundsBySourceLength(
            IOperation? condition,
            ILocalSymbol index,
            IOperation source)
        {
            if (!(condition is IBinaryOperation binary) ||
                binary.OperatorKind != BinaryOperatorKind.LessThan ||
                !IsReferenceTo(binary.LeftOperand, index))
            {
                return false;
            }

            var right = Unwrap(binary.RightOperand);
            var boundInstance = right switch
            {
                // Array.Length reaches here as an ordinary property reference too.
                IPropertyReferenceOperation property when property.Property.Name == "Count" => property.Instance,
                IPropertyReferenceOperation property when property.Property.Name == "Length" => property.Instance,
                _ => null,
            };

            if (boundInstance is null)
            {
                return false;
            }

            // The bound and the element access must be the same collection, receiver chain
            // included: `left.Items.Count` bounding a loop over `right.Items[i]` copies a
            // different number of elements than AddRange would, and comparing only the
            // terminal member would call those two the same source.
            var boundChain = TryGetReferenceChain(boundInstance);
            var sourceChain = TryGetReferenceChain(source);
            return boundChain is object && sourceChain is object && ChainsMatch(boundChain, sourceChain);
        }

        /// <summary>
        /// Decides whether replacing the loop with a single AddRange would mean the same
        /// thing. Two conditions have to hold, and neither can be waved through:
        ///
        /// Both sides must be stable references. A target like <c>GetTarget().Add(x)</c> is
        /// re-evaluated per iteration and may hand back a different collection each time; the
        /// rewrite would call it once. The same reasoning applies to an indexed source, whose
        /// bound and element access are both evaluated per iteration.
        ///
        /// And they must not be the same collection. <c>foreach (var x in items)
        /// items.Add(x)</c> throws today, and its indexed form never terminates —
        /// <c>items.AddRange(items)</c> quietly does neither. Turning broken code into working
        /// code is still a behaviour change, and not one an automatic fix should make.
        ///
        /// Two distinct references can still alias at runtime, which no amount of symbol
        /// comparison will catch. That case is already broken in the same way, and the rule
        /// says so in its documentation rather than pretending otherwise.
        /// </summary>
        private static bool IsRewritableCopy(IOperation? target, IOperation? source)
        {
            var targetChain = TryGetReferenceChain(target);
            var sourceChain = TryGetReferenceChain(source);

            return targetChain is object &&
                sourceChain is object &&
                !ChainsMatch(targetChain, sourceChain);
        }

        /// <summary>
        /// Returns the symbols identifying a reference expression, outermost member first, or
        /// null when the expression is not a stable reference.
        ///
        /// Locals, parameters, fields and <c>this</c> qualify. Everything else — invocations,
        /// indexers, and <em>properties</em> — ends the walk with null. Properties look stable
        /// and are not: a getter can return a different collection on every access, or have
        /// side effects, and nothing in the symbol says whether it is an auto-property or
        /// computed. That excludes the common auto-property collection, which is a real loss
        /// of coverage — but a rule that offers a rewrite has to be right about it, and there
        /// is no way to tell the two apart for a property that came from metadata.
        /// </summary>
        private static List<ISymbol>? TryGetReferenceChain(IOperation? operation)
        {
            var chain = new List<ISymbol>();
            var current = Unwrap(operation!);

            while (true)
            {
                switch (current)
                {
                    case ILocalReferenceOperation local:
                        chain.Add(local.Local);
                        return chain;

                    case IParameterReferenceOperation parameter:
                        chain.Add(parameter.Parameter);
                        return chain;

                    case IInstanceReferenceOperation:
                        return chain;

                    case IFieldReferenceOperation field:
                        chain.Add(field.Field);
                        if (field.Field.IsStatic)
                        {
                            return chain;
                        }

                        if (field.Instance is null)
                        {
                            return null;
                        }

                        current = Unwrap(field.Instance);
                        break;

                    default:
                        return null;
                }
            }
        }

        private static bool ChainsMatch(List<ISymbol> left, List<ISymbol> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsReferenceTo(IOperation operation, ILocalSymbol symbol) =>
            Unwrap(operation) is ILocalReferenceOperation local &&
            SymbolEqualityComparer.Default.Equals(local.Local, symbol);

        private static IOperation Unwrap(IOperation operation)
        {
            while (operation is IConversionOperation conversion && conversion.IsImplicit)
            {
                operation = conversion.Operand;
            }

            return operation;
        }

        /// <summary>
        /// Returns the single Add invocation that makes up the loop body, or null when the
        /// body does anything else. A body wrapped in braces is the same shape.
        /// </summary>
        private static IInvocationOperation? FindSoleAddCall(IOperation? body)
        {
            if (body is IBlockOperation block)
            {
                if (block.Operations.Length != 1)
                {
                    return null;
                }

                body = block.Operations[0];
            }

            if (!(body is IExpressionStatementOperation statement) ||
                !(statement.Operation is IInvocationOperation invocation) ||
                invocation.TargetMethod.Name != "Add" ||
                invocation.Arguments.Length != 1 ||
                invocation.Instance is null)
            {
                return null;
            }

            return invocation;
        }

        /// <summary>
        /// True when the argument is the loop variable itself. Anything else — a projection,
        /// a member access, a cast — changes the meaning, and AddRange would not reproduce it.
        /// </summary>
        private static bool AddsTheLoopVariableUnchanged(IInvocationOperation addCall, IForEachLoopOperation loop)
        {
            var argument = addCall.Arguments[0].Value;
            while (argument is IConversionOperation conversion && conversion.IsImplicit)
            {
                argument = conversion.Operand;
            }

            if (!(argument is ILocalReferenceOperation localReference))
            {
                return false;
            }

            return loop.LoopControlVariable is IVariableDeclaratorOperation declarator &&
                SymbolEqualityComparer.Default.Equals(declarator.Symbol, localReference.Local);
        }

        private static bool HasAddRangeTakingEnumerable(
            INamedTypeSymbol targetType,
            INamedTypeSymbol enumerableInterface)
        {
            foreach (var member in targetType.GetMembers("AddRange"))
            {
                if (member is IMethodSymbol method &&
                    method.Parameters.Length == 1 &&
                    method.Parameters[0].Type is INamedTypeSymbol parameterType &&
                    SymbolEqualityComparer.Default.Equals(parameterType.OriginalDefinition, enumerableInterface))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ImplementsCollectionInterface(
            ITypeSymbol sourceType,
            INamedTypeSymbol collectionInterface)
        {
            // Arrays implement ICollection<T> at runtime, but the symbol model does not list
            // it among AllInterfaces for every compilation, so treat them explicitly.
            if (sourceType is IArrayTypeSymbol)
            {
                return true;
            }

            if (sourceType is INamedTypeSymbol named &&
                SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, collectionInterface))
            {
                return true;
            }

            foreach (var candidate in sourceType.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, collectionInterface))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
