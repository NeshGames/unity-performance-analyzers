using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Keeps the three string surfaces aligned: the .resx resources, the Strings.cs
    /// nameof-constant mirror, and the id-derived keys the UpaDescriptor factory resolves
    /// at runtime (a missing resource there degrades to an empty string with no compile
    /// error, so it must fail here instead).
    /// </summary>
    public class StringsResourceConsistencyTests
    {
        private static readonly Type s_strings = typeof(UPA0001ComponentLookupAnalyzer).Assembly
            .GetType("UnityPerformanceAnalyzers.Strings")!;

        private static HashSet<string> ResourceKeys()
        {
            var manager = (System.Resources.ResourceManager)s_strings
                .GetField("ResourceManager", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            var set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
            return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
        }

        private static HashSet<string> StringsConstants() =>
            s_strings.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToHashSet(StringComparer.Ordinal);

        [Fact]
        public void StringsConstants_AndResxKeys_MirrorExactly()
        {
            var keys = ResourceKeys();
            var constants = StringsConstants();

            Assert.Empty(constants.Except(keys)); // constant without a resource entry
            Assert.Empty(keys.Except(constants)); // orphaned resource entry
        }

        // The factory derives {id}Title/{id}MessageFormat/{id}Description at runtime; a
        // missing entry yields an empty localizable string, never a compile error.
        [Fact]
        public void EveryDescriptor_ResolvesNonEmptyResources()
        {
            var descriptors = typeof(UPA0001ComponentLookupAnalyzer).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
                .SelectMany(a => a.SupportedDiagnostics);

            foreach (var descriptor in descriptors)
            {
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Title.ToString()), $"{descriptor.Id} Title");
                Assert.False(string.IsNullOrWhiteSpace(descriptor.MessageFormat.ToString()), $"{descriptor.Id} MessageFormat");
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Description.ToString()), $"{descriptor.Id} Description");
            }
        }
    }
}
