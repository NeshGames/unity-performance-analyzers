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
    /// Every rule's help link is derived from its id and points at
    /// <c>docs/rules/&lt;id&gt;.md</c>. Nothing checked that the file was there, so the first
    /// sign of a missing one would have been a user following the link to a 404 — and nothing
    /// checked the severity and category printed at the top of it either, which is the part
    /// most likely to drift, since changing a descriptor is a one-line edit somewhere else.
    /// </summary>
    public class RuleDocumentationTests
    {
        private static readonly Regex s_row = new Regex(@"^\|\s*(?<key>[^|]+?)\s*\|\s*(?<value>[^|]+?)\s*\|\s*$", RegexOptions.Multiline);

        private static string RulesDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is object && !Directory.Exists(Path.Combine(dir.FullName, "docs", "rules")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "docs", "rules");
        }

        private static IReadOnlyDictionary<string, string> Header(string path)
        {
            var text = File.ReadAllText(path);
            var rows = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in s_row.Matches(text))
            {
                var key = match.Groups["key"].Value;
                if (key.Length > 0 && key[0] != '-' && !rows.ContainsKey(key))
                {
                    rows[key] = match.Groups["value"].Value;
                }
            }

            return rows;
        }

        [Fact]
        public void EveryRule_HasBothLanguagePages()
        {
            var directory = RulesDirectory();
            var missing = UpaRuleCatalog.Rules()
                .SelectMany(rule => new[] { $"{rule.Id}.md", $"{rule.Id}.zh-TW.md" })
                .Where(name => !File.Exists(Path.Combine(directory, name)))
                .ToArray();

            Assert.Empty(missing);
        }

        [Fact]
        public void EveryPage_StatesWhatTheDescriptorSays()
        {
            var directory = RulesDirectory();
            var wrong = new List<string>();

            foreach (var rule in UpaRuleCatalog.Rules())
            {
                var header = Header(Path.Combine(directory, $"{rule.Id}.md"));

                void Check(string key, string expected)
                {
                    if (!header.TryGetValue(key, out var actual))
                    {
                        wrong.Add($"{rule.Id}.md has no '{key}' row");
                    }
                    else if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        wrong.Add($"{rule.Id}.md says {key} = '{actual}'; the descriptor says '{expected}'");
                    }
                }

                Check("Category", rule.Category);
                Check("Default severity", rule.DefaultSeverity);
                Check("Enabled by default", rule.EnabledByDefault ? "Yes" : "No");
            }

            Assert.Empty(wrong);
        }

        // The two pages of a rule link to each other by hand. A page that links to the wrong
        // rule reads as correct from either end until someone follows it.
        [Fact]
        public void EveryPagePair_LinksToItsOwnTranslation()
        {
            var directory = RulesDirectory();
            var wrong = new List<string>();

            foreach (var rule in UpaRuleCatalog.Rules())
            {
                var english = File.ReadAllText(Path.Combine(directory, $"{rule.Id}.md"));
                var chinese = File.ReadAllText(Path.Combine(directory, $"{rule.Id}.zh-TW.md"));

                if (!english.Contains($"({rule.Id}.zh-TW.md)", StringComparison.Ordinal))
                {
                    wrong.Add($"{rule.Id}.md does not link to its zh-TW page");
                }

                if (!chinese.Contains($"({rule.Id}.md)", StringComparison.Ordinal))
                {
                    wrong.Add($"{rule.Id}.zh-TW.md does not link to its English page");
                }
            }

            Assert.Empty(wrong);
        }
    }
}
