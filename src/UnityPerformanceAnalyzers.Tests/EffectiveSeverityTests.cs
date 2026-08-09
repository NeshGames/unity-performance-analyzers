using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Does a project-side .editorconfig (dotnet_diagnostic.&lt;ID&gt;.severity) override the
    /// severity passed to the Diagnostic.Create effectiveSeverity overload? The
    /// severity-by-profile technique is only usable if it does. These tests are a permanent
    /// regression guard for that behavior on the pinned Roslyn version.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EffectiveSeverityProbeAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "UPATEST02";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Effective severity probe",
            "Probe diagnostic reported with effectiveSeverity Error",
            "Test",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
                ctx.RegisterSyntaxNodeAction(
                    nodeCtx => nodeCtx.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        nodeCtx.Node.GetLocation(),
                        DiagnosticSeverity.Error,
                        additionalLocations: null,
                        properties: null)),
                    SyntaxKind.InvocationExpression));
        }
    }

    public class EffectiveSeverityTests
    {
        private const string Source = @"
class C
{
    void M()
    {
        {|#0:Marker.Mark()|};
    }
}

static class Marker
{
    public static void Mark() { }
}
";

        private static CSharpAnalyzerTest<EffectiveSeverityProbeAnalyzer, DefaultVerifier> CreateTest(
            string? editorConfig)
            => RuleVerifier.CreateTest<EffectiveSeverityProbeAnalyzer>(Source, new RuleHarness
            {
                UnityStubs = false,
                RawEditorConfig = editorConfig,
            });

        // Baseline: the overload really does raise the reported severity above the
        // descriptor default (Warning -> Error).
        [Fact]
        public async Task WithoutEditorConfig_EffectiveSeverityErrorIsReported()
        {
            var test = CreateTest(null);
            test.TestState.ExpectedDiagnostics.Add(
                new DiagnosticResult(EffectiveSeverityProbeAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                    .WithLocation(0));
            await test.RunAsync();
        }

        // Q5 core question: .editorconfig wins over effectiveSeverity (the desired outcome —
        // the analyzer supplies a context-dependent default, the user can still override it).
        [Fact]
        public async Task EditorConfigWarning_OverridesEffectiveSeverityError()
        {
            var test = CreateTest("root = true\n\n[*.cs]\ndotnet_diagnostic.UPATEST02.severity = warning\n");
            test.TestState.ExpectedDiagnostics.Add(
                new DiagnosticResult(EffectiveSeverityProbeAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0));
            await test.RunAsync();
        }

        // severity = none fully suppresses the diagnostic even though the analyzer reports
        // it with effectiveSeverity Error.
        [Fact]
        public async Task EditorConfigNone_SuppressesDiagnosticEntirely()
        {
            var test = CreateTest("root = true\n\n[*.cs]\ndotnet_diagnostic.UPATEST02.severity = none\n");
            await test.RunAsync();
        }
    }
}
