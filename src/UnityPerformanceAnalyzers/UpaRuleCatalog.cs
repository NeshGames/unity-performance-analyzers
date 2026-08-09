using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers.Catalog
{
    /// <summary>
    /// One rule, as the analyzer assembly describes itself. Read by reflection rather than
    /// from the committed catalog file: that file is allowed to go stale between releases,
    /// so generating anything from it would inherit the drift the generation exists to
    /// remove.
    /// </summary>
    public sealed class UpaRule
    {
        /// <summary>Creates a catalog entry from what one descriptor reports about itself.</summary>
        public UpaRule(
            string id,
            string title,
            string category,
            string defaultSeverity,
            bool enabledByDefault,
            bool hotPath,
            string? condition,
            bool compilationWide,
            string helpUri)
        {
            Id = id;
            Title = title;
            Category = category;
            DefaultSeverity = defaultSeverity;
            EnabledByDefault = enabledByDefault;
            HotPath = hotPath;
            Condition = condition;
            CompilationWide = compilationWide;
            HelpUri = helpUri;
        }

        /// <summary>The diagnostic id, e.g. <c>UPA0001</c>.</summary>
        public string Id { get; }

        /// <summary>The descriptor title.</summary>
        public string Title { get; }

        /// <summary>The diagnostic category.</summary>
        public string Category { get; }

        /// <summary>The descriptor's severity, which presets are free to override.</summary>
        public string DefaultSeverity { get; }

        /// <summary>Whether the rule reports without a preset enabling it.</summary>
        public bool EnabledByDefault { get; }

        /// <summary>Whether the rule only looks at per-frame code.</summary>
        public bool HotPath { get; }

        /// <summary>The package or platform this rule waits for, or null when it always applies.</summary>
        public string? Condition { get; }

        /// <summary>Whether the rule's premise needs the whole assembly to be true.</summary>
        public bool CompilationWide { get; }

        /// <summary>Where the rule is documented.</summary>
        public string HelpUri { get; }
    }

    /// <summary>
    /// The one walk over the analyzer assembly. It used to be three — the manifest writer,
    /// the README emitter and the command line each had their own — and with them three
    /// copies of the rule that decides what a rule <em>is</em>: several analyzers export more
    /// than one descriptor under a single id, and the first one carries the catalog data.
    /// Three copies of that meant three places for it to be answered differently.
    /// </summary>
    /// <remarks>
    /// It lives in the analyzer assembly rather than as source shared between the tools,
    /// because the test project references both of them: as shared source the type existed
    /// twice and every use of it was ambiguous. Here there is one, which is the point.
    /// </remarks>
    public static class UpaRuleCatalog
    {
        private static Assembly AnalyzerAssembly => typeof(UpaAnalyzer).Assembly;

        /// <summary>The version the analyzer assembly was built with.</summary>
        public static string Version =>
            AnalyzerAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0]
            ?? AnalyzerAssembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        /// <summary>Every analyzer the assembly exports, instantiated, ordered by type name.</summary>
        public static IEnumerable<(Type Type, DiagnosticAnalyzer Instance)> Analyzers() =>
            AnalyzerAssembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .Select(type => (type, (DiagnosticAnalyzer)Activator.CreateInstance(type)!));

        /// <summary>Every rule the analyzer assembly exports, ordered by id.</summary>
        public static IReadOnlyList<UpaRule> Rules()
        {
            var rules = new SortedDictionary<string, UpaRule>(StringComparer.Ordinal);

            foreach (var (type, instance) in Analyzers())
            {
                var hotPath = type.GetCustomAttribute<HotPathRuleAttribute>() is object;
                var condition = type.GetCustomAttribute<ConditionalRuleAttribute>()?.Condition;
                var compilationWide = type.GetCustomAttribute<CompilationWideRuleAttribute>() is object;

                foreach (var descriptor in instance.SupportedDiagnostics)
                {
                    // A rule may export several descriptors under one id, one per message
                    // format (UPA0027, UPA0030, UPA2012). The first carries the catalog data;
                    // the rest differ only in wording. Roslyn matches by id, not by identity.
                    if (rules.ContainsKey(descriptor.Id))
                    {
                        continue;
                    }

                    rules[descriptor.Id] = new UpaRule(
                        descriptor.Id,
                        descriptor.Title.ToString(),
                        descriptor.Category,
                        descriptor.DefaultSeverity.ToString(),
                        descriptor.IsEnabledByDefault,
                        hotPath,
                        condition,
                        compilationWide,
                        descriptor.HelpLinkUri);
                }
            }

            return rules.Values.ToArray();
        }
    }
}
