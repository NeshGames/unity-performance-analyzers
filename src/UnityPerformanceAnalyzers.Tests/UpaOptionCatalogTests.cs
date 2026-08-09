using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// The catalog is what generates the option list in rules.json and the commented defaults
    /// in every preset. Nothing forced a new option into it, and two of the seven keys had
    /// quietly fallen out — along with the options file itself, which is the only channel
    /// that reaches a Unity build.
    /// </summary>
    public class UpaOptionCatalogTests
    {
        // Any const named *OptionKey is an option the analyzers read. Reflection rather than a
        // second list: a list is the thing that drifted.
        private static IReadOnlyDictionary<string, string> DeclaredKeys()
        {
            var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var type in typeof(UpaAnalyzer).Assembly.GetTypes())
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsLiteral || field.FieldType != typeof(string) || !field.Name.EndsWith("OptionKey", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    keys[(string)field.GetRawConstantValue()!] = $"{type.Name}.{field.Name}";
                }
            }

            return keys;
        }

        [Fact]
        public void EveryOptionTheAnalyzersRead_IsInTheCatalog()
        {
            var cataloged = UpaOptionCatalog.Options.Select(option => option.Key).ToHashSet(StringComparer.Ordinal);
            var missing = DeclaredKeys()
                .Where(pair => !cataloged.Contains(pair.Key))
                .Select(pair => $"{pair.Key} (declared by {pair.Value})")
                .ToArray();

            Assert.Empty(missing);
        }

        [Fact]
        public void EveryCatalogedOption_IsReadByAnAnalyzer()
        {
            var declared = DeclaredKeys();
            var strays = UpaOptionCatalog.Options
                .Select(option => option.Key)
                .Where(key => !declared.ContainsKey(key))
                .ToArray();

            Assert.Empty(strays);
        }

        [Fact]
        public void OptionKeys_AreUnique()
        {
            var duplicates = UpaOptionCatalog.Options
                .GroupBy(option => option.Key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.Empty(duplicates);
        }

        // The stated default is what the presets and rules.json publish. It said
        // "HotPath,PerfCritical" for a year: a name the detector has never recognised, in an
        // option that replaces the default set rather than adding to it.
        [Fact]
        public void StatedHotPathDefaults_MatchTheRealOnes()
        {
            var stated = UpaOptionCatalog.Options.ToDictionary(option => option.Key, option => option.Default, StringComparer.Ordinal);

            Assert.Equal(
                HotPathDetector.DefaultHotMessages.OrderBy(name => name, StringComparer.Ordinal),
                stated[HotPathDetector.MessagesOptionKey].Split(',').Select(name => name.Trim()).OrderBy(name => name, StringComparer.Ordinal));

            Assert.Equal(
                HotPathDetector.DefaultHotAttributes.OrderBy(name => name, StringComparer.Ordinal),
                stated[HotPathDetector.AttributesOptionKey].Split(',').Select(name => name.Trim()).OrderBy(name => name, StringComparer.Ordinal));
        }
    }
}
