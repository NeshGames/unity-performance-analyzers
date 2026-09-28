using System;
using System.Linq;
using System.Reflection;
using UnityPerformanceAnalyzers.Catalog;
using UnityPerformanceAnalyzers.RuleManifest;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class RulePolicyTableTests
    {
        [Fact]
        public void PolicyTable_CoversEveryLiveAndRetiredRuleExactlyOnce()
        {
            var rows = RulePolicyTable.Rows;
            var ids = rows.Select(row => row.Id).ToArray();

            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);

            var live = UpaRuleCatalog.Rules()
                .Select(rule => rule.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            var retiredType = typeof(UpaAnalyzer).Assembly.GetType("UnityPerformanceAnalyzers.RetiredRuleIds")!;
            var retired = ((string[])retiredType
                    .GetField("All", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetValue(null)!)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            var expected = live.Concat(retired).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, ids);

            Assert.All(rows.Where(row => live.Contains(row.Id, StringComparer.Ordinal)),
                row => Assert.NotEqual(RulePolicyTable.Policy.Retired, row.Kind));
            Assert.All(rows.Where(row => retired.Contains(row.Id, StringComparer.Ordinal)),
                row => Assert.Equal(RulePolicyTable.Policy.Retired, row.Kind));
        }

        [Fact]
        public void OptionalAndHouseRules_AreNotPromotedToCiErrors()
        {
            var policy = RulePolicyTable.Rows.ToDictionary(row => row.Id, row => row.Kind, StringComparer.Ordinal);

            var overPromoted = PresetTable.UpaRows
                .Where(row => row.Ci == "error")
                .Where(row => policy[row.Id] is RulePolicyTable.Policy.Optional or RulePolicyTable.Policy.House)
                .Select(row => row.Id)
                .ToArray();

            Assert.Empty(overPromoted);
        }

        [Fact]
        public void PlatformRules_AreExactlyTheWebGlOverlay()
        {
            var platform = RulePolicyTable.Rows
                .Where(row => row.Kind == RulePolicyTable.Policy.Platform)
                .Select(row => row.Id)
                .ToArray();

            Assert.Equal(PresetTable.WebGlRules, platform);
        }
    }
}
