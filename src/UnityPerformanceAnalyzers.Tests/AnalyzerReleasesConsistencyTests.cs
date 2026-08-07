using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Bidirectional release-tracking check. Descriptors are created through the
    /// UpaDescriptor factory, which the Roslyn release-tracking analyzer (RS2000/RS2008)
    /// cannot see, so this test replaces it — and improves on it: the one-way original
    /// never caught a rule missing from the release files.
    /// </summary>
    public class AnalyzerReleasesConsistencyTests
    {
        private static readonly Regex s_ruleRow = new Regex(@"^(UPA\d{4}) \|", RegexOptions.Multiline);

        private static string AnalyzerProjectDir =>
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "UnityPerformanceAnalyzers"));

        [Fact]
        public void ReleaseFiles_ListExactlyTheAssemblyRuleIds()
        {
            var releaseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in new[] { "AnalyzerReleases.Shipped.md", "AnalyzerReleases.Unshipped.md" })
            {
                var path = Path.Combine(AnalyzerProjectDir, file);
                foreach (Match match in s_ruleRow.Matches(File.ReadAllText(path)))
                {
                    releaseIds.Add(match.Groups[1].Value);
                }
            }

            var assemblyIds = typeof(UPA0001ComponentLookupAnalyzer).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
                .SelectMany(a => a.SupportedDiagnostics)
                .Select(d => d.Id)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Empty(assemblyIds.Except(releaseIds)); // rule shipped without a release row
            Assert.Empty(releaseIds.Except(assemblyIds)); // release row for a rule that no longer exists
        }
    }
}
