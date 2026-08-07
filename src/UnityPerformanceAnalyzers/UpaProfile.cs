using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Per-compilation profile shared by the conditional rules (UPA2000+/UPA3000+), resolved
    /// from referenced assembly names and preprocessor symbols. Detection is by assembly
    /// name, never by type name — namespaces move between versions, assembly names stay
    /// stable.
    /// </summary>
    internal sealed class UpaProfile
    {
        // Verified against the real packages: the Unity asmdef and NuGet assemblies carry
        // exactly these names. Kept as single-point constants regardless.
        internal const string UniTaskAssemblyName = "UniTask";
        internal const string ZStringAssemblyName = "ZString";
        internal const string R3AssemblyName = "R3";

        internal const string WebGlDefine = "UPA_TARGET_WEBGL";

        public bool HasUniTask { get; }
        public bool HasZString { get; }
        public bool HasR3 { get; }
        public bool RequiresWebGL { get; }

        private UpaProfile(bool hasUniTask, bool hasZString, bool hasR3, bool requiresWebGL)
        {
            HasUniTask = hasUniTask;
            HasZString = hasZString;
            HasR3 = hasR3;
            RequiresWebGL = requiresWebGL;
        }

        /// <summary>
        /// Resolves the profile once per compilation; call inside
        /// RegisterCompilationStartAction and pass the result to the node actions.
        /// <paramref name="analyzerOptions"/> is reserved for a future .additionalfile-based
        /// override channel for RequiresWebGL.
        /// </summary>
        public static UpaProfile Resolve(Compilation compilation, AnalyzerOptions analyzerOptions)
        {
            var hasUniTask = false;
            var hasZString = false;
            var hasR3 = false;

            foreach (var identity in compilation.ReferencedAssemblyNames)
            {
                if (string.Equals(identity.Name, UniTaskAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    hasUniTask = true;
                }
                else if (string.Equals(identity.Name, ZStringAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    hasZString = true;
                }
                else if (string.Equals(identity.Name, R3AssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    hasR3 = true;
                }
            }

            var symbols = (compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions)
                              ?.PreprocessorSymbolNames.ToImmutableHashSet(StringComparer.Ordinal)
                          ?? ImmutableHashSet<string>.Empty;
            var requiresWebGL = symbols.Contains(WebGlDefine);

            return new UpaProfile(hasUniTask, hasZString, hasR3, requiresWebGL);
        }
    }
}
