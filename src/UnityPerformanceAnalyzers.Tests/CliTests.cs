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

        /// <summary>
        /// Reports UPA0016, whose message contains commas — which the workflow-command syntax
        /// treats as a property separator.
        /// </summary>
        private const string CommaBearingViolation = @"
using UnityEngine;

public class Messenger : MonoBehaviour
{
    void Update()
    {
        SendMessage(""Ping"");
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
        public void UnknownFormat_IsAUsageError()
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (exitCode, _, stderr) = Run(file, "--format", "xml");

            Assert.Equal(2, exitCode);
            Assert.Contains("text|json|sarif|github", stderr);
        }

        // Case 12e - the catalog has no findings to render, and silently substituting text in
        // a mode chosen for machine consumption is worse than refusing.
        [Fact]
        public void ListRules_RejectsTheFindingFormats()
        {
            foreach (var format in new[] { "sarif", "github" })
            {
                var (exitCode, stdout, stderr) = Run("--list-rules", "--format", format);

                Assert.Equal(2, exitCode);
                Assert.Contains("--list-rules supports --format text|json only", stderr);
                Assert.Equal(string.Empty, stdout.Trim());
            }
        }

        // Case 12b - SARIF is what every code-scanning service reads; the tool's own JSON has
        // no consumers but this repository.
        [Fact]
        public void SarifFormat_CarriesTheRequiredShape()
        {
            var file = Write("Probe.cs", HotPathViolation);

            var (exitCode, stdout, _) = Run(file, "--format", "sarif");
            var root = ParseJson(stdout);

            Assert.Equal(1, exitCode);
            Assert.Equal("2.1.0", root.GetProperty("version").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("$schema").GetString()));

            var run = root.GetProperty("runs")[0];
            var driver = run.GetProperty("tool").GetProperty("driver");
            Assert.Equal("unity-performance-analyzers", driver.GetProperty("name").GetString());
            Assert.False(string.IsNullOrWhiteSpace(driver.GetProperty("semanticVersion").GetString()));

