using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// The Claude Code / Codex plugin at the repository root. Agents parse a skill's frontmatter
    /// and silently drop one that does not satisfy them, so a broken skill never announces
    /// itself; and a plugin version that trails the package tells an agent it has a rule set it
    /// does not have.
    /// </summary>
    public class AgentPluginTests
    {
        private static readonly string Root = TestRepository.Root;

        private static readonly Regex s_frontmatter = new Regex(
            @"\A---\r?\n(?<body>[\s\S]*?)\r?\n---\r?\n", RegexOptions.Compiled);

        [Fact]
        public void EverySkillHasFrontmatterNamedAfterItsFolder()
        {
            var skills = Directory.GetDirectories(Path.Combine(Root, "skills"));
            Assert.NotEmpty(skills);

            foreach (var directory in skills)
            {
                var folder = Path.GetFileName(directory);
                var text = File.ReadAllText(Path.Combine(directory, "SKILL.md"));
                var match = s_frontmatter.Match(text);
                Assert.True(match.Success, folder + "/SKILL.md has no frontmatter block");

                var fields = match.Groups["body"].Value
                    .Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .Where(line => line.Contains(':'))
                    .ToDictionary(
                        line => line.Substring(0, line.IndexOf(':')).Trim(),
                        line => line.Substring(line.IndexOf(':') + 1).Trim());

                Assert.Equal(folder, fields.GetValueOrDefault("name"));
                var description = fields.GetValueOrDefault("description") ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(description), folder + " has no description");
                Assert.True(description.Length <= 1024, folder + " description exceeds 1024 characters");
            }
        }

        [Theory]
        [InlineData(".claude-plugin/plugin.json")]
        [InlineData(".codex-plugin/plugin.json")]
        public void PluginVersionFollowsTheBuildVersion(string manifest)
        {
            var props = File.ReadAllText(Path.Combine(Root, "src", "Directory.Build.props"));
            var buildVersion = Regex.Match(props, "<Version>(?<v>[^<]+)</Version>").Groups["v"].Value;

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, manifest)));
            Assert.Equal(buildVersion, document.RootElement.GetProperty("version").GetString());
        }

        [Fact]
        public void TheMarketplaceListsThePluginByItsOwnName()
        {
            using var marketplace = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, ".claude-plugin", "marketplace.json")));
            using var plugin = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, ".claude-plugin", "plugin.json")));

            var listed = marketplace.RootElement.GetProperty("plugins").EnumerateArray()
                .Select(entry => entry.GetProperty("name").GetString())
                .ToArray();

            Assert.Contains(plugin.RootElement.GetProperty("name").GetString(), listed);
        }
    }
}
