using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Covers the Traditional Chinese diagnostic strings.
    /// </summary>
    /// <remarks>
    /// The failure mode a satellite assembly has is partial coverage: a key with no
    /// translation falls back to English at run time, so the Console shows two languages
    /// interleaved and nothing anywhere reports a problem. Every assertion here exists to
    /// turn that silence into a build failure.
    /// </remarks>
    public class LocalizedStringsTests
    {
        private static readonly string ResourceDirectory = Path.Combine(
            FindRepositoryRoot(), "src", "UnityPerformanceAnalyzers", "Resources");

        private static IReadOnlyDictionary<string, string> Load(string file)
        {
            var document = XDocument.Load(Path.Combine(ResourceDirectory, file));
            return document.Root!
                .Elements("data")
                .ToDictionary(
                    d => d.Attribute("name")!.Value,
                    d => d.Element("value")!.Value,
                    StringComparer.Ordinal);
        }

        /// <summary>
        /// Every English key has a translation, and no translation names a key that no longer
        /// exists. A rule added without a Chinese entry would otherwise ship reporting in
        /// English inside an otherwise Chinese console.
        /// </summary>
        [Fact]
        public void EveryStringIsTranslated()
        {
            var english = Load("Strings.resx");
            var chinese = Load("Strings.zh-Hant.resx");

            var untranslated = english.Keys.Where(key => !chinese.ContainsKey(key)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var orphaned = chinese.Keys.Where(key => !english.ContainsKey(key)).OrderBy(k => k, StringComparer.Ordinal).ToArray();

            Assert.True(untranslated.Length == 0, "no Chinese string for: " + string.Join(", ", untranslated));
            Assert.True(orphaned.Length == 0, "Chinese string for a key that no longer exists: " + string.Join(", ", orphaned));
        }

        /// <summary>
        /// The placeholders have to survive translation. A message format that lost {1} throws
        /// at format time, and one that gained a placeholder throws too — in the middle of a
        /// compile, on the machines that read Chinese and nowhere else.
        /// </summary>
        [Fact]
        public void PlaceholdersSurviveTranslation()
        {
            var english = Load("Strings.resx");
            var chinese = Load("Strings.zh-Hant.resx");
            var problems = new List<string>();

            foreach (var pair in english)
            {
                if (!chinese.TryGetValue(pair.Key, out var translated))
                {
                    continue;
                }

                var expected = Placeholders(pair.Value);
                var actual = Placeholders(translated);

                if (!expected.SetEquals(actual))
                {
                    problems.Add($"{pair.Key}: English has {Show(expected)}, Chinese has {Show(actual)}");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// Nothing was left in English. Checked by requiring a CJK character rather than by
        /// comparing to the source, so a string copied across verbatim is caught even though
        /// it is a perfectly valid resx entry.
        /// </summary>
        [Fact]
        public void NoStringWasLeftInEnglish()
        {
            var chinese = Load("Strings.zh-Hant.resx");

            var untouched = chinese
                .Where(pair => !pair.Value.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                .Select(pair => pair.Key)
                .ToArray();

            Assert.True(untouched.Length == 0, "no Chinese characters in: " + string.Join(", ", untouched));
        }

        /// <summary>
        /// The satellite assembly is built and reachable. This is the assertion that the whole
        /// feature rests on: without it the resx is a file nobody reads.
        /// </summary>
        [Fact]
        public void TheSatelliteAssemblyResolves()
        {
            var descriptor = Descriptor("UPA0001");
            var previous = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = new CultureInfo("zh-Hant");
                var localized = descriptor.MessageFormat.ToString();

                CultureInfo.CurrentUICulture = new CultureInfo("en-US");
                var english = descriptor.MessageFormat.ToString();

                Assert.NotEqual(english, localized);
                Assert.Contains("原生元件查詢", localized);
                Assert.Contains("native component lookup", english);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        /// <summary>
        /// zh-TW resolves through zh-Hant, which is the culture a Traditional Chinese Windows
        /// actually reports. A satellite named for the specific culture would leave the
        /// neutral one unmatched, and vice versa; this pins which way round it is.
        /// </summary>
        [Theory]
        [InlineData("zh-TW")]
        [InlineData("zh-HK")]
        [InlineData("zh-Hant")]
        public void TraditionalChineseCulturesAllResolve(string culture)
        {
            var descriptor = Descriptor("UPA0019");
            var previous = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = new CultureInfo(culture);
                Assert.Contains("協程", descriptor.Title.ToString());
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        /// <summary>
        /// Simplified Chinese has no translation here, so it must fall back to English rather
        /// than to Traditional — a reader who cannot read the script would otherwise be worse
        /// off than one who got English.
        /// </summary>
        [Fact]
        public void SimplifiedChineseFallsBackToEnglish()
        {
            var descriptor = Descriptor("UPA0001");
            var previous = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
                Assert.Contains("native component lookup", descriptor.MessageFormat.ToString());
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        private static DiagnosticDescriptor Descriptor(string id) =>
            typeof(UPA0001ComponentLookupAnalyzer).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
                .SelectMany(a => a.SupportedDiagnostics)
                .First(d => d.Id == id);

        private static HashSet<string> Placeholders(string value) =>
            Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

        private static string Show(IEnumerable<string> values)
        {
            var ordered = values.OrderBy(v => v, StringComparer.Ordinal).ToArray();
            return ordered.Length == 0 ? "none" : string.Join(" ", ordered);
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
