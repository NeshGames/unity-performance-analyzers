namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// Expands <c>@path</c> arguments by reading the arguments out of a file.
///
/// This exists because the documented way to use the tool as a gate does not fit on a
/// Windows command line. Unity's own compile arguments for a single real assembly - 76
/// sources, 143 defines, 262 references - come to about 34,000 characters once translated
/// into this tool's flags, and Windows refuses to start a process past 32,767. Unity
/// solves it the same way.
///
/// One argument per line, and no quote handling: quoting rules are the most common way a
/// csc response file goes wrong (does a path with a space need quotes, how are they
/// escaped), and there is no reason to inherit that. A line is an argument.
/// </summary>
internal static class ResponseFile
{
    /// <summary>
    /// Replaces every <c>@path</c> argument with the arguments in that file, in place, so the
    /// existing precedence rules (a later flag wins) are unaffected by where an argument came
    /// from. Returns null and sets <paramref name="error"/> on any usage failure.
    /// </summary>
    public static string[]? Expand(string[] args, out string? error)
    {
        error = null;
        var expanded = new List<string>(args.Length);

        foreach (var arg in args)
        {
            if (!arg.StartsWith("@", StringComparison.Ordinal))
            {
                expanded.Add(arg);
                continue;
            }

            var path = arg.Substring(1);
            if (path.Length == 0)
            {
                error = "'@' needs a path: @<response-file>.";
                return null;
            }

            string[] lines;
            try
            {
                // Reads with BOM detection, so a file saved as UTF-8 with a signature does not
                // put an invisible character at the front of the first argument.
                lines = File.ReadAllLines(path);
            }
            catch (FileNotFoundException)
            {
                error = $"Response file not found: {path}";
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                error = $"Response file not found: {path}";
                return null;
            }
            catch (IOException exception)
            {
                error = $"Could not read response file {path}: {exception.Message}";
                return null;
            }
            catch (UnauthorizedAccessException exception)
            {
                error = $"Could not read response file {path}: {exception.Message}";
                return null;
            }

            for (var line = 0; line < lines.Length; line++)
            {
                var value = lines[line].Trim();
                if (value.Length == 0 || value.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (value.StartsWith("@", StringComparison.Ordinal))
                {
                    // Refused rather than resolved. Detecting cycles is cheap; explaining which
                    // line of which nested file was at fault is not, and one generated file is
                    // all the real use case needs.
                    error = $"{path}({line + 1}): a response file cannot reference another one.";
                    return null;
                }

                expanded.Add(value);
            }
        }

        return expanded.ToArray();
    }
}
