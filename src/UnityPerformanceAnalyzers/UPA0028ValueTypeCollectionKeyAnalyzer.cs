using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0028: Reports structs used as hashed collection keys without both
    /// <c>IEquatable&lt;T&gt;</c> and a <c>GetHashCode</c> override.
    ///
    /// <c>EqualityComparer&lt;T&gt;.Default</c> picks its implementation from the type:
    /// a struct implementing <c>IEquatable&lt;T&gt;</c> gets a comparer that calls the typed
    /// Equals, and everything else gets one that calls <c>object.Equals(object)</c> — boxing
    /// both operands per comparison, and falling back to reflection field-by-field when the
    /// struct does not override Equals either. Sandbox measurement across Mono and IL2CPP
    /// confirms both halves: the plain struct allocates, the equatable one does not.
    ///
    /// Not hot-path scoped: this is a property of how the type is used, and a lookup on a
    /// badly-keyed dictionary costs the same wherever it happens.
    /// </summary>
    [UpaClaim(UpaClaimKind.Correctness)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0028ValueTypeCollectionKeyAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0028";

        private static readonly DiagnosticDescriptor RuleMissingBoth = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor RuleMissingEquatable = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            messageFormatKey: Strings.UPA0028MessageFormatEquatable);

        private static readonly DiagnosticDescriptor RuleMissingHashCode = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            messageFormatKey: Strings.UPA0028MessageFormatHashCode);

        // One descriptor per message format, one entry in SupportedDiagnostics. Roslyn matches
        // a reported diagnostic against the declared set by ID, not by descriptor identity, so
        // the alternate formats are supported by the entry below. Verified rather than assumed:
        // emptying this array makes every test in this analyzer's suite fail on an unsupported
        // diagnostic, and the alternate-format tests pass with it as written.
        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(RuleMissingBoth);

        /// <summary>Hashed collections whose key type goes through EqualityComparer&lt;T&gt;.Default.</summary>
        private static readonly string[] s_hashedCollectionMetadataNames =
        {
            "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.HashSet`1",
            "System.Collections.Concurrent.ConcurrentDictionary`2",
        };

        /// <summary>Members that run an equality search over a sequence element by element.</summary>
        private static readonly string[] s_searchingMemberNames = { "Contains", "IndexOf", "Remove" };

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var hashedCollections = ResolveHashedCollections(ctx.Compilation);
            var equatable = ctx.Type("System.IEquatable`1");
            if (hashedCollections.Count == 0 || equatable is null)
            {
                return;
            }

            var listType = ctx.Type("System.Collections.Generic.List`1");
            var arrayType = ctx.Type("System.Array");

            ctx.RegisterOperationAction(
                opCtx => AnalyzeObjectCreation(opCtx, hashedCollections, equatable),
                OperationKind.ObjectCreation);

            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeSearchingCall(nodeCtx, listType, arrayType, equatable),
                SyntaxKind.InvocationExpression);
        }

        private static HashSet<INamedTypeSymbol> ResolveHashedCollections(Compilation compilation)
        {
            return new HashSet<INamedTypeSymbol>(
                WellKnownTypes.Resolve(compilation, s_hashedCollectionMetadataNames),
                SymbolEqualityComparer.Default);
        }

        private static void AnalyzeObjectCreation(
            OperationAnalysisContext context,
            HashSet<INamedTypeSymbol> hashedCollections,
            INamedTypeSymbol equatable)
        {
            var creation = (IObjectCreationOperation)context.Operation;

            // Operations rather than syntax, so that target-typed `new()` is covered: there
            // the type name lives in the declaration and the creation expression has no type
            // syntax at all, which a syntax walk would miss entirely.
            if (!(creation.Type is INamedTypeSymbol type) ||
                !type.IsGenericType ||
                !hashedCollections.Contains(type.OriginalDefinition))
            {
                return;
            }

            var keyType = type.TypeArguments.Length == 0 ? null : type.TypeArguments[0];
            if (!IsProblematicKey(
                    keyType, equatable, requireHashCode: true, out var missingEquatable, out var missingHashCode))
            {
                return;
            }

            // Only creations are reported. A standalone type annotation — a field, parameter,
            // property or return type — says nothing about which comparer the instance uses:
            // the field may be built in a constructor with a custom comparer, the parameter
            // handed one by its caller. Reporting those would be a false positive nobody can
            // act on.
            if (PassesEqualityComparer(creation))
            {
                return;
            }

            Report(context, creation.Syntax.GetLocation(), keyType!, missingEquatable, missingHashCode);
        }

        private static void AnalyzeSearchingCall(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol? listType,
            INamedTypeSymbol? arrayType,
            INamedTypeSymbol equatable)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                as IMethodSymbol;
            if (method is null || !IsSearchingMemberName(method.Name))
            {
                return;
            }

            ITypeSymbol? elementType = null;

            var containingType = method.ContainingType;
            if (listType is object &&
                containingType is object &&
                SymbolEqualityComparer.Default.Equals(containingType.OriginalDefinition, listType))
            {
                elementType = containingType.TypeArguments.Length == 0 ? null : containingType.TypeArguments[0];
            }
            else if (arrayType is object &&
                containingType is object &&
                SymbolEqualityComparer.Default.Equals(containingType, arrayType) &&
                method.TypeArguments.Length == 1)
            {
                elementType = method.TypeArguments[0];
            }

            // Linear search only ever calls Equals, so the hash side is irrelevant here.
            if (!IsProblematicKey(
                    elementType, equatable, requireHashCode: false, out var missingEquatable, out var missingHashCode))
            {
                return;
            }

            ReportAtSyntax(context, invocation.GetLocation(), elementType!, missingEquatable, missingHashCode);
        }

        private static bool IsSearchingMemberName(string name)
        {
            foreach (var candidate in s_searchingMemberNames)
            {
                if (name == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the type is a struct that would fall to the boxing comparer.
        ///
        /// Enums are excluded — measurement shows the BCL has non-boxing comparers for them —
        /// as are type parameters, where the answer depends on the instantiation.
        /// <c>Nullable&lt;T&gt;</c> is unwrapped rather than judged on its own: it does not
        /// implement <c>IEquatable</c> of itself, but the runtime gives it a dedicated
        /// comparer that defers to the underlying type, so <c>Dictionary&lt;int?, V&gt;</c>
        /// costs no more than <c>Dictionary&lt;int, V&gt;</c>.
        ///
        /// <paramref name="requireHashCode"/> is false for linear searches. <c>Contains</c>,
        /// <c>IndexOf</c> and <c>Remove</c> compare elements and never hash them, so demanding
        /// a <c>GetHashCode</c> override there would report a cost that does not exist.
        /// </summary>
        private static bool IsProblematicKey(
            ITypeSymbol? type,
            INamedTypeSymbol equatable,
            bool requireHashCode,
            out bool missingEquatable,
            out bool missingHashCode)
        {
            missingEquatable = false;
            missingHashCode = false;

            if (type is null ||
                type is ITypeParameterSymbol ||
                type.TypeKind != TypeKind.Struct ||
                !type.IsValueType)
            {
                return false;
            }

            if (type is INamedTypeSymbol named &&
                named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                named.TypeArguments.Length == 1)
            {
                return IsProblematicKey(
                    named.TypeArguments[0], equatable, requireHashCode, out missingEquatable, out missingHashCode);
            }

            missingEquatable = !ImplementsEquatableOfSelf(type, equatable);
            missingHashCode = requireHashCode && !OverridesGetHashCode(type);
            return missingEquatable || missingHashCode;
        }

        private static bool ImplementsEquatableOfSelf(ITypeSymbol type, INamedTypeSymbol equatable)
        {
            foreach (var candidate in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, equatable) &&
                    candidate.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverridesGetHashCode(ITypeSymbol type)
        {
            foreach (var member in type.GetMembers("GetHashCode"))
            {
                if (member is IMethodSymbol method && method.IsOverride && method.Parameters.Length == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PassesEqualityComparer(IObjectCreationOperation creation)
        {
            foreach (var argument in creation.Arguments)
            {
                if (!(argument.Parameter?.Type is INamedTypeSymbol parameterType) ||
                    parameterType.OriginalDefinition.Name != "IEqualityComparer")
                {
                    continue;
                }

                var value = OperationFacts.Unwrap(argument.Value);

                // A null or default argument leaves the default comparer in place.
                var isNull = value.ConstantValue.HasValue && value.ConstantValue.Value is null;
                if (isNull || value is IDefaultValueOperation)
                {
                    return false;
                }

                // So does passing it explicitly. EqualityComparer<T>.Default is precisely the
                // comparer this rule warns about; naming it does not make it a different one.
                return !IsDefaultEqualityComparer(value);
            }

            return false;
        }

        private static void Report(
            OperationAnalysisContext context,
            Location location,
            ITypeSymbol keyType,
            bool missingEquatable,
            bool missingHashCode)
        {
            context.ReportDiagnostic(BuildDiagnostic(location, keyType, missingEquatable, missingHashCode));
        }

        private static void ReportAtSyntax(
            SyntaxNodeAnalysisContext context,
            Location location,
            ITypeSymbol keyType,
            bool missingEquatable,
            bool missingHashCode)
        {
            context.ReportDiagnostic(BuildDiagnostic(location, keyType, missingEquatable, missingHashCode));
        }

        private static bool IsDefaultEqualityComparer(IOperation value) =>
            value is IPropertyReferenceOperation property &&
            property.Property.Name == "Default" &&
            property.Property.ContainingType?.OriginalDefinition.Name == "EqualityComparer";

        private static Diagnostic BuildDiagnostic(
            Location location,
            ITypeSymbol keyType,
            bool missingEquatable,
            bool missingHashCode)
        {
            var rule = missingEquatable && missingHashCode
                ? RuleMissingBoth
                : missingEquatable ? RuleMissingEquatable : RuleMissingHashCode;

            var additionalLocations = ImmutableArray<Location>.Empty;
            foreach (var reference in keyType.DeclaringSyntaxReferences)
            {
                additionalLocations = ImmutableArray.Create(Location.Create(
                    reference.SyntaxTree,
                    reference.Span));
                break;
            }

            return UpaDiagnostics.Create(rule, location, additionalLocations, keyType.Name);
        }
    }
}
