using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Diagnostic factory that attaches the stable-violation-identification properties
    /// for downstream tooling: "snippet" carries the trimmed source line at the
    /// report location so downstream trend tooling can build a stable hash
    /// (path + rule ID + snippet) without re-parsing source code.
    /// </summary>
    internal static class UpaDiagnostics
    {
        internal const string SnippetPropertyKey = "snippet";

        public static Diagnostic Create(
            DiagnosticDescriptor descriptor,
            Location location,
            params object?[]? messageArgs)
        {
            return Diagnostic.Create(descriptor, location, GetProperties(location), messageArgs);
        }

        /// <summary>
        /// Same, for rules that report at a use site but want to point at a declaration
        /// elsewhere — the snippet still comes from the primary location, since that is the
        /// line the diagnostic is anchored to.
        /// </summary>
        public static Diagnostic Create(
            DiagnosticDescriptor descriptor,
            Location location,
            ImmutableArray<Location> additionalLocations,
            params object?[]? messageArgs)
        {
            return Diagnostic.Create(
                descriptor, location, additionalLocations, GetProperties(location), messageArgs);
        }

        private static ImmutableDictionary<string, string?> GetProperties(Location location)
        {
            var tree = location.SourceTree;
            if (tree is null)
            {
                return ImmutableDictionary<string, string?>.Empty;
            }

            var line = tree.GetText().Lines.GetLineFromPosition(location.SourceSpan.Start);
            return ImmutableDictionary<string, string?>.Empty
                .Add(SnippetPropertyKey, line.ToString().Trim());
        }
    }
}
