using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Holds the contribution guide to the repository it describes.
    /// </summary>
    /// <remarks>
    /// A walkthrough is a promise about a layout, and layouts move. The failure is
    /// particularly costly here because the reader is a first-time contributor: a path that
    /// does not exist reads as "this project is abandoned" rather than "this line is stale".
    /// <para>
    /// The acceptance criterion behind this guide asked for the walkthrough to be verified
    /// by following it end to end for a trivial rule. That is not possible from a test — a
    /// new rule needs an ID, and IDs are permanent once shipped, so allocating one is a
    /// decision rather than a build step. These assertions cover what a test can: every
    /// path the guide names exists, and every command it gives is the command CI runs.
    /// </para>
    /// </remarks>
    public class ContributionDocsTests
    {
        private static readonly string Root = FindRepositoryRoot();

        /// <summary>
        /// Paths inside backticks that name something in this repository. Placeholders such
        /// as <c>UPA####Something.cs</c> are matched by shape rather than existence.
        /// </summary>
        private static readonly Regex s_path =
            new Regex(@"`((?:src|docs|package)/[A-Za-z0-9_.~#/\-]+)`");

        private static readonly string[] Guides = { "CONTRIBUTING.md", "CONTRIBUTING.zh-TW.md" };

        [Fact]
        public void EveryPathTheGuideNamesExists()
        {
            var missing = new List<string>();

            foreach (var guide in Guides)
            {
                var text = File.ReadAllText(Path.Combine(Root, guide));
                foreach (Match match in s_path.Matches(text))
                {
                    var value = match.Groups[1].Value;
                    if (value.Contains("####", StringComparison.Ordinal))
                    {
                        // A placeholder for a rule that does not exist yet. Its directory
                        // still has to, or the instruction points nowhere.
                        var directory = Path.GetDirectoryName(value.Replace('/', Path.DirectorySeparatorChar))!;
                        if (!Directory.Exists(Path.Combine(Root, directory)))
                        {
                            missing.Add($"{guide}: {value} (directory {directory})");
                        }

                        continue;
                    }

                    var full = Path.Combine(Root, value.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full) && !Directory.Exists(full))
                    {
                        missing.Add($"{guide}: {value}");
                    }
                }
            }

            Assert.True(missing.Count == 0, "paths the guide names that do not exist:"
                + Environment.NewLine + string.Join(Environment.NewLine, missing));
        }

        /// <summary>
        /// The regeneration commands. Documented separately from CI, which is exactly how a
        /// contributor ends up running something that has not been the real command for two
        /// versions and concluding the build is broken.
        /// </summary>
        [Fact]
        public void GeneratorCommandsMatchWhatCiRuns()
        {
            var workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "pr.yml"));

            foreach (var guide in Guides)
            {
                var text = File.ReadAllText(Path.Combine(Root, guide));
                foreach (var mode in new[] { "--readme", "--presets" })
                {
                    Assert.True(
                        text.Contains($"--project src/UnityPerformanceAnalyzers.RuleManifest -c Release -- {mode}",
                            StringComparison.Ordinal),
                        $"{guide} does not give the {mode} command in the form CI runs it");

                    Assert.Contains($"src/UnityPerformanceAnalyzers.RuleManifest -c Release --no-build -- {mode}", workflow);
                }
            }
        }

        [Fact]
        public void TheCommunityFilesExistAndLinkTogether()
        {
            foreach (var file in new[] { "CONTRIBUTING.md", "CONTRIBUTING.zh-TW.md", "SECURITY.md", "CODE_OF_CONDUCT.md" })
            {
                Assert.True(File.Exists(Path.Combine(Root, file)), file + " is missing");
            }

            var english = File.ReadAllText(Path.Combine(Root, "CONTRIBUTING.md"));
            var chinese = File.ReadAllText(Path.Combine(Root, "CONTRIBUTING.zh-TW.md"));

            Assert.Contains("(CONTRIBUTING.zh-TW.md)", english);
            Assert.Contains("(CONTRIBUTING.md)", chinese);

            // Both guides point at the same two policies, so a reader who arrives in either
            // language reaches the same answers about upgrades and disclosure.
            Assert.Contains("docs/versioning.md", english);
            Assert.Contains("docs/versioning.zh-TW.md", chinese);
            Assert.Contains("SECURITY.md", english);
            Assert.Contains("SECURITY.md", chinese);
        }

        /// <summary>
        /// The two templates and the fields that make a report actionable. The snippet is
        /// the field that decides how fast a false positive is fixed, and the evidence
        /// question is the whole reason a rule proposal is accepted or not — a template
        /// that stops asking for either quietly lowers the bar.
        /// </summary>
        [Fact]
        public void IssueTemplatesAskForWhatMakesAReportActionable()
        {
            var directory = Path.Combine(Root, ".github", "ISSUE_TEMPLATE");

            var falsePositive = File.ReadAllText(Path.Combine(directory, "false-positive.yml"));
            foreach (var label in new[] { "Rule ID", "Smallest snippet that still triggers it", "Unity version" })
            {
                Assert.True(IsMandatory(falsePositive, label), $"false-positive.yml asks for '{label}' but does not require it");
            }

            var proposal = File.ReadAllText(Path.Combine(directory, "rule-proposal.yml"));
            Assert.Contains("IL2CPP", proposal);
            Assert.Contains("Where the numbers come from", proposal);
            Assert.Contains("When the pattern is legitimate", proposal);

            var config = File.ReadAllText(Path.Combine(directory, "config.yml"));
            Assert.Contains("security/advisories/new", config);
            Assert.Contains("docs/versioning.md", config);
        }

        /// <summary>
        /// Whether the field carrying this label is mandatory. Asked per field rather than
        /// by counting: a count passes just as well when someone makes the snippet optional
        /// and adds a required field somewhere else.
        /// </summary>
        private static bool IsMandatory(string template, string label)
        {
            var start = template.IndexOf("label: " + label, StringComparison.Ordinal);
            Assert.True(start >= 0, "no field labelled " + label);

            var next = template.IndexOf("- type:", start, StringComparison.Ordinal);
            var field = next < 0 ? template.Substring(start) : template.Substring(start, next - start);
            return field.Contains("required: true", StringComparison.Ordinal);
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
