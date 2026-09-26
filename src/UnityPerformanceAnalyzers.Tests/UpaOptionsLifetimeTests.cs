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

        /// <summary>
        /// At most one read per analyzer, however many of its parts ask. UPA0003 used to parse
        /// the file itself and again through the hot-path detector it built eagerly, and the
        /// detector did the same for every hot-path rule - so a compilation read the file
        /// once per question rather than once per analyzer. Every analyzer below is made to
        /// reach its option lookup, which is what makes the upper bound mean something.
        /// </summary>
        [Fact]
        public async Task OptionsFile_IsReadAtMostOncePerAnalyzer_AcrossItsParts()
        {
            const string source = @"
using System.Collections.Generic;
using UnityEngine;

enum State { Idle, Running }

class C : MonoBehaviour
{
    public Material mat;
    List<int> source = new List<int>();
    List<int> target = new List<int>();

    void Update()
    {
        mat.SetFloat(""_A"", 1f);
        object boxed = 1;
        foreach (var item in source)
            target.Add(item);
    }

    void M(State state)
    {
        switch (state)
        {
            case State.Idle:
                break;
            default:
                break;
        }
    }
}";

            var probe = new CountingAdditionalText(
                "/" + UpaOptionCatalog.OptionsFileName,
                string.Join("\n",
                    "upa_shader_property_hot_path_only = true",
                    "upa_addrange_hot_path_only = true",
                    "upa_enum_switch_allow_default = false"));

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
                new UPA0003StringPropertyAccessAnalyzer(),
                new UPA0006HotPathAllocationAnalyzer(),
                new UPA0029SequentialAddAnalyzer(),
                new UPA1001NonExhaustiveEnumSwitchAnalyzer());

            var compilation = CSharpCompilation.Create(
                "Fixture",
                new[] { CSharpSyntaxTree.ParseText(source) },
                new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(UnityEngine.Debug).Assembly.Location),
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var diagnostics = await compilation
                .WithAnalyzers(analyzers, new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(probe)))
                .GetAnalyzerDiagnosticsAsync();

            // Non-vacuity: each rule reported, so each reached its option lookup, and the
            // options took effect (UPA1001 reports past a default arm only when told to).
            Assert.Equal(
                new[] { "UPA0003", "UPA0006", "UPA0029", "UPA1001" },
                diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id, System.StringComparer.Ordinal));
            Assert.InRange(probe.Reads, 1, analyzers.Length);
        }

        /// <summary>
        /// The parse is shared across analyzers by the text it came from, which is immutable -
        /// so the same text answers with the same parse, and an edited file, arriving as a new
        /// text, is parsed afresh rather than answered from the old one.
        /// </summary>
        [Fact]
        public void Parse_IsSharedPerSourceText_AndNeverAcrossDifferentTexts()
        {
            var path = "/" + UpaOptionCatalog.OptionsFileName;
            var first = new CountingAdditionalText(path, "upa_log_wrapper_types = A");
            var edited = new CountingAdditionalText(path, "upa_log_wrapper_types = A");

            var once = UpaOptions.Resolve(new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(first)));
            var again = UpaOptions.Resolve(new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(first)));
            var afresh = UpaOptions.Resolve(new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(edited)));

            Assert.Same(once, again);
            Assert.NotSame(once, afresh);
        }
    }
}
