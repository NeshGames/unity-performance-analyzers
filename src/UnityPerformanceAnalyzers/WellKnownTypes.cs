using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Resolving a list of metadata names against a compilation, with the ones that are not
    /// referenced dropped.
    /// </summary>
    /// <remarks>
    /// Four rules had their own copy of the same five lines. The queries they build on top
    /// differ enough that their symbol tables stay separate — one returns a descriptor to pick
    /// between four ids, another branches on member kind — but the step that turns names into
    /// symbols is the same everywhere, and a missing type is never an error here: a package
    /// that is not referenced simply has nothing for the rule to match.
    /// </remarks>
    internal static class WellKnownTypes
    {
        public static ImmutableArray<INamedTypeSymbol> Resolve(
            Compilation compilation,
            IEnumerable<string> metadataNames)
        {
            var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            foreach (var metadataName in metadataNames)
            {
                var type = compilation.GetTypeByMetadataName(metadataName);
                if (type is object)
                {
                    builder.Add(type);
                }
            }

            return builder.ToImmutable();
        }
    }
}
