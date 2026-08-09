namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// Turns a path into the one spelling a baseline key may use. A baseline is committed and
/// read back on other machines, so two spellings of the same file must not produce two keys:
/// that failure is invisible, since a run that suppresses nothing looks exactly like a run
/// with nothing to suppress.
/// </summary>
internal static class BaselinePath
{
    /// <summary>
    /// The canonical form of <paramref name="path"/> relative to <paramref name="baselineDirectory"/>,
    /// or null when it lies outside that directory.
    /// </summary>
    /// <remarks>
    /// The anchor is the directory holding the baseline file, never the process working
    /// directory — a contract defines its own root, or the same checkout keys differently
    /// depending on where the command was typed.
    /// </remarks>
    public static string? Normalize(string path, string baselineDirectory)
    {
        var full = ResolveRealSpelling(Path.GetFullPath(path));
        var relative = Path.GetRelativePath(baselineDirectory, full).Replace('\\', '/');

        if (relative.Length == 0
            || relative == ".."
            || relative.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return null;
        }

        return relative;
    }

    /// <summary>
    /// The path as the file system actually spells it. Comparison is Ordinal on every
    /// platform, so a baseline written on Windows from <c>assets/a.cs</c> would otherwise
    /// miss every entry once it reaches a case-sensitive CI checkout.
    /// </summary>
    public static string ResolveRealSpelling(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return fullPath;
        }

        var remainder = fullPath.Substring(root.Length)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

        var resolved = root;
        foreach (var segment in remainder)
        {
            var actual = ActualName(resolved, segment);
            resolved = Path.Combine(resolved, actual);
        }

        return resolved;
    }

    /// <summary>
    /// The on-disk spelling of one path segment, or the given spelling when nothing matches —
    /// entries for deleted files still have to normalize to something stable.
    /// </summary>
    private static string ActualName(string directory, string segment)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, segment))
            {
                var name = Path.GetFileName(entry);
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return segment;
    }

    /// <summary>
    /// True when a stored <c>file</c> value is already the canonical form. Values are required
    /// to equal their own normalization rather than merely normalize to it: otherwise
    /// <c>Assets/A.cs</c>, <c>Assets\A.cs</c> and <c>Assets/./A.cs</c> are three distinct raw
    /// tuples for one file, and duplicate detection — which exists to stop two suppression
    /// quotas hiding behind one key — would have to run before or after normalization, either
    /// of which silently merges or drops one of them.
    /// </summary>
    public static bool IsCanonical(string value)
    {
        if (value.Length == 0
            || value.Contains('\\')
            || value.Contains("//", StringComparison.Ordinal)
            || value.StartsWith('/')
            || value.EndsWith('/')
            || IsDriveQualified(value))
        {
            return false;
        }

        foreach (var segment in value.Split('/'))
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the value opens with a drive letter. Checked directly rather than through
    /// Path.IsPathRooted, which answers per host: <c>C:/repo/A.cs</c> would be rejected on
    /// Windows and taken as a relative path on Linux, so one committed baseline would validate
    /// differently depending on where it was read.
    /// </summary>
    private static bool IsDriveQualified(string value) =>
        value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';
}
