using System.Text;
using System.Text.RegularExpressions;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// Expands path patterns itself instead of relying on the shell. Shells disagree about
/// this: bash needs globstar before ** means "any depth", and PowerShell passes patterns
/// to native executables untouched — so a documented command line would otherwise behave
/// differently, or fail outright, depending on where it was typed.
///
/// Supports <c>*</c> (within one segment), <c>?</c> (one character), and <c>**</c> (any
/// number of segments). Arguments with no wildcard are taken literally.
/// </summary>
internal static class FileGlob
{
    private static readonly char[] s_wildcards = { '*', '?' };

    public static bool HasWildcard(string pattern) => pattern.IndexOfAny(s_wildcards) >= 0;

    /// <summary>
    /// Files matching the pattern, sorted so runs are reproducible. Empty when nothing
    /// matches — the caller reports that as a usage error rather than analyzing nothing.
    /// </summary>
    public static IReadOnlyList<string> Expand(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var firstWildcard = normalized.IndexOfAny(s_wildcards);
        var lastSeparatorBeforeWildcard = normalized.LastIndexOf('/', Math.Max(firstWildcard - 1, 0));

        var baseDirectory = lastSeparatorBeforeWildcard <= 0
            ? "."
            : normalized[..lastSeparatorBeforeWildcard];
        var remainder = lastSeparatorBeforeWildcard <= 0
            ? normalized
            : normalized[(lastSeparatorBeforeWildcard + 1)..];

        if (!Directory.Exists(baseDirectory))
        {
            return Array.Empty<string>();
        }

        var matcher = new Regex(
            "^" + TranslateToRegex(remainder) + "$",
            OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None);

        var matches = new List<string>();
        foreach (var file in Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(baseDirectory, file).Replace('\\', '/');
            if (matcher.IsMatch(relative))
            {
                matches.Add(file);
            }
        }

        matches.Sort(StringComparer.Ordinal);
        return matches;
    }

    private static string TranslateToRegex(string pattern)
    {
        var regex = new StringBuilder();

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*' when i + 1 < pattern.Length && pattern[i + 1] == '*':
                    // "**/" spans any number of directories, including none, so that
                    // Assets/**/*.cs also matches Assets/Player.cs.
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        regex.Append("(?:[^/]+/)*");
                        i += 2;
                    }
                    else
                    {
                        regex.Append(".*");
                        i++;
                    }

                    break;
                case '*':
                    regex.Append("[^/]*");
                    break;
                case '?':
                    regex.Append("[^/]");
                    break;
                case '/':
                    regex.Append('/');
                    break;
                default:
                    regex.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        return regex.ToString();
    }
}
