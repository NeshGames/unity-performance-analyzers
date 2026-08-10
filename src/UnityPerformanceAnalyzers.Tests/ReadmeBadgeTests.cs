using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Badges are the one part of a README that goes stale without anyone editing it: they
    /// point at a workflow file, a release page or a licence, and the thing they point at
    /// moves. A badge that has rotted does not look broken - it renders as a grey "no
    /// status" chip that reads like a project nobody runs CI on, which is worse than having
    /// no badge at all.
    ///
    /// Two properties are worth holding. Both READMEs must carry the same badges, because
    /// the Chinese page is the one that gets forgotten when a badge is added. And every
    /// local target a badge links to must exist, which is the half a renamed workflow file
    /// silently breaks.
    /// </summary>
    public class ReadmeBadgeTests
    {
        private const string BeginMarker = "<!-- badges -->";
        private const string EndMarker = "<!-- /badges -->";

        [Fact]
        public void BothReadmesCarryTheSameBadges()
        {
            var root = FindRepositoryRoot();
            var english = BadgeBlock(Path.Combine(root, "README.md"));
            var chinese = BadgeBlock(Path.Combine(root, "README.zh-TW.md"));

            Assert.Equal(english, chinese);
        }

        [Fact]
        public void TheBadgeBlockIsNotEmpty()
        {
            // Without this the test above passes on two files that each have an empty block,
            // which is precisely the state that removing a badge from one and then "fixing"
            // the failure by removing it from the other would produce.
            var root = FindRepositoryRoot();
            var badges = BadgeBlock(Path.Combine(root, "README.md"));

            Assert.NotEmpty(badges);
        }

        [Fact]
        public void EveryWorkflowBadgeNamesAWorkflowThatExists()
        {
            var root = FindRepositoryRoot();
            var badges = BadgeBlock(Path.Combine(root, "README.md"));

            var referenced = Regex.Matches(
                    string.Join("\n", badges),
                    @"actions/workflows/(?<file>[A-Za-z0-9._-]+)/badge\.svg")
                .Select(match => match.Groups["file"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(referenced);

            foreach (var file in referenced)
            {
                var path = Path.Combine(root, ".github", "workflows", file);
                Assert.True(
                    File.Exists(path),
                    $"the README shows a status badge for .github/workflows/{file}, which does not exist. " +
                    "GitHub renders that as a grey 'no status' chip rather than an error.");
            }
        }

        [Fact]
        public void EveryRelativeBadgeLinkResolves()
        {
            var root = FindRepositoryRoot();
            var badges = BadgeBlock(Path.Combine(root, "README.md"));

            // The link target of a badge, not the image: [![alt](image)](target).
            var targets = Regex.Matches(string.Join("\n", badges), @"\]\((?<target>[^)]+)\)\s*$", RegexOptions.Multiline)
                .Select(match => match.Groups["target"].Value)
                .Where(target => !target.StartsWith("http", StringComparison.Ordinal))
                .ToArray();

            foreach (var target in targets)
            {
                var path = Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(
                    File.Exists(path) || Directory.Exists(path),
                    $"a badge links to '{target}', which does not exist in the repository.");
            }
        }

        /// <summary>
        /// Every image a README shows has to be somewhere the published branch carries. The
        /// overlay publishes <c>docs/rules/*</c> and excludes the rest of <c>docs/*</c>, so a
        /// screenshot filed under <c>docs/images</c> resolves here and is a broken image for
        /// everyone reading the repository — the one place the mistake would never be seen by
        /// the person who made it.
        /// </summary>
        [Theory]
        [InlineData("README.md")]
        [InlineData("README.zh-TW.md")]
        public void EveryImageAReadmeShowsExists(string readme)
        {
            var root = FindRepositoryRoot();
            var text = File.ReadAllText(Path.Combine(root, readme));

            var images = Regex.Matches(text, @"!\[[^\]]*\]\((?<path>[^)]+)\)")
                .Select(match => match.Groups["path"].Value)
                .Where(path => !path.StartsWith("http", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(images);

            foreach (var image in images)
            {
                var path = Path.Combine(root, image.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"{readme} shows '{image}', which is not in the repository.");
            }
        }

        private static string[] BadgeBlock(string path)
        {
            var text = File.ReadAllText(path);

            var start = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            var stop = text.IndexOf(EndMarker, StringComparison.Ordinal);
            Assert.True(
                start >= 0 && stop > start,
                $"{Path.GetFileName(path)} has no {BeginMarker} … {EndMarker} block.");

            return text
                .Substring(start + BeginMarker.Length, stop - start - BeginMarker.Length)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
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
