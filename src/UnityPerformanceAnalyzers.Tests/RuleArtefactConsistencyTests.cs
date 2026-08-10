using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CodeFixes;
using UnityPerformanceAnalyzers.Catalog;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Asserts that every rule's artefacts agree with each other: the descriptor, both
    /// documentation pages, both README tables, the presets, and whether a code fix exists.
    /// </summary>
    /// <remarks>
    /// 46 rules times six artefacts is more consistency than anyone holds in their head, and
    /// the failure is silent: nothing breaks, the documentation just stops being true. It
    /// already happened — UPA0029's page shipped in 0.8.0 saying both that a fix is offered
    /// for array sources and, fifteen lines later, that there is no automatic fix at all. A
    /// reader found it, not the build.
    /// <para>
    /// Each test reports every offender rather than the first, because a drift that spans
    /// several rules is one edit to fix and several runs to discover otherwise.
    /// </para>
    /// </remarks>
    public class RuleArtefactConsistencyTests
    {
        /// <summary>
        /// The heading each page uses to describe its fix. A convention rather than prose
        /// matching: prose is where the drift lives, so the assertion needs something a page
        /// either has or does not.
        /// </summary>
        private const string EnglishFixHeading = "### The code fix";

        private const string ChineseFixHeading = "### 關於 code fix";

        private static readonly string Root = FindRepositoryRoot();

        private static IReadOnlyList<UpaRule> Rules => UpaRuleCatalog.Rules();

        [Fact]
        public void EveryRuleHasBothLanguagePages()
        {
            var missing = Rules
                .SelectMany(rule => new[] { rule.Id + ".md", rule.Id + ".zh-TW.md" })
                .Where(name => !File.Exists(Path.Combine(Root, "docs", "rules", name)))
                .ToArray();

            Assert.True(missing.Length == 0, "rule pages missing: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryRulePageBelongsToALiveRule()
        {
            var ids = Rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);

            var orphans = Directory.EnumerateFiles(Path.Combine(Root, "docs", "rules"), "UPA*.md")
                .Select(Path.GetFileName)
                .Select(name => name!.Split('.')[0])
                .Distinct(StringComparer.Ordinal)
                .Where(id => !ids.Contains(id))
                .ToArray();

            Assert.True(
                orphans.Length == 0,
                "documented rules that no descriptor exports: " + string.Join(", ", orphans));
        }

        [Fact]
        public void RuleIdsAreWellFormedAndUnique()
        {
            var malformed = Rules
                .Where(rule => !Regex.IsMatch(rule.Id, "^UPA[0-9]{4}$"))
                .Select(rule => rule.Id)
                .ToArray();

            Assert.True(malformed.Length == 0, "malformed ids: " + string.Join(", ", malformed));
            Assert.Equal(Rules.Count, Rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void HelpUriPointsAtAPageInTheRepository()
        {
            var broken = Rules
                .Where(rule => string.IsNullOrWhiteSpace(rule.HelpUri) ||
                    !File.Exists(Path.Combine(Root, "docs", "rules", rule.Id + ".md")))
                .Select(rule => rule.Id + " -> " + rule.HelpUri)
                .ToArray();

            Assert.True(broken.Length == 0, "help links with no page behind them: " + string.Join(", ", broken));
        }

        [Fact]
        public void BothReadmeTablesListEveryRuleOnce()
        {
            foreach (var readme in new[] { "README.md", "README.zh-TW.md" })
            {
                var text = File.ReadAllText(Path.Combine(Root, readme));
                var missing = Rules
                    .Where(rule => !text.Contains("[" + rule.Id + "]", StringComparison.Ordinal))
                    .Select(rule => rule.Id)
                    .ToArray();

                Assert.True(missing.Length == 0, readme + " does not list: " + string.Join(", ", missing));
            }
        }

        /// <summary>
        /// The assertion that would have caught UPA0029: whether a fix exists is decided by
        /// the assembly, and every place that talks about it has to say the same thing.
        /// </summary>
        [Fact]
        public void CodeFixExistenceAgreesEverywhere()
        {
            var fixedIds = FixableDiagnosticIds();
            var readme = File.ReadAllText(Path.Combine(Root, "README.md"));
            var problems = new List<string>();

            foreach (var rule in Rules)
            {
                var hasFix = fixedIds.Contains(rule.Id);
                var english = File.ReadAllText(Path.Combine(Root, "docs", "rules", rule.Id + ".md"));
                var chinese = File.ReadAllText(Path.Combine(Root, "docs", "rules", rule.Id + ".zh-TW.md"));

                var englishSays = english.Contains(EnglishFixHeading, StringComparison.Ordinal);
                var chineseSays = chinese.Contains(ChineseFixHeading, StringComparison.Ordinal);
                var readmeSays = Regex.IsMatch(readme, @"^\| " + rule.Id + @" \|", RegexOptions.Multiline);

                if (englishSays != hasFix)
                {
                    problems.Add($"{rule.Id}: fix registered = {hasFix}, English page says {englishSays}");
                }

                if (chineseSays != hasFix)
                {
                    problems.Add($"{rule.Id}: fix registered = {hasFix}, Chinese page says {chineseSays}");
                }

                if (readmeSays != hasFix)
                {
                    problems.Add($"{rule.Id}: fix registered = {hasFix}, README fix table says {readmeSays}");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// A deprecated rule is off by default, and both pages say so in their title. The
        /// descriptor is the source of truth for the first half only — "off by default" and
        /// "deprecated" are not the same claim, so the pages carry the word.
        /// </summary>
        [Fact]
        public void DeprecatedRulesSaySoOnBothPages()
        {
            var problems = new List<string>();

            foreach (var rule in Rules)
            {
                // The title line only. A page that mentions another rule's deprecation - the
                // cross-references between UPA0022, UPA0026 and UPA0030 all do - is not
                // declaring its own status, and reading the whole file cannot tell the
                // difference. This assertion learned that from its own first run.
                var englishTitle = FirstLine(Path.Combine(Root, "docs", "rules", rule.Id + ".md"));
                var chineseTitle = FirstLine(Path.Combine(Root, "docs", "rules", rule.Id + ".zh-TW.md"));

                var englishSays = englishTitle.Contains("(deprecated)", StringComparison.OrdinalIgnoreCase);
                var chineseSays = chineseTitle.Contains("已廢止", StringComparison.Ordinal);

                if (englishSays != chineseSays)
                {
                    problems.Add($"{rule.Id}: pages disagree about deprecation (en {englishSays}, zh {chineseSays})");
                }

                if (englishSays && rule.EnabledByDefault)
                {
                    problems.Add($"{rule.Id}: documented as deprecated but enabled by default");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// A new rule that no preset grades reports at whatever the descriptor happens to say
        /// and cannot be turned off by choosing a preset. The absences are deliberate and
        /// listed where they are decided, so this only catches the ones nobody decided.
        /// </summary>
        [Fact]
        public void EveryRuleIsGradedByAPresetOrDeliberatelyAbsent()
        {
            var graded = RuleManifest.PresetTable.UpaRows
                .Select(row => row.Id)
                .ToHashSet(StringComparer.Ordinal);

            // UPA1001 keeps its default in every preset; the WebGL group lives only in the
            // addon, because an explicit entry in a base preset would override the Include.
            var deliberatelyAbsent = new HashSet<string>(StringComparer.Ordinal)
            {
                "UPA1001", "UPA3000", "UPA3001", "UPA3002", "UPA3003", "UPA3004",
            };

            var ungraded = Rules
                .Select(rule => rule.Id)
                .Where(id => !graded.Contains(id) && !deliberatelyAbsent.Contains(id))
                .ToArray();

            Assert.True(ungraded.Length == 0, "rules no preset grades: " + string.Join(", ", ungraded));
        }

        private static string FirstLine(string path) => File.ReadLines(path).FirstOrDefault() ?? string.Empty;

        private static HashSet<string> FixableDiagnosticIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var type in typeof(CodeFixes.UPA0019BoxedYieldCodeFixProvider).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type))
                {
                    continue;
                }

                var provider = (CodeFixProvider)Activator.CreateInstance(type)!;
                foreach (var id in provider.FixableDiagnosticIds)
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <summary>
        /// Walks up from the test assembly to the directory holding the solution. The tests
        /// read the committed artefacts rather than copies, because a copy would only prove
        /// the copy is consistent.
        /// </summary>
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is object)
            {
                if (directory.EnumerateFiles("*.sln").Any())
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "no directory containing a .sln above " + AppContext.BaseDirectory);
        }
    }
}
