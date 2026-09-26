using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityPerformanceAnalyzers.Catalog;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Asserts that every rule's artefacts agree with each other: the descriptor, both
    /// documentation pages, both README tables, and the presets.
    /// </summary>
    /// <remarks>
    /// 46 rules times five artefacts is more consistency than anyone holds in their head, and
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
        private static readonly string Root = TestRepository.Root;

        private static IReadOnlyList<UpaRule> Rules => UpaRuleCatalog.Rules();

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

        /// <summary>
        /// The link every diagnostic carries has to name its own rule's page. That the page
        /// exists is RuleDocumentationTests' assertion; this one is about the link.
        /// </summary>
        [Fact]
        public void HelpUriPointsAtTheRulesOwnPage()
        {
            var broken = Rules
                .Where(rule => !rule.HelpUri.EndsWith("/docs/rules/" + rule.Id + ".md", StringComparison.Ordinal))
                .Select(rule => rule.Id + " -> " + rule.HelpUri)
                .ToArray();

            Assert.True(broken.Length == 0, "help links that miss their rule's page: " + string.Join(", ", broken));
        }

        [Fact]
        public void BothReadmeTablesListEveryRule()
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
    }
}
