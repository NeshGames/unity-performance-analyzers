using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var hashedCollections = ResolveHashedCollections(ctx.Compilation);
            var equatable = ctx.Compilation.GetTypeByMetadataName("System.IEquatable`1");
            if (hashedCollections.Count == 0 || equatable is null)
            {
                return;
            }

            var listType = ctx.Compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            var arrayType = ctx.Compilation.GetTypeByMetadataName("System.Array");

            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeGenericName(nodeCtx, hashedCollections, equatable),
                SyntaxKind.GenericName);

            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeSearchingCall(nodeCtx, listType, arrayType, equatable),
                SyntaxKind.InvocationExpression);
        }

        private static HashSet<INamedTypeSymbol> ResolveHashedCollections(Compilation compilation)
        {
            var resolved = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var metadataName in s_hashedCollectionMetadataNames)
            {
                var type = compilation.GetTypeByMetadataName(metadataName);
                if (type is object)
                {
                    resolved.Add(type);
                }
            }

            return resolved;
        }

        private static void AnalyzeGenericName(
            SyntaxNodeAnalysisContext context,
            HashSet<INamedTypeSymbol> hashedCollections,
            INamedTypeSymbol equatable)
        {
            var genericName = (GenericNameSyntax)context.Node;

            // A qualified name reaches here through its right-hand side too; analyzing the
            // inner node once is enough.
            if (genericName.Parent is QualifiedNameSyntax qualified && qualified.Left == genericName)
            {
                return;
            }

            var type = context.SemanticModel.GetSymbolInfo(genericName, context.CancellationToken).Symbol
                as INamedTypeSymbol;
            if (type is null ||
                !type.IsGenericType ||
                !hashedCollections.Contains(type.OriginalDefinition))
            {
                return;
            }

            var keyType = type.TypeArguments.Length == 0 ? null : type.TypeArguments[0];
            if (!IsProblematicKey(keyType, equatable, out var missingEquatable, out var missingHashCode))
            {
                return;
            }

            var creation = FindOwningObjectCreation(genericName);
            if (creation is object)
            {
                // The creation is where a comparer can be passed, so it decides.
                if (PassesEqualityComparer(context.SemanticModel, creation, context.CancellationToken))
                {
                    return;
                }
            }
            else if (IsDeclarationTypeWithCreationInitializer(genericName))
            {
                // The creation on the same line reports instead — one collection, one
                // diagnostic, and it lands where the fix goes.
                return;
            }

            Report(context, genericName.GetLocation(), keyType!, missingEquatable, missingHashCode);
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

            if (!IsProblematicKey(elementType, equatable, out var missingEquatable, out var missingHashCode))
            {
                return;
            }

            Report(context, invocation.GetLocation(), elementType!, missingEquatable, missingHashCode);
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
        /// True when the type is a struct that would fall to the boxing comparer. Enums are
        /// excluded — measurement shows the BCL has non-boxing comparers for them — as are
        /// type parameters, where the answer depends on the instantiation.
        /// </summary>
        private static bool IsProblematicKey(
            ITypeSymbol? type,
            INamedTypeSymbol equatable,
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

            missingEquatable = !ImplementsEquatableOfSelf(type, equatable);
            missingHashCode = !OverridesGetHashCode(type);
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

        private static ObjectCreationExpressionSyntax? FindOwningObjectCreation(GenericNameSyntax genericName)
        {
            var node = (SyntaxNode)genericName;
            while (node.Parent is QualifiedNameSyntax qualified && qualified.Right == node)
            {
                node = qualified;
            }

            return node.Parent as ObjectCreationExpressionSyntax;
        }

        private static bool PassesEqualityComparer(
            SemanticModel semanticModel,
            ObjectCreationExpressionSyntax creation,
            System.Threading.CancellationToken cancellationToken)
        {
            var constructor = semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol;
            if (constructor is null)
            {
                return false;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Type is INamedTypeSymbol named &&
                    named.OriginalDefinition.Name == "IEqualityComparer")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when this generic name is the declared type of a field, property or local
        /// whose initializer constructs the collection — the creation reports instead.
        /// </summary>
        private static bool IsDeclarationTypeWithCreationInitializer(GenericNameSyntax genericName)
        {
            var node = (SyntaxNode)genericName;
            while (node.Parent is QualifiedNameSyntax qualified && qualified.Right == node)
            {
                node = qualified;
            }

            switch (node.Parent)
            {
                case VariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Variables)
                    {
                        if (declarator.Initializer?.Value is ObjectCreationExpressionSyntax)
                        {
                            return true;
                        }
                    }

                    return false;

                case PropertyDeclarationSyntax property:
                    return property.Initializer?.Value is ObjectCreationExpressionSyntax;

                default:
                    return false;
            }
        }

        private static void Report(
            SyntaxNodeAnalysisContext context,
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

            context.ReportDiagnostic(UpaDiagnostics.Create(
                rule,
                location,
                additionalLocations,
                keyType.Name));
        }
    }
}
