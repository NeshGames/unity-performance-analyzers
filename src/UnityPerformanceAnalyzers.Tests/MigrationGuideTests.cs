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
    /// Holds the UnityEngineAnalyzer migration guide to this repository's rule set.
    /// </summary>
    /// <remarks>
    /// A migration table is a promise about two rule sets, and the half describing ours goes
    /// stale exactly when a rule is renumbered or retired. The other half is checked by
    /// having been read once, on a date the page states.
    /// <para>
    /// This page exists because three claims about that project in this repository turned out
    /// to be wrong when someone finally fetched its rule list: a rule id attributed to the
    /// wrong rule, a rule count that was never right, and an archival that had not happened.
    /// </para>
    /// </remarks>
    public class MigrationGuideTests
    {
        private static readonly string Root = FindRepositoryRoot();

        private static readonly string[] Pages =
        {
            "migration-unityengineanalyzer.md",
            "migration-unityengineanalyzer.zh-TW.md",
        };

        private static string Read(string page) => File.ReadAllText(Path.Combine(Root, "docs", page));

        /// <summary>
        /// Every rule the guide points at is a rule this package still has. A retired or
        /// renumbered rule leaves a migration instruction pointing at nothing, and the reader
        /// is by definition someone who does not know the rule set well enough to notice.
        /// </summary>
        [Fact]
        public void EveryRuleTheGuideNamesStillExists()
        {
            var known = UpaRuleCatalog.Rules().Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var page in Pages)
            {
                var referenced = Regex.Matches(Read(page), @"UPA\d{4}")
                    .Select(match => match.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                Assert.NotEmpty(referenced);
                var missing = referenced.Where(id => !known.Contains(id)).ToArray();
                Assert.True(missing.Length == 0, page + " names rules that do not exist: " + string.Join(", ", missing));
            }
        }

        /// <summary>
        /// Every page linked from the guide is a page that is there. Rule pages are linked by
        /// language, so the Chinese guide must link Chinese pages or it hands a reader who
        /// chose Chinese a set of English ones.
        /// </summary>
        [Fact]
        public void EveryRulePageTheGuideLinksExists()
        {
            var missing = new List<string>();

            foreach (var page in Pages)
            {
                foreach (Match match in Regex.Matches(Read(page), @"\(rules/(UPA\d{4}(?:\.zh-TW)?\.md)\)"))
                {
                    var target = match.Groups[1].Value;
                    if (!File.Exists(Path.Combine(Root, "docs", "rules", target)))
                    {
                        missing.Add($"{page}: {target}");
                    }

                    var chinesePage = page.Contains("zh-TW", StringComparison.Ordinal);
                    var chineseTarget = target.Contains("zh-TW", StringComparison.Ordinal);
                    if (chinesePage != chineseTarget)
                    {
                        missing.Add($"{page}: links {target}, which is the wrong language");
                    }
                }
            }

            Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
        }

        /// <summary>
        /// All sixteen of their rules are accounted for. A migration guide that silently omits
        /// one leaves the reader believing a rule they relied on has an equivalent here.
        /// </summary>
        [Fact]
        public void EveryRuleOfTheirsIsAccountedFor()
        {
            foreach (var page in Pages)
            {
                var text = Read(page);
                var missing = Enumerable.Range(1, 16)
                    .Select(number => $"UEA{number:D4}")
                    .Where(id => !text.Contains(id, StringComparison.Ordinal))
                    .ToArray();

                Assert.True(missing.Length == 0, page + " does not mention: " + string.Join(", ", missing));
            }
        }

        [Fact]
        public void BothLanguagesExistAndLinkToEachOther()
        {
            Assert.Contains("(migration-unityengineanalyzer.zh-TW.md)", Read(Pages[0]));
            Assert.Contains("(migration-unityengineanalyzer.md)", Read(Pages[1]));
        }

        /// <summary>
        /// The claims about someone else's project carry the date they were checked. Without
        /// it a reader cannot tell a fact from a recollection, which is how this repository
        /// came to state three wrong ones.
        /// </summary>
        [Fact]
        public void TheClaimsAboutTheirProjectAreDated()
        {
            foreach (var page in Pages)
            {
                var text = Read(page);
                Assert.Contains("2026-08-10", text);
                Assert.Contains("2019", text);
            }
        }

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

            throw new InvalidOperationException("no directory containing a .sln above " + AppContext.BaseDirectory);
        }
    }
}
