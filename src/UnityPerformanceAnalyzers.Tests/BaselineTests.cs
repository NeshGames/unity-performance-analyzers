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
    /// Covers the baseline: the contract that freezes a project's existing violations so only
    /// new ones are reported. Everything here is a frozen surface — the comparison key, the
    /// count semantics, the file format, the write contract — because a committed baseline
    /// keyed one way cannot be re-keyed without turning every recorded violation into a new
    /// one. Each failure mode below is silent by nature: a baseline that matches nothing looks
    /// exactly like a baseline with nothing to match.
    /// </summary>
    public sealed class BaselineTests : IDisposable
    {
        private readonly string _dir;

        public BaselineTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "upa-baseline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private const string OneViolation = @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        GetComponent<Rigidbody>();
    }
}";

        private const string TwoIdenticalInOneMember = @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        GetComponent<Rigidbody>();
        GetComponent<Rigidbody>();
    }
}";

        private const string TwoMembers = @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        GetComponent<Rigidbody>();
    }

    void LateUpdate()
    {
        GetComponent<Rigidbody>();
    }
}";

        private string Write(string name, string source)
        {
            var path = Path.Combine(_dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
            return path;
        }

        private string BaselinePathIn(string name = "upa-baseline.json") => Path.Combine(_dir, name);

        private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliEntryPoint.Run(args, stdout, stderr);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        private static JsonElement ParseJson(string stdout) => JsonDocument.Parse(stdout).RootElement;

        private static JsonElement Summary(string stdout) => ParseJson(stdout).GetProperty("summary");

        private static int DiagnosticCount(string stdout) =>
            ParseJson(stdout).GetProperty("diagnostics").GetArrayLength();

        /// <summary>Writes a baseline over the given files and asserts it succeeded.</summary>
        private string Freeze(string baseline, params string[] files)
        {
            var args = files.Concat(new[] { "--whole-assembly", "--write-baseline", baseline }).ToArray();
            var (exitCode, _, stderr) = Run(args);
            Assert.Equal(0, exitCode);
            Assert.Contains("baseline entries", stderr);
            return baseline;
        }

        private void WriteBaselineFile(string path, string json) => File.WriteAllText(path, json);

        private static string Entry(
            string file,
            string rule = "UPA0001",
            string type = "Probe",
            string member = "Update",
            string snippet = "GetComponent<Rigidbody>();",
            int count = 1) =>
            $@"{{ ""file"": ""{file}"", ""rule"": ""{rule}"", ""type"": ""{type}"",
                  ""member"": ""{member}"", ""snippet"": ""{snippet}"", ""count"": {count} }}";

        private static string Document(params string[] entries) =>
            $@"{{ ""schemaVersion"": 1, ""toolVersion"": ""0.7.0"",
                  ""entries"": [{string.Join(",", entries)}] }}";

        // Case 35
        [Fact]
        public void WrittenBaseline_SuppressesTheSameRunEntirely()
        {
            var file = Write("Probe.cs", OneViolation);
            var (_, before, _) = Run(file, "--whole-assembly", "--format", "json");
            var original = DiagnosticCount(before);
            Assert.True(original > 0, "the fixture must produce something to freeze");

            var baseline = Freeze(BaselinePathIn(), file);
            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(0, DiagnosticCount(stdout));
            Assert.Equal(original, Summary(stdout).GetProperty("baselineSuppressedCount").GetInt32());
        }

        // Case 36
        [Fact]
        public void ViolationAddedAfterTheBaseline_IsTheOnlyOneReported()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, TwoIdenticalInOneMember);
            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(1, exitCode);
            Assert.Equal(1, DiagnosticCount(stdout));
        }

        // Case 37 - count semantics: the key holds no line number, so both occurrences share
        // one key and set semantics would either leak or block the second forever.
        [Fact]
        public void TwoIdenticalOccurrences_AreBothSuppressed()
        {
            var file = Write("Probe.cs", TwoIdenticalInOneMember);
            var baseline = Freeze(BaselinePathIn(), file);

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(0, DiagnosticCount(stdout));
            Assert.Equal(2, Summary(stdout).GetProperty("baselineSuppressedCount").GetInt32());
        }

        // Case 38
        [Fact]
        public void ThirdOccurrenceOfABaselinedKey_IsReported()
        {
            var file = Write("Probe.cs", TwoIdenticalInOneMember);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, TwoIdenticalInOneMember.Replace(
                "        GetComponent<Rigidbody>();\r\n    }",
                "        GetComponent<Rigidbody>();\r\n        GetComponent<Rigidbody>();\r\n    }"));
            File.WriteAllText(file, @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        GetComponent<Rigidbody>();
        GetComponent<Rigidbody>();
        GetComponent<Rigidbody>();
    }
}");

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(1, exitCode);
            Assert.Equal(1, DiagnosticCount(stdout));
            Assert.Equal(2, Summary(stdout).GetProperty("baselineSuppressedCount").GetInt32());
        }

        // Case 41e - which occurrences survive is fixed, or the same source reports a
        // different location under a different build of the tool.
        [Fact]
        public void WhenQuotaRunsOut_TheLastOccurrencesAreReported()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, TwoIdenticalInOneMember);
            var (_, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            var reported = ParseJson(stdout).GetProperty("diagnostics").EnumerateArray().Single();
            var (_, full, _) = Run(file, "--whole-assembly", "--format", "json");
            var lines = ParseJson(full).GetProperty("diagnostics").EnumerateArray()
                .Where(d => d.GetProperty("id").GetString() == "UPA0001")
                .Select(d => d.GetProperty("line").GetInt32())
                .OrderBy(line => line)
                .ToArray();

            Assert.Equal(lines.Last(), reported.GetProperty("line").GetInt32());
        }

        // Case 39 - the argument's separator spelling must not change the key.
        [Fact]
        public void InputSeparatorSpelling_DoesNotChangeTheKey()
        {
            var file = Write(Path.Combine("Scripts", "Probe.cs"), OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            var alternate = Path.Combine(_dir, "Scripts/Probe.cs".Replace('/', Path.DirectorySeparatorChar));
            var (exitCode, stdout, _) = Run(alternate, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(0, DiagnosticCount(stdout));
        }

        // Case 39b - the anchor is the baseline's own directory, so where the command was run
        // from cannot change the outcome.
        [Fact]
        public void WorkingDirectory_DoesNotChangeTheOutcome()
        {
            var file = Write(Path.Combine("Scripts", "Probe.cs"), OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.Combine(_dir, "Scripts"));
                var (exitCode, stdout, _) = Run(
                    "Probe.cs", "--whole-assembly", "--baseline", baseline, "--format", "json");

                Assert.Equal(0, exitCode);
                Assert.Equal(0, DiagnosticCount(stdout));
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        // Case 39d - the recorded path is the disk's spelling, not the argument's, so a
        // baseline written on a case-insensitive file system still matches on a case-sensitive one.
        [Fact]
        public void RecordedPath_UsesTheSpellingOnDisk()
        {
            Write(Path.Combine("Scripts", "Probe.cs"), OneViolation);
            var mixedCase = Path.Combine(_dir, "scripts", "probe.cs");
            if (!File.Exists(mixedCase))
            {
                // A case-sensitive file system cannot pose this question at all.
                return;
            }

            var baseline = Freeze(BaselinePathIn(), mixedCase);

            Assert.Contains("\"file\": \"Scripts/Probe.cs\"", File.ReadAllText(baseline));
        }

        // Case 39c
        [Fact]
        public void FileOutsideTheBaselineDirectory_IsAUsageError()
        {
            var outside = Path.Combine(Path.GetTempPath(), "upa-outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            try
            {
                var file = Path.Combine(outside, "Probe.cs");
                File.WriteAllText(file, OneViolation);

                var (exitCode, _, stderr) = Run(
                    file, "--whole-assembly", "--write-baseline", BaselinePathIn());

                Assert.Equal(2, exitCode);
                Assert.Contains("outside the baseline directory", stderr);
            }
            finally
            {
                Directory.Delete(outside, recursive: true);
            }
        }

        // Case 40 - bulk reformatting is the most ordinary thing to do while adopting rules.
        [Fact]
        public void Reindenting_DoesNotBreakTheMatch()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, OneViolation.Replace(
                "        GetComponent<Rigidbody>();",
                "\t\t\tGetComponent<Rigidbody>();"));

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(0, DiagnosticCount(stdout));
        }

        // Case 41m - the whitespace character set is frozen; ASCII-space, char.IsWhiteSpace and
        // regex \s disagree over these, and a baseline travels between implementations.
        [Theory]
        [InlineData("	")]
        [InlineData("   ")]
        [InlineData(" ")]
        [InlineData(" ")]
        [InlineData("　")]
        public void NonAsciiWhitespace_NormalizesToTheSameKey(string whitespace)
        {
            // The baselined line already holds one ASCII space, so every variant below has to
            // collapse to that exact spelling rather than merely to some spelling of its own.
            var spaced = OneViolation.Replace(
                "GetComponent<Rigidbody>();", "GetComponent<Rigidbody> ();");
            var file = Write("Probe.cs", spaced);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, spaced.Replace(
                "GetComponent<Rigidbody> ();",
                $"GetComponent<Rigidbody>{whitespace}();"));

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(0, DiagnosticCount(stdout));
        }

        // Case 41 - the key holds no line number on purpose.
        [Fact]
        public void MovingAViolationDownTheFile_DoesNotBreakTheMatch()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, string.Concat(Enumerable.Repeat("// padding\n", 10)) + OneViolation);

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(0, DiagnosticCount(stdout));
        }

        // Case 41b - the enclosing symbol is what narrows the equal-exchange window from a
        // whole file down to one member.
        [Fact]
        public void IdenticalLinesInDifferentMembers_AreSuppressedIndependently()
        {
            var file = Write("Probe.cs", TwoMembers);
            var baseline = Freeze(BaselinePathIn(), file);

            var document = BaselineDocument.Read(baseline);
            var members = document.Entries
                .Where(e => e.Key.Rule == "UPA0001")
                .Select(e => e.Key.Member)
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "LateUpdate", "Update" }, members);
            Assert.All(document.Entries.Where(e => e.Key.Rule == "UPA0001"), e => Assert.Equal(1, e.Count));
        }

        // Case 41c - the accepted cost of keying on the symbol.
        [Fact]
        public void RenamingTheMember_MakesItANewViolation()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, OneViolation.Replace("void Update()", "void FixedUpdate()"));

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(1, exitCode);
            Assert.Equal(1, DiagnosticCount(stdout));
        }

        // Case 41d
        [Fact]
        public void ViolationsInLambdasAndLocalFunctions_BelongToTheEnclosingMember()
        {
            var file = Write("Probe.cs", @"
using System;
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        Action run = () => GetComponent<Rigidbody>();
        void Local() => GetComponent<Rigidbody>();
        run();
        Local();
    }
}");

            var baseline = Freeze(BaselinePathIn(), file);

            var members = BaselineDocument.Read(baseline).Entries
                .Where(e => e.Key.Rule == "UPA0001")
                .Select(e => e.Key.Member)
                .Distinct()
                .ToArray();

            Assert.Equal(new[] { "Update" }, members);
        }

        // Case 41k - top-level statements land on the synthesized entry point. An empty symbol
        // would pool every violation in such a file, and its local functions, into one bucket.
        [Fact]
        public void TopLevelStatements_GetANonEmptySymbol()
        {
            var file = Write("Program.cs", @"
using UnityEngine;

var probe = new GameObject();
Local();
void Local() { var x = new GameObject().GetComponent<Rigidbody>(); }
");

            var options = CliOptions.Parse(
                new[] { file, "--whole-assembly", "--write-baseline", BaselinePathIn() }, out _)!;
            var result = AnalysisRunner.Run(options);

            Assert.All(result.Diagnostics, d =>
            {
                Assert.NotEqual(string.Empty, d.Type);
                Assert.NotEqual(string.Empty, d.Member);
            });
        }

        // Case 41f / 41h / 41i - the shapes the type and member fields must keep apart.
        [Fact]
        public void NestedGenericAndExplicitInterfaceShapes_KeepDistinctKeys()
        {
            // UPA0005 fires in any method, so the shape of the symbol is what is under test
            // here rather than which methods a hot-path rule happens to reach.
            var file = Write("Shapes.cs", @"
using UnityEngine;

public interface INode { void Tick(); }

public sealed class Outer : INode
{
    public sealed class Node
    {
        public void Tick() { Debug.Log(""x""); }
    }

    void INode.Tick() { Debug.Log(""x""); }
}

public sealed class Other
{
    public sealed class Node
    {
        public void Tick() { Debug.Log(""x""); }
    }
}

public sealed class C
{
    public void Add() { Debug.Log(""x""); }
}

public sealed class C<T>
{
    public void Add() { Debug.Log(""x""); }
}");

            var options = CliOptions.Parse(
                new[] { file, "--all-warn", "--whole-assembly", "--write-baseline", BaselinePathIn() },
                out _)!;
            var keys = AnalysisRunner.Run(options).Diagnostics
                .Where(d => d.Id == "UPA0005")
                .Select(d => (d.Type, d.Member))
                .Distinct()
                .ToArray();

            Assert.Contains(("Outer.Node", "Tick"), keys);
            Assert.Contains(("Other.Node", "Tick"), keys);
            Assert.Contains(("Outer", "INode.Tick"), keys);
            Assert.Contains(("C", "Add"), keys);
            Assert.Contains(("C`1", "Add"), keys);
        }

        // Case 41g - members with no source name of their own.
        [Fact]
        public void ConstructorsAndOperators_UseTheirMetadataNames()
        {
            var file = Write("Members.cs", @"
using UnityEngine;

public sealed class Holder
{
    public Holder() { Debug.Log(""x""); }

    static Holder() { Debug.Log(""x""); }

    public static Holder operator +(Holder a, Holder b) { Debug.Log(""x""); return a; }
}");

            var options = CliOptions.Parse(
                new[] { file, "--all-warn", "--whole-assembly", "--write-baseline", BaselinePathIn() },
                out _)!;
            var members = AnalysisRunner.Run(options).Diagnostics
                .Where(d => d.Id == "UPA0005")
                .Select(d => d.Member)
                .ToArray();

            Assert.Contains(".ctor", members);
            Assert.Contains(".cctor", members);
            Assert.Contains("op_Addition", members);
        }

        // Case 41g - accessors report as get_/set_ methods; the key must name the property.
        // Getter and setter sharing one member also keeps them in one quota.
        [Fact]
        public void PropertyAndIndexerAccessors_AreKeyedByTheProperty()
        {
            var file = Write("Accessors.cs", @"
using UnityEngine;

public sealed class Holder
{
    public int Health
    {
        get
        {
            Debug.Log(""x"");
            return 0;
        }

        set
        {
            Debug.Log(""x"");
        }
    }

    public int this[int index]
    {
        get
        {
            Debug.Log(""x"");
            return 0;
        }
    }
}");

            var options = CliOptions.Parse(
                new[] { file, "--all-warn", "--whole-assembly", "--write-baseline", BaselinePathIn() },
                out _)!;
            var members = AnalysisRunner.Run(options).Diagnostics
                .Where(d => d.Id == "UPA0005")
                .Select(d => d.Member)
                .Distinct()
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "Health", "Item" }, members);
        }

        // Case 41j / 42d - the two collisions kept on purpose. Pinned so a later change to the
        // key cannot make them disappear unnoticed.
        [Fact]
        public void SameNamedOverloads_ShareOneKey()
        {
            var file = Write("Overloads.cs", @"
using UnityEngine;

public sealed class Probe
{
    void Work(int value)
    {
        Debug.Log(""x"");
    }

    void Work(string value)
    {
        Debug.Log(""x"");
    }
}");

            var options = CliOptions.Parse(
                new[] { file, "--all-warn", "--whole-assembly", "--write-baseline", BaselinePathIn() },
                out _)!;
            var work = AnalysisRunner.Run(options).Diagnostics
                .Where(d => d.Member == "Work" && d.Id == "UPA0005")
                .ToArray();

            Assert.Equal(2, work.Length);
            Assert.Single(work.Select(d => (d.Type, d.Member, d.Snippet)).Distinct());
        }

        [Fact]
        public void SameSimpleNamedInterfaces_ShareOneKey()
        {
            var file = Write("Interfaces.cs", @"
using UnityEngine;

namespace A { public interface IFoo { void M(); } }
namespace B { public interface IFoo { void M(); } }

public sealed class Probe : A.IFoo, B.IFoo
{
    void A.IFoo.M()
    {
        Debug.Log(""x"");
    }

    void B.IFoo.M()
    {
        Debug.Log(""x"");
    }
}");

            var options = CliOptions.Parse(
                new[] { file, "--all-warn", "--whole-assembly", "--write-baseline", BaselinePathIn() },
                out _)!;
            var members = AnalysisRunner.Run(options).Diagnostics
                .Where(d => d.Member.EndsWith("IFoo.M", StringComparison.Ordinal))
                .Select(d => (d.Type, d.Member, d.Snippet))
                .ToArray();

            Assert.Equal(2, members.Length);
            Assert.Single(members.Distinct());
        }

        // Case 42 / 42b - staleness counts occurrences, not vanished keys. Counting only keys
        // that disappeared entirely leaves a reusable quota that swallows later violations.
        [Fact]
        public void StaleCount_IsPerOccurrence()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, Document(Entry("Probe.cs", count: 5)));

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(4, Summary(stdout).GetProperty("baselineStaleCount").GetInt32());
        }

        [Fact]
        public void FixedViolation_IsStaleAndPrompted()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            File.WriteAllText(file, @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    private Rigidbody _body;

    void Awake() { _body = GetComponent<Rigidbody>(); }

    void Update() { }
}");

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline);

            Assert.Equal(0, exitCode);
            Assert.Contains("no longer match", stdout);
        }

        // Case 42e - analyzing one changed file is a normal invocation, and calling every other
        // file's entries stale would advise replacing a repository-wide contract with one file.
        [Fact]
        public void FilesNotAnalyzed_AreNotCountedStale()
        {
            var probe = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, Document(
                Entry("Probe.cs"),
                Entry("Other.cs", type: "Other", member: "Update")));

            var (_, stdout, _) = Run(probe, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, Summary(stdout).GetProperty("baselineStaleCount").GetInt32());
        }

        // Case 42h / 42i - unresolved types depress the diagnostic count, which inflates
        // staleness; acting on that number deletes quota that is still doing its job.
        [Fact]
        public void CompileErrors_MakeTheStaleCountUnavailable()
        {
            var file = Write("Probe.cs", @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        MissingType thing = null;
        GetComponent<Rigidbody>();
    }
}");

            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, Document(Entry("Probe.cs", count: 5)));

            var (_, stdout, _) = Run(file, "--baseline", baseline, "--format", "json");

            Assert.Equal(JsonValueKind.Null, Summary(stdout).GetProperty("baselineStaleCount").ValueKind);
            Assert.DoesNotContain("no longer match", stdout);
        }

        [Fact]
        public void AnalyzerFailure_MakesTheStaleCountUnavailable()
        {
            var file = Write("Probe.cs", OneViolation);
            var options = CliOptions.Parse(new[] { file, "--baseline", BaselinePathIn() }, out _)!;

            var result = AnalysisRunner.Run(
                options, ImmutableArray.Create<DiagnosticAnalyzer>(new ThrowingAnalyzer()));

            Assert.False(result.IsComplete);
            var outcome = BaselineFilter.Apply(
                result.Diagnostics,
                new BaselineDocument(ImmutableArray<BaselineEntry>.Empty),
                result.AnalyzedFiles,
                result.IsComplete);

            Assert.Null(outcome.StaleCount);
        }

        // Case 42c - once regenerated, the quota has converged and later additions all report.
        [Fact]
        public void AfterRegenerating_TheQuotaNoLongerAbsorbsNewViolations()
        {
            var file = Write("Probe.cs", TwoIdenticalInOneMember);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, Document(Entry("Probe.cs", count: 5)));

            Freeze(baseline, file);
            File.WriteAllText(file, @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        GetComponent<Rigidbody>();
        GetComponent<Rigidbody>();
        GetComponent<Rigidbody>();
        GetComponent<Rigidbody>();
    }
}");

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(1, exitCode);
            Assert.Equal(2, DiagnosticCount(stdout));
        }

        // Case 41n / 41p - a deleted or renamed file can never appear in a successful run
        // again, so refusing on its behalf would lock the baseline out of regeneration for good.
        [Fact]
        public void DeletedFile_IsDroppedOnRegeneration()
        {
            var a = Write("A.cs", OneViolation);
            var b = Write("B.cs", OneViolation.Replace("Probe", "Second"));
            var baseline = Freeze(BaselinePathIn(), a, b);

            File.Delete(b);
            Freeze(baseline, a);

            var files = BaselineDocument.Read(baseline).Entries
                .Select(e => e.Key.File)
                .Distinct()
                .ToArray();

            Assert.Equal(new[] { "A.cs" }, files);
        }

        [Fact]
        public void RenamedFile_AppearsUnderItsNewPath()
        {
            var a = Write("A.cs", OneViolation);
            var b = Write("B.cs", OneViolation.Replace("Probe", "Second"));
            var baseline = Freeze(BaselinePathIn(), a, b);

            var c = Path.Combine(_dir, "C.cs");
            File.Move(b, c);
            Freeze(baseline, a, c);

            var files = BaselineDocument.Read(baseline).Entries
                .Select(e => e.Key.File)
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "A.cs", "C.cs" }, files);
        }

        // Case 41q - a partial run must not replace a repository-wide contract.
        [Fact]
        public void PartialInput_RefusesToOverwriteAndLeavesTheFileByteIdentical()
        {
            var a = Write("A.cs", OneViolation);
            var b = Write("B.cs", OneViolation.Replace("Probe", "Second"));
            var baseline = Freeze(BaselinePathIn(), a, b);
            var before = File.ReadAllBytes(baseline);

            var (exitCode, _, stderr) = Run(a, "--whole-assembly", "--write-baseline", baseline);

            Assert.Equal(2, exitCode);
            Assert.Contains("B.cs", stderr);
            Assert.Equal(before, File.ReadAllBytes(baseline));
        }

        // Case 43 / 44 / 44b - refusing to freeze a run that already said it was incomplete.
        [Fact]
        public void WritingWithoutWholeAssembly_IsRefused()
        {
            var file = Write("Probe.cs", OneViolation);

            var (exitCode, _, stderr) = Run(file, "--write-baseline", BaselinePathIn());

            Assert.Equal(2, exitCode);
            Assert.Contains("--whole-assembly", stderr);
            Assert.False(File.Exists(BaselinePathIn()));
        }

        [Fact]
        public void WritingWithCompileErrors_IsRefused()
        {
            var file = Write("Broken.cs", @"
using UnityEngine;

public sealed class Broken : MonoBehaviour
{
    void Update() { MissingType thing = null; }
}");

            var (exitCode, _, _) = Run(file, "--whole-assembly", "--write-baseline", BaselinePathIn());

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(BaselinePathIn()));
        }

        [Fact]
        public void WritingAfterAnAnalyzerFailure_IsRefused()
        {
            var file = Write("Probe.cs", OneViolation);
            var options = CliOptions.Parse(
                new[] { file, "--whole-assembly", "--write-baseline", BaselinePathIn() }, out _)!;

            var result = AnalysisRunner.Run(
                options, ImmutableArray.Create<DiagnosticAnalyzer>(new ThrowingAnalyzer()));

            var error = Assert.Throws<CliException>(
                () => BaselineWriter.EnsureRunIsWritable(options, result));
            Assert.Contains("failed to run", error.Message);
            Assert.False(File.Exists(BaselinePathIn()));
        }

        // Case 45
        [Fact]
        public void BothBaselineOptions_IsAUsageError()
        {
            var file = Write("Probe.cs", OneViolation);

            var (exitCode, _, stderr) = Run(
                file, "--baseline", BaselinePathIn(), "--write-baseline", BaselinePathIn("other.json"));

            Assert.Equal(2, exitCode);
            Assert.Contains("cannot be given together", stderr);
        }

        // Case 46 series - validation before comparison. Every one of these would otherwise
        // suppress too much or too little while the run looked entirely normal.
        [Theory]
        [InlineData(@"{ ""schemaVersion"": 2, ""entries"": [] }", "Upgrade upa-cli")]
        [InlineData(@"{ ""entries"": [] }", "positive integer")]
        [InlineData(@"{ ""schemaVersion"": 0, ""entries"": [] }", "positive integer")]
        [InlineData(@"{ ""schemaVersion"": -1, ""entries"": [] }", "positive integer")]
        [InlineData(@"{ ""schemaVersion"": 1 }", "not an array")]
        [InlineData(@"not json at all", "not valid JSON")]
        public void MalformedBaseline_IsAUsageError(string json, string expected)
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, json);

            var (exitCode, _, stderr) = Run(file, "--baseline", baseline);

            Assert.Equal(2, exitCode);
            Assert.Contains(expected, stderr);
        }

        [Theory]
        [InlineData(@"""count"": 0", "between 1")]
        [InlineData(@"""count"": -3", "between 1")]
        [InlineData(@"""count"": 2000000", "between 1")]
        public void MalformedCount_IsAUsageError(string tail, string expected)
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, $@"{{ ""schemaVersion"": 1, ""entries"": [
                {{ ""file"": ""Probe.cs"", ""rule"": ""UPA0001"", ""type"": ""Probe"",
                   ""member"": ""Update"", ""snippet"": ""x"", {tail} }}] }}");

            var (exitCode, _, stderr) = Run(file, "--baseline", baseline);

            Assert.Equal(2, exitCode);
            Assert.Contains(expected, stderr);
        }

        [Fact]
        public void MissingCount_IsAUsageError()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, @"{ ""schemaVersion"": 1, ""entries"": [
                { ""file"": ""Probe.cs"", ""rule"": ""UPA0001"", ""type"": ""Probe"",
                  ""member"": ""Update"", ""snippet"": ""x"" }] }");

            var (exitCode, _, stderr) = Run(file, "--baseline", baseline);

            Assert.Equal(2, exitCode);
            Assert.Contains("'count'", stderr);
        }

        // Case 46c
        [Fact]
        public void MissingBaselineFile_IsAUsageErrorNotAnEmptyBaseline()
        {
            var file = Write("Probe.cs", OneViolation);

            var (exitCode, _, stderr) = Run(file, "--baseline", BaselinePathIn("absent.json"));

            Assert.Equal(2, exitCode);
            Assert.Contains("not found", stderr);
        }

        // Case 46e - neither summing nor last-wins; both silently over-suppress.
        [Fact]
        public void DuplicateEntries_AreAUsageError()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, Document(Entry("Probe.cs"), Entry("Probe.cs")));

            var (exitCode, _, stderr) = Run(file, "--baseline", baseline);

            Assert.Equal(2, exitCode);
            Assert.Contains("duplicate entry", stderr);
        }

        [Fact]
        public void DuplicatePropertyNames_AreAUsageError()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, @"{ ""schemaVersion"": 1, ""schemaVersion"": 1, ""entries"": [] }");

            var (exitCode, _, stderr) = Run(file, "--baseline", baseline);

            Assert.Equal(2, exitCode);
            Assert.Contains("appears twice", stderr);
        }

        // Case 44c / 46g - the stored path must already be canonical, or two spellings of one
        // file slip past duplicate detection as distinct raw tuples.
        [Theory]
        [InlineData(@"Assets\\A.cs")]
        [InlineData("Assets/./A.cs")]
        [InlineData("Assets//A.cs")]
        [InlineData("../A.cs")]
        [InlineData("/absolute/A.cs")]
        [InlineData("C:/absolute/A.cs")]
        public void NonCanonicalPath_IsAUsageError(string stored)
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            WriteBaselineFile(baseline, Document(Entry(stored)));

            var (exitCode, _, stderr) = Run(file, "--baseline", baseline);

            Assert.Equal(2, exitCode);
            Assert.Contains("normalized relative path", stderr);
        }

        // A baseline of hostile provenance must not overflow the aggregate into a negative
        // number, which reads as "nothing stale" and silently drops the safeguard.
        [Fact]
        public void HugeStaleCounts_DoNotOverflow()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = BaselinePathIn();
            var entries = Enumerable.Range(0, 2500)
                .Select(i => Entry("Probe.cs", member: $"M{i}", count: 1_000_000))
                .ToArray();
            WriteBaselineFile(baseline, Document(entries));

            var (_, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(
                2500L * 1_000_000L,
                Summary(stdout).GetProperty("baselineStaleCount").GetInt64());
        }

        // Case 46f - the one lenient rule, so a later version can add a field without
        // invalidating baselines already committed.
        [Fact]
        public void UnknownFields_AreIgnored()
        {
            var file = Write("Probe.cs", OneViolation);
            var baseline = Freeze(BaselinePathIn(), file);

            var text = File.ReadAllText(baseline).Replace(
                @"""count"":", @"""futureField"": {""nested"": true}, ""count"":");
            WriteBaselineFile(baseline, text);

            var (exitCode, stdout, _) = Run(file, "--whole-assembly", "--baseline", baseline, "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.Equal(0, DiagnosticCount(stdout));
        }

        // Case 47 - regenerating an unchanged baseline must produce an unchanged file, or it
        // cannot be reviewed in a diff.
        [Fact]
        public void RegeneratingTwice_ProducesIdenticalBytes()
        {
            var file = Write("Probe.cs", TwoMembers);
            var baseline = Freeze(BaselinePathIn(), file);
            var first = File.ReadAllBytes(baseline);

            Freeze(baseline, file);

            Assert.Equal(first, File.ReadAllBytes(baseline));
        }

        [Fact]
        public void WrittenFile_HasNoBomAndUnixNewlines()
        {
            var file = Write("Probe.cs", OneViolation);
            var bytes = File.ReadAllBytes(Freeze(BaselinePathIn(), file));

            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.DoesNotContain((byte)'\r', bytes);
        }

        // Plain text is the point: the default JSON encoder writes angle brackets and
        // non-ASCII as escape sequences, making an ordinary line of C# unreviewable in a diff.
        [Fact]
        public void WrittenSnippets_AreNotEscapedIntoUnreadability()
        {
            var file = Write("Probe.cs", OneViolation);
            var text = File.ReadAllText(Freeze(BaselinePathIn(), file));

            Assert.Contains("GetComponent<Rigidbody>();", text);
            Assert.DoesNotContain("u003C", text);
        }

        // Case 47d - everything reported becomes the contract at this moment, so applying
        // --fail-on would make freezing existing debt an operation that always fails.
        [Fact]
        public void SuccessfulWrite_ExitsZeroDespiteWarnings()
        {
            var file = Write("Probe.cs", OneViolation);

            var (exitCode, stdout, _) = Run(
                file, "--whole-assembly", "--write-baseline", BaselinePathIn(), "--format", "json");

            Assert.Equal(0, exitCode);
            Assert.True(DiagnosticCount(stdout) > 0, "diagnostics are still reported");
        }

        // Case 47b - a failed write leaves nothing behind.
        [Fact]
        public void FailedWrite_ReportsAnErrorAndLeavesNoTemporaryFile()
        {
            var file = Write("Probe.cs", OneViolation);
            var target = Path.Combine(_dir, "occupied");
            Directory.CreateDirectory(target);

            var (exitCode, _, stderr) = Run(file, "--whole-assembly", "--write-baseline", target);

            Assert.Equal(2, exitCode);
            Assert.Contains("Failed to write", stderr);
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        // A dangling link is the case that matters: File.Exists answers false for it, so a
        // guard built on that would replace a link someone placed on purpose.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SymlinkTarget_IsRefused(bool dangling)
        {
            var file = Write("Probe.cs", OneViolation);
            var target = Path.Combine(_dir, "linked-baseline.json");
            var destination = Path.Combine(_dir, "real-baseline.json");
            if (!dangling)
            {
                File.WriteAllText(destination, "{}");
            }

            try
            {
                File.CreateSymbolicLink(target, destination);
            }
            catch (Exception)
            {
                // Creating links needs a privilege this machine may not grant.
                return;
            }

            var (exitCode, _, stderr) = Run(file, "--whole-assembly", "--write-baseline", target);

            Assert.Equal(2, exitCode);
            Assert.Contains("symbolic link", stderr);
            Assert.Equal(destination, new FileInfo(target).LinkTarget);
        }

        [DiagnosticAnalyzer(LanguageNames.CSharp)]
        private sealed class ThrowingAnalyzer : DiagnosticAnalyzer
        {
            private static readonly DiagnosticDescriptor Rule = new(
                "UPATEST98",
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
    }
}
