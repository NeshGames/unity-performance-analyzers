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
    /// Asserts that the live rule descriptors, rule documentation and generated preset policy
    /// agree. The root README is deliberately excluded: it is an agent-oriented repository
    /// index, not a rule inventory.
    /// </summary>
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
        public void DeprecatedRulesSaySoOnBothPages()
        {
            var problems = new List<string>();

            foreach (var rule in Rules)
            {
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

        [Fact]
        public void EveryRuleIsGradedByAPresetOrDeliberatelyAbsent()
        {
            var graded = RuleManifest.PresetTable.UpaRows
                .Select(row => row.Id)
                .ToHashSet(StringComparer.Ordinal);

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
