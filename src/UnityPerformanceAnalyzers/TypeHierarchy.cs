using Microsoft.CodeAnalysis;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// The shared base-type walk. Compares by OriginalDefinition, so constructed generics
    /// match their definition; a type-parameter input resolves through its constraints
    /// (whether an unconstrained parameter's instantiation derives is unknowable, so it
    /// reports false). Replaces the per-analyzer private copies of this loop.
    /// </summary>
    internal static class TypeHierarchy
    {
        public static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
        {
            if (baseType is null)
            {
                return false;
            }

            if (type is ITypeParameterSymbol typeParameter)
            {
                foreach (var constraint in typeParameter.ConstraintTypes)
                {
                    if (DerivesFrom(constraint, baseType))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (var current = type; current is object; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
