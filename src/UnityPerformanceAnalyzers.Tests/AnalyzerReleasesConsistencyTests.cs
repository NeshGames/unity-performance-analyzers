using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Bidirectional release-tracking check, covering what Roslyn's own release-tracking
    /// analyzers do not.
    ///
    /// This class used to say those analyzers could not see descriptors built through the
    /// UpaDescriptor factory. That was wrong, and measuring it is what showed it: deleting a
    /// rule's row from the shipped file fails the build with RS2000, and promoting a severity
    /// change the source does not have fails it with RS2001. Both point at the descriptor
    /// site. So the built-in checks do work here, and the release files are validated by the
    /// build itself.
    ///
    /// What is left over is still worth holding, because none of it is RS2000's job: a
    /// release row for a rule the assembly no longer has, a history whose sections are out of
    /// order or empty, and an unshipped file that re-introduces something already shipped.
    /// Those are properties of the file as a record, not of any one rule.
    ///
    /// It reads the files the way the format means them rather than as a flat list of ids.
    /// A removed rule keeps its row forever, under <c>### Removed Rules</c>, so treating
    /// every row as a live rule reports the history itself as a defect — which is exactly
    /// what happened the first time a removal was recorded.
    /// </summary>
    public class AnalyzerReleasesConsistencyTests
    {
        private static readonly Regex s_ruleRow = new Regex(@"^(UPA\d{4}) \|", RegexOptions.Compiled);
        private static readonly Regex s_releaseHeading = new Regex(@"^## Release (\S+)\s*$", RegexOptions.Compiled);
        private static readonly Regex s_kindHeading = new Regex(@"^### (New|Removed|Changed) Rules\s*$", RegexOptions.Compiled);

        private static string AnalyzerProjectDir =>
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "UnityPerformanceAnalyzers"));

        private sealed record Entry(string Release, string Kind, string Id);

        /// <summary>
        /// Both directions of "the files describe exactly the rules the assembly has" are
        /// enforced by the build — RS2000 for a rule with no row, RS2003 for a row with no
        /// rule. What is not enforced anywhere is that those checks are still switched on:
        /// drop the package reference or unhook either AdditionalFile and the release files
        /// simply stop being checked, with a green build either way.
        ///
        /// That is the same failure shape this project pins the Roslyn version for. A check
        /// that silently stops running looks exactly like a check that keeps passing.
        /// </summary>
        [Fact]
        public void TheReleaseTrackingChecksAreStillSwitchedOn()
        {
            var project = File.ReadAllText(Path.Combine(AnalyzerProjectDir, "UnityPerformanceAnalyzers.csproj"));

            Assert.Contains("Microsoft.CodeAnalysis.Analyzers", project, StringComparison.Ordinal);
            Assert.Contains("<AdditionalFiles Include=\"AnalyzerReleases.Shipped.md\" />", project, StringComparison.Ordinal);
            Assert.Contains("<AdditionalFiles Include=\"AnalyzerReleases.Unshipped.md\" />", project, StringComparison.Ordinal);

            // The files the entries name have to be there too: an AdditionalFiles entry for a
            // path that does not exist is not an error, it is just nothing to read.
            Assert.True(File.Exists(Path.Combine(AnalyzerProjectDir, "AnalyzerReleases.Shipped.md")));
            Assert.True(File.Exists(Path.Combine(AnalyzerProjectDir, "AnalyzerReleases.Unshipped.md")));
        }

        /// <summary>
        /// The shipped file is history, so it is append-only and ordered. An out-of-order
        /// section is the shape a promotion run against the wrong version would leave, and it
        /// is the one mistake this file cannot be fixed out of afterwards: the tags are
        /// already published.
        /// </summary>
        [Fact]
        public void ShippedReleasesAreInAscendingVersionOrder()
        {
            var versions = Read("AnalyzerReleases.Shipped.md")
                .Select(entry => entry.Release)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(versions);

            var parsed = versions.Select(v => Version.Parse(v)).ToArray();
            Assert.Equal(parsed.OrderBy(v => v).ToArray(), parsed);
        }

        /// <summary>
        /// A release section with no rows says a version changed the rule set when it did not.
        /// The promotion step is what would produce one, by running on a release that had
        /// nothing to promote.
        /// </summary>
        [Fact]
        public void NoShippedReleaseSectionIsEmpty()
        {
            var lines = File.ReadAllLines(Path.Combine(AnalyzerProjectDir, "AnalyzerReleases.Shipped.md"));
            string? release = null;
            var rows = 0;

            foreach (var line in lines)
            {
                var heading = s_releaseHeading.Match(line);
                if (heading.Success)
                {
                    Assert.True(release is null || rows > 0, "release section " + release + " lists no rules");
                    release = heading.Groups[1].Value;
                    rows = 0;
                }
                else if (s_ruleRow.IsMatch(line))
                {
                    rows++;
                }
            }

            Assert.True(release is null || rows > 0, "release section " + release + " lists no rules");
        }

        // There was a fourth test here, asserting the unshipped file does not re-introduce a
        // rule an earlier release already shipped. It was deleted rather than kept: RS2006
        // fails the *build* on exactly that, so the test could never run, let alone fail. A
        // test that cannot go red is not coverage, it only reads like it.

        /// <summary>Rule rows paired with the release and the subsection they sit under.</summary>
        private static IEnumerable<Entry> Read(string fileName)
        {
            var release = "unshipped";
            var kind = "New";

            foreach (var line in File.ReadAllLines(Path.Combine(AnalyzerProjectDir, fileName)))
            {
                var heading = s_releaseHeading.Match(line);
                if (heading.Success)
                {
                    release = heading.Groups[1].Value;
                    continue;
                }

                var kindHeading = s_kindHeading.Match(line);
                if (kindHeading.Success)
                {
                    kind = kindHeading.Groups[1].Value;
                    continue;
                }

                var row = s_ruleRow.Match(line);
                if (row.Success)
                {
                    yield return new Entry(release, kind, row.Groups[1].Value);
                }
            }
        }
    }
}
