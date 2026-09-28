using System;
using System.IO;
using System.Linq;
using UnityPerformanceAnalyzers.Catalog;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Keeps the overlap documentation synchronized with the live analyzer catalog.
    /// Ownership decisions now live in analyzer code and documentation rather than shipped
    /// coexistence rulesets.
    /// </summary>
    public sealed class OverlapDocumentationTests
    {
        private static readonly string Root = TestRepository.Root;

        [Fact]
        public void TheOverlapPagesCoverEveryRule()
        {
            foreach (var page in new[] { "overlap.md", "overlap.zh-TW.md" })
            {
                var text = File.ReadAllText(Path.Combine(Root, "docs", page));
                var missing = UpaRuleCatalog.Rules()
                    .Select(rule => rule.Id)
                    .Where(id => !text.Contains("**" + id + "**", StringComparison.Ordinal))
                    .ToArray();

                Assert.True(missing.Length == 0, page + " has no row for: " + string.Join(", ", missing));
            }
        }

        [Fact]
        public void BothOverlapLanguagesLinkToEachOther()
        {
            var english = File.ReadAllText(Path.Combine(Root, "docs", "overlap.md"));
            var chinese = File.ReadAllText(Path.Combine(Root, "docs", "overlap.zh-TW.md"));

            Assert.Contains("(overlap.zh-TW.md)", english);
            Assert.Contains("(overlap.md)", chinese);
        }
    }
}
