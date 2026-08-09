using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityPerformanceAnalyzers.Catalog;
using UnityPerformanceAnalyzers.Cli;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// The same source through both paths a rule can reach a user by: the unit harness, which
    /// is Roslyn's own testing machinery, and the command line, which assembles its
    /// compilation, its options and its severities by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both assert against markers in the fixture, never against each other. Comparing the two
    /// outputs would pass whenever they are wrong in the same way, and a shared mistake is the
    /// likely kind — they load the same analyzer assembly. It would also need a filter,
    /// because the harness runs one analyzer and the command line runs all of them, and that
    /// filter would be a fresh place for a green result to mean nothing.
    /// </para>
    /// <para>
    /// Two blind spots, stated rather than found later. The ruleset channel has no counterpart
    /// here: Unity reads .ruleset and the harness does not, so its differential is the
    /// pinned-compiler smoke test instead. And the two reference sets are not identical, so
    /// rules matching BCL symbols can disagree legitimately — the fixtures stay on rules that
    /// need only UnityEngine core types, rather than allowing exceptions after the fact.
    /// </para>
    /// </remarks>
    public class DifferentialTests
    {
        private static readonly Regex s_expect = new Regex(@"^//\s*expect (UPA\d{4})\s*$", RegexOptions.Multiline);
        private static readonly Regex s_expectNone = new Regex(@"^//\s*expect-none (UPA\d{4})\s*$", RegexOptions.Multiline);
        private static readonly Regex s_reportedId = new Regex(@"""id""\s*:\s*""(UPA\d{4})""");
        private static readonly Regex s_compileErrorCount = new Regex(@"""compileErrorCount""\s*:\s*(\d+)");

        public static IEnumerable<object[]> Fixtures() =>
            Directory.EnumerateFiles(FixtureDirectory(), "*.cs.txt")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new object[] { Path.GetFileName(path) });

        private static string FixtureDirectory()
        {
            // Walk out of bin/<config>/<tfm>, so the fixtures stay readable source next to the
            // tests rather than embedded resources.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is object && !Directory.Exists(Path.Combine(dir.FullName, "Fixtures")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "Fixtures");
        }

        private static (string Source, string[] Expected, string[] Forbidden) Read(string fixture)
        {
            var text = File.ReadAllText(Path.Combine(FixtureDirectory(), fixture));
            string[] Ids(Regex pattern) => pattern.Matches(text)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            return (text, Ids(s_expect), Ids(s_expectNone));
        }

        // A scanner that matches nothing reports success exactly like one that matched
        // everything: without this, a fixture that lost its markers would pass both pipelines
        // while asserting nothing at all.
        [Theory]
        [MemberData(nameof(Fixtures))]
        public void EveryFixture_CarriesMarkers(string fixture)
        {
            var (_, expected, forbidden) = Read(fixture);
            Assert.NotEmpty(expected.Concat(forbidden).ToArray());
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public async Task UnitHarness_MatchesTheMarkers(string fixture)
        {
            var (source, expected, forbidden) = Read(fixture);

            foreach (var id in expected)
            {
                Assert.True(
                    await ReportsAnything(id, source),
                    $"{fixture} marks {id} expected; the unit harness reported nothing.");
            }

            foreach (var id in forbidden)
            {
                Assert.False(
                    await ReportsAnything(id, source),
                    $"{fixture} marks {id} expect-none; the unit harness reported it.");
            }
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void CommandLine_MatchesTheMarkers(string fixture)
        {
            var (_, expected, forbidden) = Read(fixture);
            var reported = RunCommandLine(fixture);

            Assert.Empty(expected.Where(id => !reported.Contains(id)).ToArray());
            Assert.Empty(forbidden.Where(id => reported.Contains(id)).ToArray());
        }

        /// <summary>
        /// The fixture carries no inline markup, so the harness expects silence. One analyzer
        /// is loaded, which makes "it complained" and "this rule fired" the same statement.
        /// </summary>
        /// <remarks>
        /// A compiler error would also make it complain, and would be read here as a report.
        /// That is why the command-line half refuses a fixture that does not compile: the two
        /// share the fixture, and the loud failure there is what stops this one from lying.
        /// It has already happened once — a stub member that did not exist made this method
        /// claim a rule had fired while the command line reported nothing at all.
        /// </remarks>
        private static async Task<bool> ReportsAnything(string id, string source)
        {
            var analyzerType = UpaRuleCatalog.Analyzers()
                .Single(entry => entry.Instance.SupportedDiagnostics.Any(descriptor => descriptor.Id == id))
                .Type;

            var verify = typeof(RuleVerifier)
                .GetMethod(nameof(RuleVerifier.VerifyAsync))!
                .MakeGenericMethod(analyzerType);

            var harness = new RuleHarness { EnabledRules = { id } };
            try
            {
                await (Task)verify.Invoke(null, new object?[] { source, harness })!;
                return false;
            }
            catch (Exception failure) when (failure is not Xunit.Sdk.XunitException)
            {
                return true;
            }
        }

        private static HashSet<string> RunCommandLine(string fixture)
        {
            var directory = Path.Combine(Path.GetTempPath(), "upa-diff-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var file = Path.Combine(directory, Path.GetFileNameWithoutExtension(fixture));
                File.Copy(Path.Combine(FixtureDirectory(), fixture), file);

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                CliEntryPoint.Run(
                    new[]
                    {
                        file,
                        "--reference", typeof(UnityEngine.MonoBehaviour).Assembly.Location,
                        "--all-warn",
                        "--format", "json",
                    },
                    stdout,
                    stderr);

                var json = stdout.ToString();

                // A rule matches resolved symbols, so a missing reference makes it fall
                // silent rather than fail. Zero diagnostics on a compilation that did not
                // compile proves nothing, and reads exactly like a clean run.
                var compileErrors = s_compileErrorCount.Match(json);
                Assert.True(
                    compileErrors.Success && compileErrors.Groups[1].Value == "0",
                    $"{fixture} did not compile for the command line, so its silence proves nothing. Output: {json}");

                return new HashSet<string>(
                    s_reportedId.Matches(json).Select(match => match.Groups[1].Value),
                    StringComparer.Ordinal);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
