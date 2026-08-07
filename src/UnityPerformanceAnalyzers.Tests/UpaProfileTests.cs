using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Unit tests for UpaProfile. Package presence is simulated with in-memory compilation
    /// references named after the assembly-name constants, so these tests stay valid even
    /// if the constants change.
    /// </summary>
    public class UpaProfileTests
    {
        private static readonly AnalyzerOptions s_emptyOptions =
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);

        private static CSharpCompilation CreateCompilation(
            string[]? referencedAssemblyNames = null,
            string[]? preprocessorSymbols = null,
            bool withSyntaxTree = true)
        {
            var parseOptions = new CSharpParseOptions(
                preprocessorSymbols: preprocessorSymbols ?? Array.Empty<string>());

            var trees = withSyntaxTree
                ? new[] { CSharpSyntaxTree.ParseText("class C { }", parseOptions) }
                : Array.Empty<SyntaxTree>();

            var references = (referencedAssemblyNames ?? Array.Empty<string>())
                .Select(name => (MetadataReference)CSharpCompilation.Create(name).ToMetadataReference());

            return CSharpCompilation.Create("TestAssembly", trees, references);
        }

        private static UpaProfile Resolve(CSharpCompilation compilation)
            => UpaProfile.Resolve(compilation, s_emptyOptions);

        [Fact]
        public void NoReferences_AllPackageFlagsFalse()
        {
            var profile = Resolve(CreateCompilation());

            Assert.False(profile.HasUniTask);
            Assert.False(profile.HasZString);
            Assert.False(profile.HasR3);
            Assert.False(profile.RequiresWebGL);
        }

        [Fact]
        public void UniTaskReferenced_OnlyHasUniTask()
        {
            var profile = Resolve(CreateCompilation(
                referencedAssemblyNames: new[] { UpaProfile.UniTaskAssemblyName }));

            Assert.True(profile.HasUniTask);
            Assert.False(profile.HasZString);
            Assert.False(profile.HasR3);
        }

        [Fact]
        public void ZStringReferenced_OnlyHasZString()
        {
            var profile = Resolve(CreateCompilation(
                referencedAssemblyNames: new[] { UpaProfile.ZStringAssemblyName }));

            Assert.False(profile.HasUniTask);
            Assert.True(profile.HasZString);
            Assert.False(profile.HasR3);
        }

        [Fact]
        public void R3Referenced_OnlyHasR3()
        {
            var profile = Resolve(CreateCompilation(
                referencedAssemblyNames: new[] { UpaProfile.R3AssemblyName }));

            Assert.False(profile.HasUniTask);
            Assert.False(profile.HasZString);
            Assert.True(profile.HasR3);
        }

        [Fact]
        public void AllPackagesReferenced_AllFlagsTrue()
        {
            var profile = Resolve(CreateCompilation(referencedAssemblyNames: new[]
            {
                UpaProfile.UniTaskAssemblyName,
                UpaProfile.ZStringAssemblyName,
                UpaProfile.R3AssemblyName,
            }));

            Assert.True(profile.HasUniTask);
            Assert.True(profile.HasZString);
            Assert.True(profile.HasR3);
        }

        [Fact]
        public void AssemblyNameMatch_IsCaseInsensitive()
        {
            var profile = Resolve(CreateCompilation(
                referencedAssemblyNames: new[] { UpaProfile.UniTaskAssemblyName.ToUpperInvariant() }));

            Assert.True(profile.HasUniTask);
        }

        [Fact]
        public void UnrelatedAssemblyNames_DoNotMatch()
        {
            var profile = Resolve(CreateCompilation(
                referencedAssemblyNames: new[] { "UniTask.Linq", "ZString.Extras", "R3.Unity" }));

            Assert.False(profile.HasUniTask);
            Assert.False(profile.HasZString);
            Assert.False(profile.HasR3);
        }

        [Fact]
        public void WebGlDefinePresent_RequiresWebGL()
        {
            var profile = Resolve(CreateCompilation(
                preprocessorSymbols: new[] { UpaProfile.WebGlDefine }));

            Assert.True(profile.RequiresWebGL);
        }

        [Fact]
        public void WebGlDefineAbsent_DoesNotRequireWebGL()
        {
            var profile = Resolve(CreateCompilation(
                preprocessorSymbols: new[] { "UNITY_EDITOR", "UNITY_2022_3_OR_NEWER" }));

            Assert.False(profile.RequiresWebGL);
        }

        [Fact]
        public void WebGlDefineMatch_IsCaseSensitive()
        {
            var profile = Resolve(CreateCompilation(
                preprocessorSymbols: new[] { "upa_target_webgl" }));

            Assert.False(profile.RequiresWebGL);
        }

        [Fact]
        public void CompilationWithoutSyntaxTrees_ResolvesWithoutWebGL()
        {
            var profile = Resolve(CreateCompilation(
                referencedAssemblyNames: new[] { UpaProfile.UniTaskAssemblyName },
                withSyntaxTree: false));

            Assert.True(profile.HasUniTask);
            Assert.False(profile.RequiresWebGL);
        }
    }
}
