using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// What a rule test needs from the compilation it runs against: which extra sources are in
    /// it, whether the Unity stubs are referenced, which packages appear present, which
    /// default-off rules a preset would have enabled, and what configuration reaches the
    /// analyzer.
    /// </summary>
    /// <remarks>
    /// The reference set is deliberately per-test rather than global. Several negative cases
    /// depend on a type <em>not</em> resolving — the Unity stubs are absent from a third of the
    /// rule tests, and package presence is the whole subject of the conditional rules. A shared
    /// reference set would make those tests pass for the wrong reason.
    /// </remarks>
    internal sealed class RuleHarness
    {
        /// <summary>Reference the minimal UnityEngine stand-in. Off leaves Unity types unresolvable,
        /// which also switches off hot-path classification by MonoBehaviour message name.</summary>
        public bool UnityStubs { get; set; } = true;

        /// <summary>Extra compilation units, e.g. the DOTween stubs.</summary>
        public List<string> Sources { get; } = new List<string>();

        /// <summary>Extra compilation units that need a name, so an <c>.editorconfig</c>
        /// section can glob them.</summary>
        public List<(string Name, string Content)> NamedSources { get; } = new List<(string, string)>();

        /// <summary>Assembly names to fake as referenced. Package presence is detected by name
        /// alone, so an empty assembly is enough to flip an <see cref="UpaProfile"/> flag.</summary>
        public List<string> PackageAssemblies { get; } = new List<string>();

        /// <summary>Rule ids to raise to warning, the way a preset does. Rules in the UPA2000+
        /// and UPA3000+ groups are off by default and report nothing until something enables
        /// them.</summary>
        public List<string> EnabledRules { get; } = new List<string>();

        /// <summary>Preprocessor symbols, e.g. <see cref="UpaProfile.WebGlDefine"/>.</summary>
        public List<string> Defines { get; } = new List<string>();

        /// <summary>Extra lines inside the <c>[*.cs]</c> section, for option keys.</summary>
        public string? EditorConfig { get; set; }

        /// <summary>A whole <c>.editorconfig</c> verbatim, for tests about the file itself —
        /// several sections, non-<c>[*.cs]</c> globs, ordering. Replaces everything the other
        /// properties would have written.</summary>
        public string? RawEditorConfig { get; set; }

        /// <summary>Contents of the universal options file.</summary>
        public string? OptionsFile { get; set; }

        /// <summary>Name of the assembly under analysis. The editor-only rules decide what they
        /// are looking at from this name, so it is an input to them, not a label.</summary>
        public string? AssemblyName { get; set; }

        /// <summary>Compile with <c>/unsafe</c>, for the rules about stack allocation.</summary>
        public bool AllowUnsafe { get; set; }

        /// <summary>Markup matching, for the rules whose single id carries more than one
        /// descriptor.</summary>
        public MarkupOptions? MarkupOptions { get; set; }
    }

    /// <summary>
    /// The Roslyn harness every rule test used to rebuild for itself: 41 of 58 test files
    /// carried their own copy of the same ten lines, and each variant — the WebGL define, the
    /// inline <c>.editorconfig</c>, the options file — was hand-rolled a second and third time.
    /// One module means the differential pipeline has somewhere to attach, and a new rule's
    /// tests start at one line instead of ten.
    /// </summary>
    internal static class RuleVerifier
    {
        /// <summary>Where the analyzers look for the universal options file.</summary>
        public const string OptionsFilePath = "/Rules.UnityPerformanceAnalyzers.additionalfile";

        private const string EditorConfigPath = "/.editorconfig";

        public static Task VerifyAsync<TAnalyzer>(string source, RuleHarness? harness = null)
            where TAnalyzer : DiagnosticAnalyzer, new()
            => CreateTest<TAnalyzer>(source, harness).RunAsync();

        /// <summary>
        /// The configured test, unrun. For the handful of cases that assert on the message text
        /// as well as the span, and so have to add their own expected diagnostics.
        /// </summary>
        public static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateTest<TAnalyzer>(
            string source,
            RuleHarness? harness = null)
            where TAnalyzer : DiagnosticAnalyzer, new()
        {
            harness ??= new RuleHarness();
            var test = new HarnessAnalyzerTest<TAnalyzer>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
                Defines = harness.Defines.ToArray(),
            };
            if (harness.MarkupOptions is { } markup)
            {
                test.MarkupOptions = markup;
            }

            Configure(test, harness);
            return test;
        }

        public static Task VerifyCodeFixAsync<TAnalyzer, TCodeFix>(
            string source,
            string fixedSource,
            RuleHarness? harness = null)
            where TAnalyzer : DiagnosticAnalyzer, new()
            where TCodeFix : CodeFixProvider, new()
        {
            harness ??= new RuleHarness();
            var test = new HarnessCodeFixTest<TAnalyzer, TCodeFix>
            {
                TestCode = source,
                FixedCode = fixedSource,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
                Defines = harness.Defines.ToArray(),
            };
            if (harness.MarkupOptions is { } markup)
            {
                test.MarkupOptions = markup;
            }

            Configure(test, harness);

            // The fixed state is compiled too, so anything the test code needs to resolve it
            // needs as well. Without this a harness source - a package stub, say - makes the
            // two states differ by a document and the run fails on the count, saying nothing
            // about the rewrite it was meant to check.
            foreach (var extraSource in harness.Sources)
            {
                test.FixedState.Sources.Add(extraSource);
            }

            foreach (var (extraName, extraContent) in harness.NamedSources)
            {
                test.FixedState.Sources.Add((extraName, extraContent));
            }

            return test.RunAsync();
        }

        private static void Configure(AnalyzerTest<DefaultVerifier> test, RuleHarness harness)
        {
            foreach (var source in harness.Sources)
            {
                test.TestState.Sources.Add(source);
            }

            foreach (var (name, content) in harness.NamedSources)
            {
                test.TestState.Sources.Add((name, content));
            }

            if (harness.UnityStubs)
            {
                test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            }

            foreach (var assemblyName in harness.PackageAssemblies)
            {
                test.TestState.AdditionalReferences.Add(TestMetadataReferences.EmptyAssembly(assemblyName));
            }

            var editorConfig = BuildEditorConfig(harness);
            if (editorConfig is not null)
            {
                test.TestState.AnalyzerConfigFiles.Add((EditorConfigPath, editorConfig));
            }

            if (harness.OptionsFile is not null)
            {
                test.TestState.AdditionalFiles.Add((OptionsFilePath, harness.OptionsFile));
            }

            if (harness.AssemblyName is { } projectAssemblyName)
            {
                test.SolutionTransforms.Add((solution, projectId) =>
                    solution.WithProjectAssemblyName(projectId, projectAssemblyName));
            }

            if (harness.AllowUnsafe)
            {
                test.SolutionTransforms.Add((solution, projectId) =>
                {
                    var options = (CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!;
                    return solution.WithProjectCompilationOptions(projectId, options.WithAllowUnsafe(true));
                });
            }
        }

        private static string? BuildEditorConfig(RuleHarness harness)
        {
            if (harness.RawEditorConfig is not null)
            {
                return harness.RawEditorConfig;
            }

            if (harness.EnabledRules.Count == 0 && harness.EditorConfig is null)
            {
                return null;
            }

            var text = new StringBuilder("root = true\n\n[*.cs]\n");
            foreach (var ruleId in harness.EnabledRules)
            {
                text.Append("dotnet_diagnostic.").Append(ruleId).Append(".severity = warning\n");
            }

            if (harness.EditorConfig is not null)
            {
                text.Append(harness.EditorConfig).Append('\n');
            }

            return text.ToString();
        }

        // The define set has to reach the parser, and CreateParseOptions is the only way in.
        // Two subclasses rather than one: the analyzer and code-fix tests have separate base
        // classes, and the four lines are cheaper than a shared abstraction over both.
        private sealed class HarnessAnalyzerTest<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
            where TAnalyzer : DiagnosticAnalyzer, new()
        {
            public string[] Defines { get; set; } = System.Array.Empty<string>();

            protected override ParseOptions CreateParseOptions()
            {
                var options = base.CreateParseOptions();
                return Defines.Length == 0
                    ? options
                    : ((CSharpParseOptions)options).WithPreprocessorSymbols(Defines);
            }
        }

        private sealed class HarnessCodeFixTest<TAnalyzer, TCodeFix>
            : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
            where TAnalyzer : DiagnosticAnalyzer, new()
            where TCodeFix : CodeFixProvider, new()
        {
            public string[] Defines { get; set; } = System.Array.Empty<string>();

            protected override ParseOptions CreateParseOptions()
            {
                var options = base.CreateParseOptions();
                return Defines.Length == 0
                    ? options
                    : ((CSharpParseOptions)options).WithPreprocessorSymbols(Defines);
            }
        }
    }
}
