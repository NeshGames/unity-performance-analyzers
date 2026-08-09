using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// The options file is read and parsed once per compilation. That is what
    /// <see cref="UpaOptions"/> documents, and it is invisible to every other test here —
    /// resolving it inside an operation callback produces identical diagnostics and reparses
    /// the file once per matching call. UPA0005 did exactly that for one release.
    /// </summary>
    public class UpaOptionsLifetimeTests
    {
        private sealed class CountingAdditionalText : AdditionalText
        {
            private readonly SourceText _text;

            public CountingAdditionalText(string path, string content)
            {
                Path = path;
                _text = SourceText.From(content);
            }

            public override string Path { get; }

            public int Reads { get; private set; }

            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                Reads++;
                return _text;
            }
        }

        [Fact]
        public async Task OptionsFile_IsReadOncePerCompilation_NotOncePerReport()
        {
            const string source = @"
using UnityEngine;

static class GameLog
{
    public static void A(string m) { Debug.Log(m); }
    public static void B(string m) { Debug.Log(m); }
    public static void C(string m) { Debug.LogWarning(m); }
    public static void D(string m) { Debug.LogError(m); }
    public static void E(string m) { Debug.Log(m); }
}";

            var probe = new CountingAdditionalText(
                "/" + UpaOptionCatalog.OptionsFileName,
                "upa_log_wrapper_types = NotThisType");

            var compilation = CSharpCompilation.Create(
                "Fixture",
                new[] { CSharpSyntaxTree.ParseText(source) },
                new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(UnityEngine.Debug).Assembly.Location),
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithSpecificDiagnosticOptions(new[]
                    {
                        new KeyValuePair<string, ReportDiagnostic>("UPA0005", ReportDiagnostic.Warn),
                    }));

            var withAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new UPA0005DirectDebugLoggingAnalyzer()),
                new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(probe)));

            var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

            // Non-vacuity first: if the rule never ran, the file would be read zero times and
            // the count assertion below would pass while proving nothing.
            Assert.Equal(5, diagnostics.Count(d => d.Id == "UPA0005"));
            Assert.Equal(1, probe.Reads);
        }
    }
}
