using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Every reported diagnostic carries properties["snippet"] with the trimmed
    /// source line at the report location, so downstream trend tooling can build a stable
    /// violation hash without parsing source. All analyzers report through
    /// UpaDiagnostics.Create, so probing one operation-based and one declaration-based rule
    /// covers the shared path.
    /// </summary>
    public class SnippetPropertyTests
    {
        private static async Task<ImmutableArray<Diagnostic>> RunAsync(
            DiagnosticAnalyzer analyzer,
            string source,
            string? enableRuleId = null)
        {
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(UnityEngine.MonoBehaviour).Assembly.Location),
                // UPA2011 registers only when UniTask is referenced.
                TestMetadataReferences.EmptyAssembly(UpaProfile.UniTaskAssemblyName),
            };

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            if (enableRuleId is object)
            {
                options = options.WithSpecificDiagnosticOptions(
                    ImmutableDictionary<string, ReportDiagnostic>.Empty
                        .Add(enableRuleId, ReportDiagnostic.Warn));
            }

            var compilation = CSharpCompilation.Create(
                "SnippetProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                options);

            return await compilation
                .WithAnalyzers(ImmutableArray.Create(analyzer))
                .GetAnalyzerDiagnosticsAsync();
        }

        [Fact]
        public async Task OperationBasedRule_CarriesTrimmedLineSnippet()
        {
            var diagnostics = await RunAsync(new UPA0006HotPathAllocationAnalyzer(), @"
using UnityEngine;

class C : MonoBehaviour
{
    object box;

    void Update()
    {
        box = 42;
    }
}");
            var diagnostic = Assert.Single(diagnostics, d => d.Id == "UPA0006");
            Assert.Equal("box = 42;", diagnostic.Properties[UpaDiagnostics.SnippetPropertyKey]);
        }

        [Fact]
        public async Task DeclarationBasedRule_CarriesTrimmedLineSnippet()
        {
            var diagnostics = await RunAsync(new UPA2011CoroutineAnalyzer(), @"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator FadeOut()
    {
        yield return null;
    }
}", enableRuleId: "UPA2011");
            var diagnostic = Assert.Single(diagnostics, d => d.Id == "UPA2011");
            Assert.Equal("IEnumerator FadeOut()", diagnostic.Properties[UpaDiagnostics.SnippetPropertyKey]);
        }
    }
}
