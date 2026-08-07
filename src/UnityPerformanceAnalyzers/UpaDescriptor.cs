using Microsoft.CodeAnalysis;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Factory for the project's standard descriptor shape: title/message/description come
    /// from the Strings resources under id-derived keys ({id}Title, {id}MessageFormat,
    /// {id}Description — overridable for multi-format rules), and the help link is the
    /// rule-doc URL for the id. Because the id reaches the DiagnosticDescriptor as a
    /// parameter, the Roslyn release-tracking analyzer cannot see these creations; the
    /// AnalyzerReleases files are instead kept honest by a bidirectional test in the test
    /// project (AnalyzerReleasesConsistencyTests).
    /// </summary>
    internal static class UpaDescriptor
    {
        private const string HelpLinkBase =
            "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/";

#pragma warning disable RS1017, RS1032 // id/format are parameters by design; see the class summary.
        public static DiagnosticDescriptor Create(
            string id,
            string category,
            DiagnosticSeverity defaultSeverity,
            bool isEnabledByDefault,
            string? messageFormatKey = null,
            string? customTags = null)
        {
            var title = new LocalizableResourceString(id + "Title", Strings.ResourceManager, typeof(Strings));
            var messageFormat = new LocalizableResourceString(
                messageFormatKey ?? id + "MessageFormat", Strings.ResourceManager, typeof(Strings));
            var description = new LocalizableResourceString(id + "Description", Strings.ResourceManager, typeof(Strings));
            var helpLinkUri = HelpLinkBase + id + ".md";

            return customTags is null
                ? new DiagnosticDescriptor(
                    id, title, messageFormat, category, defaultSeverity, isEnabledByDefault, description, helpLinkUri)
                : new DiagnosticDescriptor(
                    id, title, messageFormat, category, defaultSeverity, isEnabledByDefault, description, helpLinkUri, customTags);
        }
#pragma warning restore RS1017, RS1032
    }
}
