using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityPerformanceAnalyzers.Cli;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Response files and compile-error detail.
    ///
    /// Both exist because of what happened when the documented gate setup was run against a
    /// real assembly: Unity's own compile arguments for one package come to about 34,000
    /// characters once translated into this tool's flags, and Windows will not start a
    /// process past 32,767. The run that would have told you why was refused with a count
    /// and nothing else.
    /// </summary>
    public sealed class ResponseFileTests : IDisposable
    {
        private const string HotPathViolation = @"
using UnityEngine;

public class Probe : MonoBehaviour
{
    void Update()
    {
        var body = GetComponent<Rigidbody>();
    }
}";

        // Calls into a type nobody supplied, which is exactly how a real project fails: a
        // package assembly is missing and every use of it becomes an unresolved name.
        private const string Unresolvable = @"
using UnityEngine;
using SomePackage.That.Does.Not.Exist;

public class Broken : MonoBehaviour
{
    void Update()
    {
        Missing.Call();
    }
}";

        private readonly string _dir;

        public ResponseFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "upa-rsp-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private string Write(string name, string content)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliEntryPoint.Run(args, stdout, stderr);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        // 48 — the whole point: a response file must be indistinguishable from typing it out.
        [Fact]
        public void ArgumentsFromAResponseFileMatchTheSameArgumentsTypedOut()
        {
            var source = Write("Probe.cs", HotPathViolation);
            var rsp = Write("args.rsp", source + Environment.NewLine + "--define" + Environment.NewLine + "SOMETHING");

            var direct = Run(source, "--define", "SOMETHING", "--format", "json");
            var viaFile = Run("@" + rsp, "--format", "json");

            Assert.Equal(direct.ExitCode, viaFile.ExitCode);
            Assert.Equal(direct.Stdout, viaFile.Stdout);
        }

        // 49
        [Fact]
        public void BlankLinesWhitespaceLinesAndCommentsAreIgnored()
        {
            var source = Write("Probe.cs", HotPathViolation);
            var rsp = Write("args.rsp", string.Join(Environment.NewLine, new[]
            {
                "# the file this gate analyzes",
                "",
                "   ",
                source,
                "   # indented comments count as comments",
            }));

            var (exitCode, stdout, _) = Run("@" + rsp, "--format", "json");

            Assert.Equal(1, exitCode);
            var diagnostics = JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics");
            Assert.Equal(1, diagnostics.GetArrayLength());
        }

        // 50 — refused rather than resolved; see the specification for why.
        [Fact]
        public void AResponseFileCannotReferenceAnother()
        {
            var inner = Write("inner.rsp", "--all-warn");
            var outer = Write("outer.rsp", "@" + inner);

            var (exitCode, stdout, stderr) = Run("@" + outer);

            Assert.Equal(2, exitCode);
            Assert.Contains("cannot reference another", stderr, StringComparison.Ordinal);
            Assert.Empty(stdout);
        }

        // 51
        [Fact]
        public void AMissingResponseFileNamesThePath()
        {
            var missing = Path.Combine(_dir, "nope.rsp");

            var (exitCode, _, stderr) = Run("@" + missing);

            Assert.Equal(2, exitCode);
            Assert.Contains("nope.rsp", stderr, StringComparison.Ordinal);
        }

        // 52
        [Fact]
        public void ABareAtSignIsAUsageError()
        {
            var (exitCode, _, stderr) = Run("@");

            Assert.Equal(2, exitCode);
            Assert.Contains("@", stderr, StringComparison.Ordinal);
        }

        // 53
        [Fact]
        public void AnEmptyResponseFileContributesNothing()
        {
            var source = Write("Probe.cs", HotPathViolation);
            var rsp = Write("empty.rsp", string.Empty);

            var (exitCode, stdout, _) = Run("@" + rsp, source, "--format", "json");

            Assert.Equal(1, exitCode);
            var diagnostics = JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics");
            Assert.Equal(1, diagnostics.GetArrayLength());
        }

        // 54 — no quoting: the line is the argument, spaces and all.
        [Fact]
        public void APathContainingSpacesNeedsNoQuoting()
        {
            var directory = Path.Combine(_dir, "with spaces");
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "Probe.cs");
            File.WriteAllText(source, HotPathViolation);
            var rsp = Write("args.rsp", source);

            var (exitCode, stdout, _) = Run("@" + rsp, "--format", "json");

            Assert.Equal(1, exitCode);
            var diagnostics = JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics");
            Assert.Equal(1, diagnostics.GetArrayLength());
        }

        // 55 — expansion is positional, so precedence is unchanged by where an argument lived.
        [Fact]
        public void ArgumentsAfterTheResponseFileStillWin()
        {
            var source = Write("Probe.cs", HotPathViolation);
            var rsp = Write("args.rsp", string.Join(Environment.NewLine, new[]
            {
                source,
                "--fail-on",
                "error",
            }));

            var (failOnError, _, _) = Run("@" + rsp);
            var (overridden, _, _) = Run("@" + rsp, "--fail-on", "warning");

            Assert.Equal(0, failOnError);
            Assert.Equal(1, overridden);
        }

        // 56 — a signature would otherwise become an invisible character in the first argument.
        [Fact]
        public void AByteOrderMarkDoesNotLeakIntoTheFirstArgument()
        {
            var source = Write("Probe.cs", HotPathViolation);
            var rsp = Path.Combine(_dir, "bom.rsp");
            File.WriteAllText(rsp, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var (exitCode, stdout, _) = Run("@" + rsp, "--format", "json");

            Assert.Equal(1, exitCode);
            var diagnostics = JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics");
            Assert.Equal(1, diagnostics.GetArrayLength());
        }

        // 57
        [Fact]
        public void JsonListsEveryCompileErrorWithEnoughDetailToActOn()
        {
            var source = Write("Broken.cs", Unresolvable);

            var (_, stdout, _) = Run(source, "--format", "json");

            var root = JsonDocument.Parse(stdout).RootElement;
            var errors = root.GetProperty("compileErrors");
            Assert.True(errors.GetArrayLength() > 0);
            Assert.Equal(
                root.GetProperty("summary").GetProperty("compileErrorCount").GetInt32(),
                errors.GetArrayLength());

            var first = errors[0];
            Assert.StartsWith("CS", first.GetProperty("id").GetString(), StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("message").GetString()));
            Assert.Contains("Broken.cs", first.GetProperty("file").GetString()!, StringComparison.Ordinal);
            Assert.True(first.GetProperty("line").GetInt32() >= 1);
            Assert.True(first.GetProperty("column").GetInt32() >= 1);
        }

        // 58 — the text channel is for a person, so it stops at twenty and says how many are left.
        [Fact]
        public void TextListsTwentyCompileErrorsAndCountsTheRest()
        {
            var body = new StringBuilder();
            body.AppendLine("public class Many {");
            for (var i = 0; i < 25; i++)
            {
                body.AppendLine($"    NotAType field{i};");
            }

            body.AppendLine("}");
            var source = Write("Many.cs", body.ToString());

            var (_, _, stderr) = Run(source);

            var listed = stderr.Split('\n').Count(line => line.Contains("): error CS", StringComparison.Ordinal));
            Assert.Equal(20, listed);
            Assert.Contains("more compile errors.", stderr, StringComparison.Ordinal);
        }

        // 59 — present and empty, not absent: a consumer should not have to test for the key.
        [Fact]
        public void CompileErrorsIsAnEmptyArrayWhenThereAreNone()
        {
            var source = Write("Probe.cs", HotPathViolation);

            var (_, stdout, _) = Run(source, "--format", "json");

            var errors = JsonDocument.Parse(stdout).RootElement.GetProperty("compileErrors");
            Assert.Equal(0, errors.GetArrayLength());
        }

        // 60 — listing them does not turn them into findings.
        [Fact]
        public void CompileErrorsAreNeitherDiagnosticsNorWeighedByTheThreshold()
        {
            var source = Write("Broken.cs", Unresolvable);

            var (exitCode, stdout, _) = Run(source, "--format", "json", "--fail-on", "error");

            var root = JsonDocument.Parse(stdout).RootElement;
            foreach (var diagnostic in root.GetProperty("diagnostics").EnumerateArray())
            {
                Assert.StartsWith("UPA", diagnostic.GetProperty("id").GetString(), StringComparison.Ordinal);
            }

            // Compile errors are present, and the threshold still reports a clean run: they are
            // a fact about the analysis, not a finding to weigh.
            Assert.True(root.GetProperty("compileErrors").GetArrayLength() > 0);
            Assert.Equal(0, exitCode);
        }
    }
}
