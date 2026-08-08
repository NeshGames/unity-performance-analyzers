using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using UnityPerformanceAnalyzers.Cli;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Covers the CLI's public contract: argument handling, exit codes, and the JSON
    /// shape. These are the surfaces that break downstream CI when they change, so each
    /// one is pinned here.
    /// </summary>
    public sealed class CliTests : IDisposable
    {
        private const string HotPathViolation = @"
using System.Collections.Generic;
using UnityEngine;

public class Probe : MonoBehaviour
{
    void Update()
    {
        var body = GetComponent<Rigidbody>();
    }
}";

        private const string Clean = @"
using UnityEngine;

public sealed class Quiet : MonoBehaviour
{
    void Awake()
    {
        var body = GetComponent<Rigidbody>();
    }
}";

        private readonly string _dir;

        public CliTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "upa-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private string Write(string name, string source)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, source);
            return path;
        }

        private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliEntryPoint.Run(args, stdout, stderr);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        private static JsonElement ParseJson(string stdout) => JsonDocument.Parse(stdout).RootElement;

        // Case 1
        [Fact]
        public void Violation_Reports_AndExitsWithOne()
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (exitCode, stdout, _) = Run(file);

            Assert.Equal(1, exitCode);
            Assert.Contains("UPA0001", stdout);
        }

        // Case 2
        [Fact]
        public void JsonFormat_CarriesTheContractFields()
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (_, stdout, _) = Run(file, "--format", "json");
            var root = ParseJson(stdout);

            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("toolVersion").GetString()));

            var diagnostic = root.GetProperty("diagnostics")[0];
            Assert.Equal("UPA0001", diagnostic.GetProperty("id").GetString());
            Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
            Assert.True(diagnostic.GetProperty("line").GetInt32() > 0);
            Assert.True(diagnostic.GetProperty("column").GetInt32() > 0);
            Assert.Contains("docs/rules/UPA0001.md", diagnostic.GetProperty("helpUri").GetString());
            Assert.Contains(
                "GetComponent",
                diagnostic.GetProperty("properties").GetProperty("snippet").GetString());
        }

        // Case 3
        [Fact]
        public void CleanFile_ExitsZero_WithEmptyDiagnostics()
        {
            var file = Write("Quiet.cs", Clean);

            var (exitCode, stdout, _) = Run(file, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Empty(ParseJson(stdout).GetProperty("diagnostics").EnumerateArray());
        }

        // Case 4
        [Fact]
        public void ByDefault_CompilationWideRulesAreExcluded()
        {
            var file = Write("Leaf.cs", "public class Leaf { }");

            var (_, stdout, _) = Run(file, "--all-warn", "--format", "json");
            var root = ParseJson(stdout);

            Assert.Contains(
                "UPA1000",
                root.GetProperty("excludedRules").EnumerateArray().Select(e => e.GetString()));
            Assert.DoesNotContain(
                "UPA1000",
                root.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("id").GetString()));
        }

        // Case 5
        [Fact]
        public void WholeAssembly_EnablesCompilationWideRules()
        {
            var file = Write("Leaf.cs", "public class Leaf { }");

            var (_, stdout, _) = Run(file, "--whole-assembly", "--all-warn", "--format", "json");
            var root = ParseJson(stdout);

            Assert.Empty(root.GetProperty("excludedRules").EnumerateArray());
            Assert.Contains(
                "UPA1000",
                root.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("id").GetString()));
        }

        // Case 5b — the exclusion is not a file-count heuristic
        [Fact]
        public void MultipleFilesWithoutTheFlag_StillExcludeCompilationWideRules()
        {
            var first = Write("Leaf.cs", "public class Leaf { }");
            var second = Write("Other.cs", "public class Other { }");

            var (_, stdout, _) = Run(first, second, "--all-warn", "--format", "json");

            Assert.Contains(
                "UPA1000",
                ParseJson(stdout).GetProperty("excludedRules").EnumerateArray().Select(e => e.GetString()));
        }

        // Cases 6 and 7 — the same source, with and without the fake reference
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConditionalRule_FollowsTheFakeReference(bool referenced)
        {
            var file = Write("Async.cs", @"
using System.Threading.Tasks;

public class Loader
{
    public async Task LoadAsync() { await Task.Yield(); }
}");

            var args = referenced
                ? new[] { file, "--reference", "UniTask", "--all-warn", "--format", "json" }
                : new[] { file, "--all-warn", "--format", "json" };
            var (_, stdout, _) = Run(args);

            var ids = ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                .Select(d => d.GetProperty("id").GetString()).ToArray();

            Assert.Equal(referenced, ids.Contains("UPA2010"));
        }

        // Case 8
        [Fact]
        public void WebGlDefine_ActivatesPlatformRules()
        {
            var file = Write("Threading.cs", @"
using System.Threading.Tasks;

public class Worker
{
    public void Kick() { _ = Task.Run(() => { }); }
}");

            var (_, stdout, _) = Run(file, "--define", "UPA_TARGET_WEBGL", "--all-warn", "--format", "json");

            Assert.Contains(
                "UPA3000",
                ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
        }

        // Cases 9 and 10
        [Theory]
        [InlineData("none", 0)]
        [InlineData("error", 0)]
        [InlineData("warning", 1)]
        [InlineData("info", 1)]
        public void FailOn_SetsTheExitThreshold(string failOn, int expectedExitCode)
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (exitCode, _, _) = Run(file, "--fail-on", failOn);

            Assert.Equal(expectedExitCode, exitCode);
        }

        // Case 11
        [Fact]
        public void MissingFile_IsAUsageError()
        {
            var (exitCode, stdout, stderr) = Run(Path.Combine(_dir, "nope.cs"));

            Assert.Equal(2, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("not found", stderr);
        }

        // Case 12
        [Fact]
        public void ReservedSarifFormat_IsAUsageError()
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (exitCode, _, stderr) = Run(file, "--format", "sarif");

            Assert.Equal(2, exitCode);
            Assert.Contains("sarif", stderr);
        }

        // Case 13
        [Fact]
        public void ListRules_MatchesTheAssembly()
        {
            var (exitCode, stdout, _) = Run("--list-rules", "--format", "json");
            var root = ParseJson(stdout);

            Assert.Equal(0, exitCode);
            var rules = root.GetProperty("rules").EnumerateArray().ToArray();
            Assert.NotEmpty(rules);

            var upa1000 = rules.Single(r => r.GetProperty("id").GetString() == "UPA1000");
            Assert.True(upa1000.GetProperty("compilationWide").GetBoolean());

            var upa2030 = rules.Single(r => r.GetProperty("id").GetString() == "UPA2030");
            Assert.Equal("DOTween", upa2030.GetProperty("condition").GetString());
            Assert.True(upa2030.GetProperty("hotPath").GetBoolean());
        }

        // Case 14
        [Fact]
        public void ListRulesWithFiles_IsAUsageError()
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (exitCode, _, stderr) = Run("--list-rules", file);

            Assert.Equal(2, exitCode);
            Assert.Contains("--list-rules", stderr);
        }

        // Case 15 — nothing but JSON reaches stdout, even when there is something to warn about
        [Fact]
        public void JsonMode_KeepsStdoutParseable()
        {
            var file = Write("Broken.cs", @"
public class Broken
{
    void Use() { MissingType thing = null; }
}");

            var (_, stdout, stderr) = Run(file, "--format", "json");

            var document = JsonDocument.Parse(stdout);
            Assert.True(document.RootElement.GetProperty("summary").GetProperty("compileErrorCount").GetInt32() > 0);
            Assert.Contains("compile error", stderr);
        }

        // Case 16b — declaring a complete compilation makes compile errors fatal, so a
        // CI gate cannot pass code the analyzers could not see properly.
        [Fact]
        public void CompileErrors_WithWholeAssembly_RefuseToReportSuccess()
        {
            var file = Write("Broken.cs", @"
public class Broken
{
    void Use() { MissingType thing = null; }
}");

            var (exitCode, _, stderr) = Run(file, "--whole-assembly", "--format", "json");

            Assert.Equal(2, exitCode);
            Assert.Contains("Refusing to report success", stderr);
        }

        // Case 16
        [Fact]
        public void CompileErrors_DoNotChangeTheExitCodeAndAreNotReported()
        {
            var file = Write("Broken.cs", @"
public class Broken
{
    void Use() { MissingType thing = null; }
}");

            var (exitCode, stdout, _) = Run(file, "--format", "json");
            var root = ParseJson(stdout);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain(
                root.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("id").GetString()!),
                id => id.StartsWith("CS", StringComparison.Ordinal));
        }

        // Case 17
        [Fact]
        public void EditorAssemblyName_SilencesPlayerCodeRules()
        {
            var file = Write("Tools.cs", @"
using UnityEngine;

public class Tools : MonoBehaviour
{
    void OnGUI() { }
}");

            var (_, playerStdout, _) = Run(file, "--all-warn", "--format", "json");
            var (_, editorStdout, _) = Run(file, "--assembly-name", "MyGame.Editor", "--all-warn", "--format", "json");

            Assert.Contains(
                "UPA0023",
                ParseJson(playerStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
            Assert.DoesNotContain(
                "UPA0023",
                ParseJson(editorStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
        }

        // Case 18
        [Fact]
        public void Diagnostics_AreSortedByPosition()
        {
            var file = Write("Many.cs", @"
using System.Collections.Generic;
using UnityEngine;

public class Many : MonoBehaviour
{
    void Update()
    {
        var a = GetComponent<Rigidbody>();
        var b = new List<int>();
        var c = GetComponent<Transform>();
    }
}");

            var (_, stdout, _) = Run(file, "--format", "json");
            var lines = ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                .Select(d => (d.GetProperty("line").GetInt32(), d.GetProperty("column").GetInt32()))
                .ToArray();

            Assert.Equal(lines.OrderBy(p => p.Item1).ThenBy(p => p.Item2), lines);
        }

        // Severities from .editorconfig travel Roslyn's tree-options channel, which
        // analyzers never see — so these two cases prove the CLI lifts them onto the
        // compilation rather than silently dropping the file's configuration.
        [Fact]
        public void EditorConfig_CanSilenceAnEnabledRule()
        {
            var file = Write("Probe.cs", HotPathViolation);
            var config = Write(".editorconfig", @"root = true

[*.cs]
dotnet_diagnostic.UPA0001.severity = none
");

            var (exitCode, stdout, _) = Run(file, "--editorconfig", config, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain(
                "UPA0001",
                ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
        }

        [Fact]
        public void EditorConfig_CanEnableAnOffByDefaultRule()
        {
            var file = Write("Logging.cs", @"
using UnityEngine;

public class Logging : MonoBehaviour
{
    void Start() { Debug.Log(""hello""); }
}");
            var config = Write(".editorconfig", @"root = true

[*.cs]
dotnet_diagnostic.UPA0005.severity = warning
");

            var (withoutCode, withoutStdout, _) = Run(file, "--format", "json");
            var (withCode, withStdout, _) = Run(file, "--editorconfig", config, "--format", "json");

            Assert.Equal(0, withoutCode);
            Assert.DoesNotContain(
                "UPA0005",
                ParseJson(withoutStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));

            Assert.Equal(1, withCode);
            Assert.Contains(
                "UPA0005",
                ParseJson(withStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
        }

        // A section can scope a rule to one file pattern, so severities must stay per file:
        // the result must not depend on which order the files were passed.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EditorConfig_ScopesSeveritiesPerFile_RegardlessOfInputOrder(bool reversed)
        {
            var loud = Write("Loud.cs", HotPathViolation.Replace("Probe", "Loud"));
            var quiet = Write("Quiet.cs", HotPathViolation.Replace("Probe", "Quiet"));
            var config = Write(".editorconfig", @"root = true

[Quiet.cs]
dotnet_diagnostic.UPA0001.severity = none
");

            var files = reversed ? new[] { quiet, loud } : new[] { loud, quiet };
            var (_, stdout, _) = Run(files[0], files[1], "--editorconfig", config, "--format", "json");

            var reported = ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                .Where(d => d.GetProperty("id").GetString() == "UPA0001")
                .Select(d => Path.GetFileName(d.GetProperty("file").GetString()!))
                .ToArray();

            Assert.Equal(new[] { "Loud.cs" }, reported);
        }

        // Analyzer options (not severities) also come from the file, through the other channel.
        [Fact]
        public void EditorConfig_CarriesAnalyzerOptions()
        {
            var file = Write("Custom.cs", @"
using System.Collections.Generic;
using UnityEngine;

public class Custom : MonoBehaviour
{
    void Tick()
    {
        var junk = new List<int>();
    }
}");
            var config = Write(".editorconfig", @"root = true

[*.cs]
upa_hot_path_messages = Tick
");

            var (_, defaultStdout, _) = Run(file, "--format", "json");
            var (_, configuredStdout, _) = Run(file, "--editorconfig", config, "--format", "json");

            Assert.DoesNotContain(
                "UPA0006",
                ParseJson(defaultStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
            Assert.Contains(
                "UPA0006",
                ParseJson(configuredStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
        }

        // <IncludeAll> sets the policy for every rule the ruleset did not name, so
        // ignoring it would let the tool contradict the ruleset it was handed.
        [Fact]
        public void Ruleset_IncludeAllNone_SilencesUnnamedRules()
        {
            var file = Write("Probe.cs", HotPathViolation);
            var ruleset = Write("all-off.ruleset", @"<?xml version=""1.0"" encoding=""utf-8""?>
<RuleSet Name=""off"" ToolsVersion=""10.0"">
  <IncludeAll Action=""None"" />
</RuleSet>");

            var (exitCode, stdout, _) = Run(file, "--ruleset", ruleset, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Empty(ParseJson(stdout).GetProperty("diagnostics").EnumerateArray());
        }

        [Fact]
        public void Ruleset_IncludeAllError_PromotesUnnamedRules()
        {
            var file = Write("Probe.cs", HotPathViolation);
            var ruleset = Write("all-error.ruleset", @"<?xml version=""1.0"" encoding=""utf-8""?>
<RuleSet Name=""strict"" ToolsVersion=""10.0"">
  <IncludeAll Action=""Error"" />
</RuleSet>");

            var (exitCode, stdout, _) = Run(file, "--ruleset", ruleset, "--fail-on", "error", "--format", "json");

            Assert.Equal(1, exitCode);
            Assert.Contains(
                "error",
                ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                    .Where(d => d.GetProperty("id").GetString() == "UPA0001")
                    .Select(d => d.GetProperty("severity").GetString()));
        }

        // A named rule keeps its own action even when IncludeAll says otherwise.
        [Fact]
        public void Ruleset_SpecificEntry_BeatsIncludeAll()
        {
            var file = Write("Probe.cs", HotPathViolation);
            var ruleset = Write("mixed.ruleset", @"<?xml version=""1.0"" encoding=""utf-8""?>
<RuleSet Name=""mixed"" ToolsVersion=""10.0"">
  <IncludeAll Action=""Error"" />
  <Rules AnalyzerId=""UnityPerformanceAnalyzers"" RuleNamespace=""UnityPerformanceAnalyzers"">
    <Rule Id=""UPA0001"" Action=""None"" />
  </Rules>
</RuleSet>");

            var (_, stdout, _) = Run(file, "--ruleset", ruleset, "--format", "json");

            Assert.DoesNotContain(
                "UPA0001",
                ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
        }

        // Roslyn turns an analyzer exception into an AD0001 *warning*, which an
        // --fail-on error gate would wave through even though nothing was analyzed.
        [Fact]
        public void CrashingAnalyzer_FailsRegardlessOfThreshold()
        {
            var file = Write("Probe.cs", HotPathViolation);
            var options = CliOptions.Parse(new[] { file, "--fail-on", "error" }, out _)!;

            var result = AnalysisRunner.Run(
                options,
                ImmutableArray.Create<DiagnosticAnalyzer>(new ThrowingAnalyzer()));

            Assert.NotEmpty(result.AnalyzerFailures);
            Assert.False(
                AnalysisRunner.ShouldFail(result, "error"),
                "the crash must not be counted as a finding");
            Assert.Equal(CliEntryPoint.ExitError, CliEntryPoint.ResolveExitCode(result, "error"));
        }

        [Fact]
        public void CleanRun_ResolvesToSuccess()
        {
            var file = Write("Quiet.cs", Clean);
            var options = CliOptions.Parse(new[] { file }, out _)!;

            var result = AnalysisRunner.Run(options);

            Assert.Empty(result.AnalyzerFailures);
            Assert.Equal(CliEntryPoint.ExitClean, CliEntryPoint.ResolveExitCode(result, "warning"));
        }

        [DiagnosticAnalyzer(LanguageNames.CSharp)]
        private sealed class ThrowingAnalyzer : DiagnosticAnalyzer
        {
            private static readonly DiagnosticDescriptor Rule = new(
                "UPATEST99",
                "probe",
                "probe",
                "Test",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
                ImmutableArray.Create(Rule);

            public override void Initialize(AnalysisContext context)
            {
                context.EnableConcurrentExecution();
                context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                context.RegisterSyntaxNodeAction(
                    _ => throw new InvalidOperationException("deliberate analyzer failure"),
                    SyntaxKind.MethodDeclaration);
            }
        }

        // A bare --reference only asserts presence; a path loads the real assembly, which
        // is what source calling into that package needs in order to resolve at all.
        [Fact]
        public void Reference_ByPath_ResolvesTheAssemblysApis()
        {
            var file = Write("Uses.cs", @"
public class Uses
{
    public bool Matches(string text) =>
        new System.Text.RegularExpressions.Regex(""a"").IsMatch(text);
}");
            var regexDll = Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Text.RegularExpressions.dll");

            var (_, withoutStdout, _) = Run(file, "--format", "json");
            var (_, withStdout, _) = Run(file, "--reference", regexDll, "--format", "json");

            Assert.True(
                ParseJson(withoutStdout).GetProperty("summary").GetProperty("compileErrorCount").GetInt32() > 0,
                "the type should be unresolved without the reference");
            Assert.Equal(
                0,
                ParseJson(withStdout).GetProperty("summary").GetProperty("compileErrorCount").GetInt32());
        }

        // The same run can mix both forms: real APIs for one package, presence for another.
        [Fact]
        public void Reference_ByPathAndByName_CanBeCombined()
        {
            var file = Write("Mixed.cs", @"
using System.Threading.Tasks;

public class Mixed
{
    public bool Matches(string text) =>
        new System.Text.RegularExpressions.Regex(""a"").IsMatch(text);

    public async Task LoadAsync() { await Task.Yield(); }
}");
            var regexDll = Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Text.RegularExpressions.dll");

            var (_, stdout, _) = Run(
                file, "--reference", regexDll, "--reference", "UniTask", "--all-warn", "--format", "json");
            var root = ParseJson(stdout);

            Assert.Equal(0, root.GetProperty("summary").GetProperty("compileErrorCount").GetInt32());
            Assert.Contains(
                "UPA2010",
                root.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("id").GetString()));
        }

        // Patterns are expanded by the tool, so a documented command line behaves the same
        // in every shell — bash needs globstar for **, PowerShell expands nothing at all.
        [Fact]
        public void RecursivePattern_MatchesNestedFiles()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "Deep", "Nested"));
            Write("Top.cs", HotPathViolation.Replace("Probe", "Top"));
            File.WriteAllText(
                Path.Combine(_dir, "Deep", "Nested", "Buried.cs"),
                HotPathViolation.Replace("Probe", "Buried"));

            var (_, stdout, _) = Run(Path.Combine(_dir, "**", "*.cs"), "--format", "json");

            var files = ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                .Select(d => Path.GetFileName(d.GetProperty("file").GetString()!))
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "Buried.cs", "Top.cs" }, files);
        }

        [Fact]
        public void SingleStarPattern_StaysWithinOneDirectory()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "Deep"));
            Write("Top.cs", HotPathViolation.Replace("Probe", "Top"));
            File.WriteAllText(
                Path.Combine(_dir, "Deep", "Buried.cs"),
                HotPathViolation.Replace("Probe", "Buried"));

            var (_, stdout, _) = Run(Path.Combine(_dir, "*.cs"), "--format", "json");

            var files = ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                .Select(d => Path.GetFileName(d.GetProperty("file").GetString()!))
                .Distinct()
                .ToArray();

            Assert.Equal(new[] { "Top.cs" }, files);
        }

        [Fact]
        public void PatternMatchingNothing_IsAUsageError()
        {
            Write("Probe.cs", HotPathViolation);

            var (exitCode, stdout, stderr) = Run(Path.Combine(_dir, "*.fs"));

            Assert.Equal(2, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("No files matched", stderr);
        }

        // The same file reached twice (literally and by pattern) must be analyzed once.
        [Fact]
        public void OverlappingInputs_AreDeduplicated()
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (_, stdout, _) = Run(file, Path.Combine(_dir, "*.cs"), "--format", "json");

            var upa0001 = ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                .Count(d => d.GetProperty("id").GetString() == "UPA0001");

            Assert.Equal(1, upa0001);
        }

        // An option the code accepts but --help never mentions is an option nobody finds.
        [Theory]
        [InlineData("--reference")]
        [InlineData("--define")]
        [InlineData("--assembly-name")]
        [InlineData("--ruleset")]
        [InlineData("--editorconfig")]
        [InlineData("--additionalfile")]
        [InlineData("--unity-dll-dir")]
        [InlineData("--all-warn")]
        [InlineData("--whole-assembly")]
        [InlineData("--fail-on")]
        [InlineData("--format")]
        [InlineData("--list-rules")]
        [InlineData("--version")]
        [InlineData("--help")]
        [InlineData("-h")]
        public void Help_DocumentsEveryAcceptedOption(string option)
        {
            var (_, stdout, _) = Run("--help");

            Assert.Contains(option, stdout);
        }

        [Fact]
        public void Help_ShowsExamples()
        {
            var (_, stdout, _) = Run("--help");

            Assert.Contains("Examples:", stdout);
            Assert.Contains("upa-cli Assets/Scripts/Player.cs", stdout);
        }

        [Fact]
        public void Help_DocumentsTheApproximations()
        {
            var (exitCode, stdout, _) = Run("--help");

            Assert.Equal(0, exitCode);
            Assert.Contains("--whole-assembly", stdout);
            Assert.Contains("Exit codes", stdout);
            Assert.Contains("final authority", stdout);
        }
    }
}
