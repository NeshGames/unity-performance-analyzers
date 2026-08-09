using System;
using System.IO;
using System.Linq;
using UnityPerformanceAnalyzers.Catalog;
using UnityPerformanceAnalyzers.RuleManifest;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Guards the half of the README tables that cannot be derived. Which rules exist comes
    /// from the analyzers; what each one reports is written by hand, and a rule added without
    /// that description would otherwise render an empty cell — the silent drift this
    /// generation exists to end. The generator refuses instead, and this proves it.
    /// </summary>
    public class ReadmeEmitterTests
    {
        [Fact]
        public void EveryRuleHasADescription()
        {
            // A rule with no entry makes the emitter throw, so a clean run over a temporary
            // copy of the READMEs is the assertion: it means nothing was left undescribed.
            var root = CreateTemporaryReadmes();
            try
            {
                ReadmeEmitter.WriteAll(root);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void GeneratedTablesListEveryRuleExactlyOnce()
        {
            var root = CreateTemporaryReadmes();
            try
            {
                ReadmeEmitter.WriteAll(root);
                var english = File.ReadAllText(Path.Combine(root, "README.md"));

                foreach (var rule in UpaRuleCatalog.Rules())
                {
                    var link = $"[{rule.Id}](docs/rules/{rule.Id}.md)";
                    var occurrences = english.Split(new[] { link }, StringSplitOptions.None).Length - 1;
                    Assert.True(occurrences == 1, $"{rule.Id} appears {occurrences} times; expected exactly one.");
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void MissingMarkersAreAnError()
        {
            var root = Path.Combine(Path.GetTempPath(), "upa-readme-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "README.md"), "no markers here");
                File.WriteAllText(Path.Combine(root, "README.zh-TW.md"), "no markers here");

                Assert.Throws<InvalidOperationException>(() => ReadmeEmitter.WriteAll(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTemporaryReadmes()
        {
            var root = Path.Combine(Path.GetTempPath(), "upa-readme-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var skeleton =
                "# heading\n\n"
                + ReadmeEmitter.CountBeginMarker + "0" + ReadmeEmitter.CountEndMarker + "\n\n"
                + ReadmeEmitter.BeginMarker + "\n" + ReadmeEmitter.EndMarker + "\n";

            File.WriteAllText(Path.Combine(root, "README.md"), skeleton);
            File.WriteAllText(Path.Combine(root, "README.zh-TW.md"), skeleton);
            return root;
        }
    }
}
