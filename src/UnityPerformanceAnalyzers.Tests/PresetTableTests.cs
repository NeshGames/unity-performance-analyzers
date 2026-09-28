using System;
using System.Linq;
using UnityPerformanceAnalyzers.RuleManifest;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
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
                _ = PresetTable.ToRulesetAction(row.Unity);
                _ = PresetTable.ToRulesetAction(row.Ci);
            }
        }

        [Fact]
        public void UnityProfile_HasNoErrorEntries()
        {
            Assert.DoesNotContain(PresetTable.UpaRows, row => row.Unity == "error");
        }

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

        [Fact]
        public void EmittedRulesets_AreValidXml()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "upa-preset-emit-" + Guid.NewGuid().ToString("N"));
            try
            {
                var written = PresetEmitter.WriteAll(dir);
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
        public void Regenerating_RemovesGeneratedPresetsNoLongerProduced()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "upa-preset-stale-" + Guid.NewGuid().ToString("N"));
            var presets = System.IO.Path.Combine(dir, "package", "Samples~", "Ruleset Presets");
            try
            {
                System.IO.Directory.CreateDirectory(presets);
                var stale = System.IO.Path.Combine(presets, "retired.ruleset");
                System.IO.File.WriteAllText(stale,
                    "<!-- GENERATED FILE - do not edit. Severities live in PresetTable.cs;\n"
                    + "     " + PresetEmitter.OwnershipMarker + " (see that file's header). -->\n"
                    + "<RuleSet Name=\"retired\" ToolsVersion=\"10.0\" />\n");
                var handWritten = System.IO.Path.Combine(presets, "team.ruleset");
                System.IO.File.WriteAllText(handWritten, "<RuleSet Name=\"ours\" ToolsVersion=\"10.0\" />\n");

                var written = PresetEmitter.WriteAll(dir, out var removed);

                Assert.False(System.IO.File.Exists(stale));
                Assert.Equal(new[] { stale }, removed);
                Assert.True(System.IO.File.Exists(handWritten));
                Assert.All(written, path => Assert.True(System.IO.File.Exists(path)));
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
            Assert.Equal("None", PresetTable.ToRulesetAction("none"));
        }
    }
}
