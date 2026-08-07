using System;
using System.Linq;
using UnityPerformanceAnalyzers.RuleManifest;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Guards the invariants of the preset single source of truth. Content-level drift
    /// between the table and the generated files is caught by the CI regeneration diff;
    /// these tests catch malformed table rows before they ever generate.
    /// </summary>
    public class PresetTableTests
    {
        [Fact]
        public void UpaRows_AreUniqueSortedAndWellFormed()
        {
            var ids = PresetTable.UpaRows.Select(r => r.Id).ToArray();

            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
            Assert.All(ids, id => Assert.Matches(@"^UPA\d{4}$", id));
        }

        [Fact]
        public void UpaRows_UseOnlyCanonicalSeverities()
        {
            foreach (var row in PresetTable.UpaRows)
            {
                foreach (var severity in new[] { row.Minimal, row.Recommended, row.Strict, row.Cysharp })
                {
                    // Throws on anything outside none/info/warning/error.
                    _ = PresetTable.ToRulesetAction(severity);
                }
            }
        }

        // UPA1001 deliberately keeps its default Warning everywhere; UPA3000-3004 live
        // only in the webgl-addon (an explicit base-preset entry would override the
        // addon's Include) and in editor-relaxed.
        [Fact]
        public void DeliberateAbsences_StayAbsent()
        {
            var ids = PresetTable.UpaRows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

            Assert.DoesNotContain("UPA1001", ids);
            Assert.All(PresetTable.WebGlRules, id => Assert.DoesNotContain(id, ids));
        }

        [Fact]
        public void SandboxOverrides_ReferenceKnownRules()
        {
            var ids = PresetTable.UpaRows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            Assert.All(PresetTable.SandboxOverrides.Keys, id => Assert.Contains(id, ids));
        }

        // The sandbox caught a generator header that made csc reject every ruleset
        // (an XML comment containing "--", CS8035) — so every emitted .ruleset must
        // round-trip through a real XML parser here.
        [Fact]
        public void EmittedRulesets_AreValidXml()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "upa-preset-emit-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var written = RuleManifest.PresetEmitter.WriteAll(dir);
                var rulesets = written.Where(p => p.EndsWith(".ruleset", StringComparison.Ordinal)).ToArray();
                Assert.NotEmpty(rulesets);
                foreach (var path in rulesets)
                {
                    var document = System.Xml.Linq.XDocument.Load(path);
                    Assert.Equal("RuleSet", document.Root!.Name.LocalName);
                }
            }
            finally
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SeverityMappings_MatchChannelConventions()
        {
            Assert.Equal("Info", PresetTable.ToRulesetAction("info"));
            Assert.Equal("suggestion", PresetTable.ToEditorconfigSeverity("info"));
            Assert.Equal("None", PresetTable.ToRulesetAction("none"));
            Assert.Equal("error", PresetTable.ToEditorconfigSeverity("error"));
        }
    }
}
