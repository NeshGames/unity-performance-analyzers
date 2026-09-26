using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityPerformanceAnalyzers.Catalog;
using UnityPerformanceAnalyzers.Cli;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Covers the coexistence rulesets and the overlap page they come from.
    /// </summary>
    public sealed class CoexistPresetTests : IDisposable
    {
        private static readonly string Root = TestRepository.Root;

        private static readonly string PresetDirectory =
            Path.Combine(Root, "package", "Samples~", "Ruleset Presets");

        private readonly string _dir;

        public CoexistPresetTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "upa-coexist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        /// <summary>
        /// The assertion this whole design turns on. A rule entry in the including file beats
        /// the same entry in an included file, so a coexistence file written to be included
        /// by a preset silences nothing — every base preset grades every rule. That version
        /// looks correct in review and in the file listing, and the only way to tell the
        /// difference is to run it.
        /// </summary>
        [Fact]
        public void CoexistRulesets_ActuallySilenceTheirRules()
        {
            foreach (var coexist in RuleManifest.PresetTable.Coexists)
            {
                var ruleset = Path.Combine(PresetDirectory, coexist.Name + "-coexist.ruleset");
                var genuinelySilenced = 0;

                foreach (var (id, _) in coexist.Rules)
                {
                    var withBase = Report(id, coexist.Base);
                    var withCoexist = Report(id, coexist.Name + "-coexist");

                    Assert.False(withCoexist, $"{ruleset} does not silence {id}");
                    if (withBase)
                    {
                        genuinelySilenced++;
                    }
                }

                // Non-vacuity, per overlay rather than per rule. A rule the chosen base
                // already holds at none is inert here and says so in the table; a file where
                // every entry is inert is a file that does nothing, which is the failure the
                // include direction would otherwise have produced silently.
                //
                // An overlay with no entries at all is a different thing and is allowed: the
                // vs one is deliberately empty, because the single overlap it used to defer
                // was measured and gave up three true positives to buy one false one. What
                // this assertion guards against is entries that look effective and are not.
                if (coexist.Rules.Length > 0)
                {
                    Assert.True(
                        genuinelySilenced > 0,
                        $"{ruleset} silences nothing the {coexist.Base} preset was reporting");
                }

                // The other half of the composition: the base has to still apply. Without the
                // Include the file is a bare list of disables, which silences its own rules
                // correctly and quietly drops every other rule the preset was grading - a
                // project that thinks it deferred four rules and actually deferred all of them.
                Assert.True(
                    Report("UPA0006", coexist.Name + "-coexist"),
                    $"{ruleset} does not carry the {coexist.Base} preset through: UPA0006 stopped reporting");
            }
        }

        /// <summary>
        /// Whether one rule reports under one preset, with every rule forced on first so the
        /// answer is about the ruleset rather than about defaults.
        /// </summary>
        private bool Report(string ruleId, string preset)
        {
            var file = Path.Combine(_dir, "Probe.cs");
            File.WriteAllText(file, SourceFor(ruleId));

            var arguments = new List<string>
            {
                file,
                "--ruleset", Path.Combine(PresetDirectory, preset + ".ruleset"),
                "--fail-on", "none",
                "--format", "json",
            };

            // Package-conditional rules do not exist at all without the reference, so the
            // probe would report nothing and the silencing would prove nothing.
            if (ruleId.StartsWith("UPA2", StringComparison.Ordinal))
            {
                arguments.AddRange(new[] { "--reference", "UniTask" });
            }

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliEntryPoint.Run(arguments.ToArray(), stdout, stderr);

            Assert.Equal(0, exitCode);
            return stdout.ToString().Contains($"\"{ruleId}\"", StringComparison.Ordinal);
        }

        /// <summary>Source that trips one rule. Only the deferred rules need an entry.</summary>
        private static string SourceFor(string ruleId) => ruleId switch
        {
            "UPA0003" => Wrap(@"var id = Shader.PropertyToID(""_Color"");
        GetComponent<Renderer>().material.SetFloat(""_Cutoff"", 1f);"),
            "UPA0005" => Wrap(@"Debug.Log(""tick"");"),
            "UPA0014" => Wrap(@"var found = GameObject.Find(""Player"");"),
            "UPA0015" => Wrap(@"var camera = Camera.main;"),
            "UPA0016" => Wrap(@"SendMessage(""Ping"");"),
            // Graded by every base preset and by none of the overlays: the probe for whether
            // the base came through the Include at all.
            "UPA0006" => Wrap(@"var boxed = (object)42;"),
            "UPA2012" => @"
using UnityEngine;

public class Probe : MonoBehaviour
{
    async void Update()
    {
        await System.Threading.Tasks.Task.Yield();
    }
}",
            _ => throw new InvalidOperationException(
                $"no probe source for {ruleId} - add one when adding it to a coexistence file"),
        };

        private static string Wrap(string body) => @"
using UnityEngine;

public class Probe : MonoBehaviour
{
    void Update()
    {
        " + body + @"
    }
}";

        /// <summary>
        /// The overlap page carries a row per rule. Its own maintenance list asked for this
        /// to be a test rather than something to remember; a rule added without a row is a
        /// page that quietly describes 46 of 47 rules.
        /// </summary>
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