            var rule = driver.GetProperty("rules")[0];
            Assert.Equal("UPA0001", rule.GetProperty("id").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                rule.GetProperty("shortDescription").GetProperty("text").GetString()));
            Assert.Contains("UPA0001.md", rule.GetProperty("helpUri").GetString());
            Assert.Equal("warning", rule.GetProperty("defaultConfiguration").GetProperty("level").GetString());

            var result = run.GetProperty("results")[0];
            Assert.Equal("UPA0001", result.GetProperty("ruleId").GetString());
            Assert.Equal("warning", result.GetProperty("level").GetString());
            Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("message").GetProperty("text").GetString()));

            var region = result.GetProperty("locations")[0]
                .GetProperty("physicalLocation").GetProperty("region");
            Assert.True(region.GetProperty("startLine").GetInt32() > 0);
            Assert.True(region.GetProperty("startColumn").GetInt32() > 0);
        }

        // Case 12c - a Windows path uploads without error and then matches no file in the
        // repository, so the separator is part of the contract. Asserted end to end rather
        // than on the writer: input paths are already forward-slashed by the pattern expander,
        // so breaking the writer alone leaves this green. What must not regress is the shape
        // that leaves the tool, whichever layer produces it.
        [Fact]
        public void SarifUris_UseForwardSlashes()
        {
            var file = Write("Probe.cs", HotPathViolation);
            if (Path.DirectorySeparatorChar == '\\')
            {
                // Non-vacuity: on Windows the path this test hands in genuinely has separators
                // to normalize. On POSIX there is nothing to convert and the assertion below
                // only pins that nothing introduces one.
                Assert.Contains(@"\", file);
            }

            var (_, stdout, _) = Run(file, "--format", "sarif");
            var uri = ParseJson(stdout).GetProperty("runs")[0].GetProperty("results")[0]
                .GetProperty("locations")[0].GetProperty("physicalLocation")
                .GetProperty("artifactLocation").GetProperty("uri").GetString();

            Assert.DoesNotContain(@"\", uri);
        }

        // Case 12d - the workflow-command syntax is line based and comma separated, so an
        // unescaped message truncates the annotation at the first comma or newline. The probe
        // reports UPA0016, whose message contains commas: a rule whose wording happens to have
        // none would let this pass with no escaping at all.
        [Fact]
        public void GithubFormat_EmitsEscapedWorkflowCommands()
        {
            var file = Write("Probe.cs", CommaBearingViolation);

            var (exitCode, stdout, _) = Run(file, "--format", "github");
            var (_, json, _) = Run(file, "--format", "json");

            Assert.Equal(1, exitCode);
            var line = stdout
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .First(l => l.StartsWith("::", StringComparison.Ordinal));
            Assert.StartsWith("::warning file=", line);
            Assert.Contains("line=", line);
            Assert.Contains("title=UPA0016", line);

            // Non-vacuity: the same finding rendered as JSON carries both characters raw.
            var raw = ParseJson(json).GetProperty("diagnostics")[0].GetProperty("message").GetString()!;
            Assert.Contains(",", raw);

            // The properties escape ',' and ':', which is what keeps a comma in the title from
            // ending it early.
            var separator = line.IndexOf("::", 2, StringComparison.Ordinal);
            var properties = line.Substring(2, separator - 2);
            var title = properties.Substring(properties.IndexOf("title=", StringComparison.Ordinal));
            Assert.StartsWith("title=UPA0016%3A ", title);

            // The data does not: the runner unescapes ',' and ':' only inside properties, so
            // escaping them in the message printed a literal %2C in every annotation. What the
            // reader sees is the message exactly as the JSON carries it.
            var message = line.Substring(separator + 2);
            Assert.StartsWith(raw, message);
            Assert.DoesNotContain("%2C", message);
            Assert.DoesNotContain("%3A", message);
        }

        // actions/toolkit's escapeData and escapeProperty, character for character.
        [Theory]
        [InlineData("a,b:c", "a,b:c", "a%2Cb%3Ac")]
        [InlineData("50%", "50%25", "50%25")]
        [InlineData("one\r\ntwo", "one%0D%0Atwo", "one%0D%0Atwo")]
        [InlineData("%2C", "%252C", "%252C")]
        public void GithubEscapes_MatchTheActionsToolkit(string value, string data, string property)
        {
            Assert.Equal(data, OutputWriter.EscapeData(value));
            Assert.Equal(property, OutputWriter.EscapeProperty(value));
        }

        // GitHub resolves an annotation's file against the repository root. A path as given
        // only lands when the tool ran from that root with relative paths.
        [Fact]
        public void GithubFile_IsRelativeToTheWorkspace()
        {
            var workspace = Path.Combine(_dir, "repo");
            var file = Path.Combine(workspace, "Assets", "Scripts", "Probe.cs");

            Assert.Equal("Assets/Scripts/Probe.cs", OutputWriter.GithubFile(file, workspace));
            Assert.Equal(
                "Assets/Scripts/Probe.cs",
                OutputWriter.GithubFile(file, workspace + Path.DirectorySeparatorChar));
        }

        [Fact]
        public void GithubFile_OutsideTheWorkspace_IsAsGiven()
        {
            var workspace = Path.Combine(_dir, "repo");
            var outside = Path.Combine(_dir, "repo-other", "Probe.cs");

            Assert.Equal(outside.Replace('\\', '/'), OutputWriter.GithubFile(outside, workspace));
            Assert.Equal("Assets/Probe.cs", OutputWriter.GithubFile("Assets/Probe.cs", null));
            Assert.Equal("Assets/Probe.cs", OutputWriter.GithubFile("Assets/Probe.cs", string.Empty));
        }

        // End to end, through the variable the runner actually sets. The file is passed
        // absolute, which is the case that used to annotate nothing.
        [Fact]
        public void GithubFormat_NamesFilesRelativeToGithubWorkspace()
        {
            var workspace = Path.Combine(_dir, "repo");
            Directory.CreateDirectory(Path.Combine(workspace, "Assets"));
            var file = Path.Combine(workspace, "Assets", "Probe.cs");
            File.WriteAllText(file, HotPathViolation);

            var previous = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
            try
            {
                Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", workspace);
                var (exitCode, stdout, _) = Run(file, "--format", "github");

                Assert.Equal(1, exitCode);
                Assert.Contains("::warning file=Assets/Probe.cs,line=", stdout);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previous);
            }
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

            Assert.DoesNotContain(rules, r => r.GetProperty("compilationWide").GetBoolean());

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

        private const string UnsafeSource = @"
public static class Pointers
{
    public static unsafe int First(int[] values)
    {
        fixed (int* p = values) { return *p; }
    }
}";

        // An assembly with allowUnsafeCode is ordinary in Unity - Burst, NativeArray pointer
        // access, ZString - and without the switch every pointer is CS0227, which under
        // --whole-assembly is an error exit and a baseline that can never be written.
        [Fact]
        public void UnsafeCode_IsACompileErrorWithoutTheSwitch()
        {
            var file = Write("Pointers.cs", UnsafeSource);

            var (exitCode, stdout, stderr) = Run(file, "--whole-assembly", "--format", "json");

            Assert.Equal(2, exitCode);
            Assert.Contains("CS0227", stderr);
            Assert.Contains(
                ParseJson(stdout).GetProperty("compileErrors").EnumerateArray(),
                e => e.GetProperty("id").GetString() == "CS0227");
        }

        [Fact]
        public void UnsafeSwitch_CompilesUnsafeCode()
        {
            var file = Write("Pointers.cs", UnsafeSource);

            var (exitCode, stdout, stderr) = Run(file, "--whole-assembly", "--unsafe", "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("compile error", stderr);
            Assert.Equal(0, ParseJson(stdout).GetProperty("summary").GetProperty("compileErrorCount").GetInt32());
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

        [Fact]
        public void AdditionalFile_CarriesAnalyzerOptions()
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
            var optionsFile = Write(
                "Rules.UnityPerformanceAnalyzers.additionalfile",
                "upa_hot_path_messages = Tick");

            var (_, defaultStdout, _) = Run(file, "--format", "json");
            var (_, configuredStdout, _) = Run(
                file, "--additionalfile", optionsFile, "--format", "json");

            Assert.DoesNotContain(
                "UPA0006",
                ParseJson(defaultStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
            Assert.Contains(
                "UPA0006",
                ParseJson(configuredStdout).GetProperty("diagnostics").EnumerateArray()
                    .Select(d => d.GetProperty("id").GetString()));
        }

        [Fact]
        public void EditorConfigOption_IsNoLongerAccepted()
        {
            var file = Write("Probe.cs", HotPathViolation);
            var config = Write(".editorconfig", "root = true");

            var (exitCode, stdout, stderr) = Run(file, "--editorconfig", config);

            Assert.Equal(2, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("Unknown option '--editorconfig'", stderr);
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
        [InlineData("--additionalfile")]
        [InlineData("--unity-dll-dir")]
        [InlineData("--all-warn")]
        [InlineData("--whole-assembly")]
        [InlineData("--unsafe")]
        [InlineData("--only")]
        [InlineData("--only-from")]
        [InlineData("--baseline")]
        [InlineData("--write-baseline")]
        [InlineData("--prune-baseline")]
        [InlineData("--report-stale-baseline")]
        [InlineData("--fail-on-stale")]
        [InlineData("--init-args")]
        [InlineData("--project")]
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
        public void Help_DocumentsTheApproximations()
        {
            var (exitCode, stdout, _) = Run("--help");

            Assert.Equal(0, exitCode);
            Assert.Contains("--whole-assembly", stdout);
            Assert.Contains("Exit codes", stdout);
            Assert.Contains("final authority", stdout);
        }

        // --only narrows the report, not the compilation: a caller that changed one file hears
        // about that file, with every other input still there to resolve its symbols.
        private string WriteViolation(string name, string className) =>
            Write(name, HotPathViolation.Replace("class Probe", "class " + className));

        private static string[] ReportedFiles(string stdout) =>
            ParseJson(stdout).GetProperty("diagnostics").EnumerateArray()
                .Select(d => Path.GetFileName(d.GetProperty("file").GetString()!))
                .Distinct()
                .ToArray();

        [Fact]
        public void Only_ReportsTheNamedFileAndNothingElse()
        {
            var changed = WriteViolation("Changed.cs", "Changed");
            var untouched = WriteViolation("Untouched.cs", "Untouched");

            var (exitCode, stdout, _) = Run(changed, untouched, "--only", changed, "--format", "json");

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Changed.cs" }, ReportedFiles(stdout));
        }

        [Fact]
        public void Only_DecidesTheExitCodeFromTheNamedFilesAlone()
        {
            var clean = Write("Quiet.cs", Clean);
            var untouched = WriteViolation("Untouched.cs", "Untouched");

            var (exitCode, stdout, _) = Run(clean, untouched, "--only", clean, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Empty(ReportedFiles(stdout));
        }

        [Fact]
        public void Only_ReadsAListAndSkipsWhatAChangeListLegitimatelyHolds()
        {
            var changed = WriteViolation("Changed.cs", "Changed");
            var untouched = WriteViolation("Untouched.cs", "Untouched");
            var list = Write("changed.txt", string.Join("\n",
                "# from git diff --name-only",
                changed,
                Path.Combine(_dir, "Deleted.cs"),
                Path.Combine(_dir, "Probe.prefab")));

            var (exitCode, stdout, stderr) = Run(changed, untouched, "--only-from", list, "--format", "json");

            Assert.True(exitCode == 1, stderr);
            Assert.Equal(new[] { "Changed.cs" }, ReportedFiles(stdout));
        }

        [Fact]
        public void Only_RefusesAFileThatWasNotAnalyzed()
        {
            var analyzed = WriteViolation("Analyzed.cs", "Analyzed");
            var outside = WriteViolation("Outside.cs", "Outside");

            var (exitCode, _, stderr) = Run(analyzed, "--only", outside);

            Assert.Equal(2, exitCode);
            Assert.Contains("not one of the analyzed files", stderr);
        }

        [Fact]
        public void Only_RefusesToWriteABaseline()
        {
            var file = WriteViolation("Changed.cs", "Changed");

            var (exitCode, _, stderr) = Run(
                file, "--whole-assembly", "--only", file, "--write-baseline", Path.Combine(_dir, "b.json"));

            Assert.Equal(2, exitCode);
            Assert.Contains("--write-baseline", stderr);
        }

        [Fact]
        public void Only_WithAPathThatIsNotThere_IsRefusedRatherThanReportedClean()
        {
            var analyzed = WriteViolation("Analyzed.cs", "Analyzed");

            var (exitCode, _, stderr) = Run(analyzed, "--only", Path.Combine(_dir, "Analyzd.cs"));

            Assert.Equal(2, exitCode);
            Assert.Contains("no C# file", stderr);
        }

        // git diff --name-only prints repository-relative paths; run from a Unity project in a
        // subfolder, none of them resolve, and an empty narrowing would read as a clean run.
        [Fact]
        public void OnlyFrom_WhenNoListedCSharpFileResolves_IsRefused()
        {
            var analyzed = WriteViolation("Analyzed.cs", "Analyzed");
            var list = Write("changed.txt", string.Join("\n",
                "Game/Assets/Scripts/Analyzed.cs",
                "Game/Assets/Scripts/Other.cs",
                "README.md"));

            var (exitCode, _, stderr) = Run(analyzed, "--only-from", list);

            Assert.Equal(2, exitCode);
            Assert.Contains("--relative", stderr);
        }

        [Fact]
        public void OnlyFrom_WithNoCSharpChanges_ReportsNothingAndPasses()
        {
            var analyzed = WriteViolation("Analyzed.cs", "Analyzed");
            var list = Write("changed.txt", "README.md\nAssets/Scene.unity");

            var (exitCode, stdout, stderr) = Run(analyzed, "--only-from", list, "--format", "json");

            Assert.True(exitCode == 0, stderr);
            Assert.Empty(ReportedFiles(stdout));
        }
    }
}
