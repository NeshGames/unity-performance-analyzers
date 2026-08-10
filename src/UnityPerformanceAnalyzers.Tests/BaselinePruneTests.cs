using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityPerformanceAnalyzers.Cli;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Covers reporting and removing baseline quota a run did not use.
    /// </summary>
    /// <remarks>
    /// A baseline that only ever suppresses turns into a fossil: entries for violations that
    /// were genuinely fixed stay forever, and the team gets no signal that the debt is going
    /// down. Pruning is how the contract shrinks — which makes it the one baseline operation
    /// whose failure mode is deletion, so most of what is asserted here is what it refuses.
    /// </remarks>
    public sealed class BaselinePruneTests : IDisposable
    {
        private const string TwoViolations = @"
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

        private const string OneViolation = @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        GetComponent<Rigidbody>();
    }

    void LateUpdate()
    {
    }
}";

        /// <summary>Fixed one violation, introduced a different one.</summary>
        private const string OneFixedOneNew = @"
using UnityEngine;

public sealed class Probe : MonoBehaviour
{
    void Update()
    {
        GetComponent<Rigidbody>();
    }

    void LateUpdate()
    {
        var found = GameObject.Find(""Player"");
    }
}";

        private readonly string _dir;

        public BaselinePruneTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "upa-prune-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private string Write(string name, string source)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, source);
            return path;
        }

        private string BaselinePath => Path.Combine(_dir, "upa-baseline.json");

        private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliEntryPoint.Run(args, stdout, stderr);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        /// <summary>Freezes the current state of the given files, then rewrites one of them.</summary>
        private string FreezeThen(string source, string replacement = null!)
        {
            var file = Write("Probe.cs", source);
            var (exitCode, _, stderr) = Run(file, "--whole-assembly", "--write-baseline", BaselinePath);
            Assert.Equal(0, exitCode);
            Assert.Contains("baseline entries", stderr);

            if (replacement is object)
            {
                File.WriteAllText(file, replacement);
            }

            return file;
        }

        private int EntryCount() =>
            JsonDocument.Parse(File.ReadAllText(BaselinePath))
                .RootElement.GetProperty("entries").GetArrayLength();

        // Case 68
        [Fact]
        public void ReportStale_ListsFileRuleAndMember()
        {
            var file = FreezeThen(TwoViolations, OneViolation);

            var (_, stdout, _) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--report-stale-baseline");

            Assert.Contains("no longer match", stdout);
            Assert.Contains("Probe.cs(UPA0001, LateUpdate): 1 -> 0", stdout);

            // The entry that still matches is not listed: an unused-quota report that names
            // live entries is a report someone deletes their way through.
            Assert.DoesNotContain("Update): 1 -> 1", stdout);
        }

        // Case 69
        [Fact]
        public void JsonCarriesTheStaleEntries_AndAgreesWithTheCount()
        {
            var file = FreezeThen(TwoViolations, OneViolation);

            var (_, stdout, _) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--format", "json");
            var root = JsonDocument.Parse(stdout).RootElement;

            var stale = root.GetProperty("baselineStale");
            Assert.Equal(1, stale.GetArrayLength());
            Assert.Equal("UPA0001", stale[0].GetProperty("id").GetString());
            Assert.Equal("LateUpdate", stale[0].GetProperty("member").GetString());
            Assert.Equal(1, stale[0].GetProperty("recorded").GetInt32());
            Assert.Equal(0, stale[0].GetProperty("observed").GetInt32());

            var total = stale.EnumerateArray()
                .Sum(e => e.GetProperty("recorded").GetInt32() - e.GetProperty("observed").GetInt32());
            Assert.Equal(
                root.GetProperty("summary").GetProperty("baselineStaleCount").GetInt64(),
                total);
        }

        // Case 70 - the difference from regenerating, which is the whole reason this exists.
        [Fact]
        public void Prune_RemovesUnusedQuotaAndAbsorbsNothingNew()
        {
            var file = FreezeThen(TwoViolations, OneFixedOneNew);
            Assert.Equal(2, EntryCount());

            var (exitCode, _, stderr) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--prune-baseline");

            Assert.Equal(0, exitCode);
            Assert.Contains("Pruned", stderr);

            var text = File.ReadAllText(BaselinePath);
            Assert.Contains("\"member\": \"Update\"", text);
            Assert.DoesNotContain("LateUpdate", text);

            // The new violation is not in the contract, so it still reports afterwards. A
            // regeneration would have frozen it, which is the outcome someone reaching for a
            // command that shrinks the file would least expect.
            Assert.DoesNotContain("UPA0014", text);
            var (after, stdout, _) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--format", "json");
            Assert.Equal(1, after);
            Assert.Contains("UPA0014", stdout);
        }

        // Case 70b - the failure that would have shipped: the prune ran against the filtered
        // result, where every suppressed occurrence has already been removed, so every entry
        // looked unused and the whole contract went.
        [Fact]
        public void Prune_KeepsEntriesThatStillMatch()
        {
            var file = FreezeThen(TwoViolations);

            var (exitCode, _, _) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--prune-baseline");

            Assert.Equal(0, exitCode);
            Assert.Equal(2, EntryCount());
        }

        // Case 71
        [Fact]
        public void Prune_RequiresWholeAssembly()
        {
            var file = FreezeThen(TwoViolations, OneViolation);
            var before = File.ReadAllText(BaselinePath);

            var (exitCode, _, stderr) = Run(file, "--baseline", BaselinePath, "--prune-baseline");

            Assert.Equal(2, exitCode);
            Assert.Contains("--prune-baseline requires --whole-assembly", stderr);
            Assert.Equal(before, File.ReadAllText(BaselinePath));
        }

        // Case 72
        [Fact]
        public void Prune_RefusesWhenTheCodeDidNotCompile()
        {
            var file = FreezeThen(TwoViolations);
            var before = File.ReadAllText(BaselinePath);
            File.WriteAllText(file, "using Nope; public class Probe : Missing { }");

            var (exitCode, _, stderr) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--prune-baseline");

            Assert.Equal(2, exitCode);
            Assert.Equal(before, File.ReadAllText(BaselinePath));
            Assert.Contains("compile error", stderr);
        }

        // Case 73 - the one that matters most: pruning from one changed file would find no
        // occurrences for every other file and read that as debt paid off.
        [Fact]
        public void Prune_RefusesWhenTheRunDoesNotCoverTheBaseline()
        {
            var first = Write("Probe.cs", TwoViolations);
            var second = Write("Other.cs", TwoViolations.Replace("Probe", "Other"));
            var (frozen, _, _) = Run(
                first, second, "--whole-assembly", "--write-baseline", BaselinePath);
            Assert.Equal(0, frozen);

            var before = File.ReadAllText(BaselinePath);
            var (exitCode, _, stderr) = Run(
                first, "--whole-assembly", "--baseline", BaselinePath, "--prune-baseline");

            Assert.Equal(2, exitCode);
            Assert.Contains("Other.cs", stderr);
            Assert.Equal(before, File.ReadAllText(BaselinePath));
        }

        // Case 74
        [Fact]
        public void Prune_DropsEntriesWhoseFileIsGone()
        {
            var first = Write("Probe.cs", TwoViolations);
            var second = Write("Other.cs", TwoViolations.Replace("Probe", "Other"));
            Run(first, second, "--whole-assembly", "--write-baseline", BaselinePath);
            Assert.Equal(4, EntryCount());

            File.Delete(second);
            var (exitCode, _, _) = Run(
                first, "--whole-assembly", "--baseline", BaselinePath, "--prune-baseline");

            Assert.Equal(0, exitCode);
            Assert.Equal(2, EntryCount());
            Assert.DoesNotContain("Other.cs", File.ReadAllText(BaselinePath));
        }

        // Case 75
        [Fact]
        public void PruneAndWrite_TogetherIsAUsageError()
        {
            var file = FreezeThen(TwoViolations);

            var (exitCode, _, stderr) = Run(
                file,
                "--whole-assembly",
                "--baseline", BaselinePath,
                "--prune-baseline",
                "--write-baseline", Path.Combine(_dir, "other.json"));

            Assert.Equal(2, exitCode);

            // The prune-specific message, not just "cannot be given together": --baseline and
            // --write-baseline already reject each other, so the looser assertion passed with
            // this validation removed entirely.
            Assert.Contains("--prune-baseline and --write-baseline", stderr);
        }

        // Case 76
        [Fact]
        public void FailOnStale_FailsWhenQuotaWentUnused()
        {
            var file = FreezeThen(TwoViolations, OneViolation);

            var (exitCode, _, stderr) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--fail-on-stale");

            Assert.Equal(1, exitCode);
            Assert.Contains("--prune-baseline", stderr);
        }

        // Case 77
        [Fact]
        public void FailOnStale_PassesWhenTheContractIsExact()
        {
            var file = FreezeThen(TwoViolations);

            var (exitCode, _, _) = Run(
                file, "--whole-assembly", "--baseline", BaselinePath, "--fail-on-stale");

            Assert.Equal(0, exitCode);
        }

        // Case 78 - a gate asked whether the baseline is stale, in a run that cannot tell,
        // must not answer no.
        [Fact]
        public void FailOnStale_RefusesWhenItCannotTell()
        {
            var file = FreezeThen(TwoViolations);
            File.WriteAllText(file, "using Nope; public class Probe : Missing { }");

            var (exitCode, _, stderr) = Run(file, "--baseline", BaselinePath, "--fail-on-stale");

            Assert.Equal(2, exitCode);
            Assert.Contains("cannot be answered", stderr);
        }

        [Fact]
        public void TheStaleOptionsNeedABaseline()
        {
            var file = Write("Probe.cs", TwoViolations);

            foreach (var flag in new[] { "--prune-baseline", "--report-stale-baseline", "--fail-on-stale" })
            {
                var (exitCode, _, stderr) = Run(file, "--whole-assembly", flag);

                Assert.Equal(2, exitCode);
                Assert.Contains("needs --baseline", stderr);
            }
        }
    }
}
