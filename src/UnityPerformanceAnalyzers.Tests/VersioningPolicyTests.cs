using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using UnityPerformanceAnalyzers.Catalog;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Asserts the promises the published versioning policy makes about severities.
    /// </summary>
    /// <remarks>
    /// A policy page is a claim about the code, and a claim nothing checks drifts the same
    /// way documentation does — except this one is what a reader relies on when deciding
    /// whether an upgrade can fail their build. Each test below corresponds to a sentence
    /// in <c>docs/versioning.md</c>.
    /// </remarks>
    public class VersioningPolicyTests
    {
        private static IReadOnlyList<UpaRule> Rules => UpaRuleCatalog.Rules();

        /// <summary>
        /// "No rule's own default is above Warning. Nothing in this package decides on its
        /// own that your build should fail."
        /// </summary>
        [Fact]
        public void NoRuleDefaultsAboveWarning()
        {
            var tooLoud = Rules
                .Where(rule => Severity(rule) > DiagnosticSeverity.Warning)
                .Select(rule => $"{rule.Id} defaults to {rule.DefaultSeverity}")
                .ToArray();

            Assert.True(tooLoud.Length == 0, string.Join(Environment.NewLine, tooLoud));
        }

        /// <summary>
        /// "Ecosystem rules (UPA2000+) and platform rules (UPA3000+) ship off by default."
        /// They activate from what the assembly references or defines, so one shipping
        /// enabled would report on projects that never asked for it.
        /// </summary>
        [Fact]
        public void ConditionalGroupsShipDisabled()
        {
            var enabled = Rules
                .Where(rule => Number(rule) >= 2000 && rule.EnabledByDefault)
                .Select(rule => rule.Id)
                .ToArray();

            Assert.True(enabled.Length == 0, "enabled by default: " + string.Join(", ", enabled));
        }

        /// <summary>
        /// The counts the page prints. Stated rather than described because "43 of 46" is
        /// what makes the severity claim checkable by a reader, and a stale number reads as
        /// authoritative.
        /// </summary>
        [Fact]
        public void PublishedCountsMatchTheCatalog()
        {
            var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "versioning.md"));
            var warnings = Rules.Count(rule => Severity(rule) == DiagnosticSeverity.Warning);
            var infos = Rules.Count(rule => Severity(rule) == DiagnosticSeverity.Info);

            Assert.Contains($"Of {Rules.Count} rules, {warnings} default to Warning and {infos} to", page);
        }

        /// <summary>
        /// "Two rules are retired today." Both language pages name them, and a third
        /// retirement that forgot to update the page would leave the count wrong.
        /// </summary>
        [Fact]
        public void RetiredRulesNamedByThePolicyAreTheDeprecatedOnes()
        {
            var root = RepositoryRoot();
            var deprecated = Rules
                .Where(rule => FirstLine(Path.Combine(root, "docs", "rules", rule.Id + ".md"))
                    .Contains("(deprecated)", StringComparison.OrdinalIgnoreCase))
                .Select(rule => rule.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            foreach (var page in new[] { "versioning.md", "versioning.zh-TW.md" })
            {
                var text = File.ReadAllText(Path.Combine(root, "docs", page));
                foreach (var id in deprecated)
                {
                    Assert.True(text.Contains(id, StringComparison.Ordinal), $"{page} does not name {id}");
                }

                var named = Rules
                    .Select(rule => rule.Id)
                    .Where(id => text.Contains("rules/" + id + ".", StringComparison.Ordinal))
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();

                Assert.Equal(deprecated, named);
            }
        }

        /// <summary>
        /// Both languages exist and point at each other. The rule pages are held to this
        /// already; the policy is the page most likely to be read by someone deciding
        /// whether to adopt at all.
        /// </summary>
        [Fact]
        public void BothLanguagesExistAndLinkToEachOther()
        {
            var root = Path.Combine(RepositoryRoot(), "docs");
            var english = File.ReadAllText(Path.Combine(root, "versioning.md"));
            var chinese = File.ReadAllText(Path.Combine(root, "versioning.zh-TW.md"));

            Assert.Contains("(versioning.zh-TW.md)", english);
            Assert.Contains("(versioning.md)", chinese);
        }

        private static DiagnosticSeverity Severity(UpaRule rule) =>
            Enum.Parse<DiagnosticSeverity>(rule.DefaultSeverity, ignoreCase: true);

        private static int Number(UpaRule rule) => int.Parse(rule.Id.Substring(3));

        private static string FirstLine(string path) => File.ReadLines(path).FirstOrDefault() ?? string.Empty;

        private static string RepositoryRoot()
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
