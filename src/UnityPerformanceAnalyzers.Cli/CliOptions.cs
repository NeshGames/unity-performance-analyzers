namespace UnityPerformanceAnalyzers.Cli;

/// <summary>Output format. The sarif value is reserved by the contract, not implemented.</summary>
internal enum OutputFormat
{
    Text,
    Json,
}

/// <summary>
/// Parsed command line. These argument shapes are the tool's public contract —
/// renaming or repurposing one breaks every pipeline that invokes it.
/// </summary>
internal sealed class CliOptions
{
    public List<string> Files { get; } = new();
    public List<string> References { get; } = new();
    public List<string> Defines { get; } = new();
    public List<string> AdditionalFiles { get; } = new();
    public string AssemblyName { get; private set; } = "Assembly-CSharp";
    public string? RulesetPath { get; private set; }
    public string? EditorConfigPath { get; private set; }
    public string? UnityDllDir { get; private set; }
    public bool AllWarn { get; private set; }
    public bool WholeAssembly { get; private set; }
    public string? BaselinePath { get; private set; }
    public string? WriteBaselinePath { get; private set; }

    /// <summary>True when either baseline path was given, so keys have to be computed.</summary>
    public bool UsesBaseline => BaselinePath is object || WriteBaselinePath is object;

    /// <summary>
    /// The directory a baseline key is relative to. The contract defines its own root: anchoring
    /// to the working directory would key the same file differently depending on where the
    /// command was run, and a baseline is meant to travel between machines.
    /// </summary>
    public string BaselineDirectory
    {
        get
        {
            var path = BaselinePath ?? WriteBaselinePath;
            if (path is null)
            {
                return Directory.GetCurrentDirectory();
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            return string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
        }
    }
    public string FailOn { get; private set; } = "warning";
    public OutputFormat Format { get; private set; } = OutputFormat.Text;
    public bool ListRules { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }

    /// <summary>
    /// Parses arguments, or returns null with a message written to <paramref name="error"/>.
    /// Every failure here is exit code 2 (usage error).
    /// </summary>
    public static CliOptions? Parse(string[] args, out string? error)
    {
        // Response files expand first and in place, so everything below - including the
        // precedence rules that depend on argument order - is unaware of where an argument
        // came from.
        var expanded = ResponseFile.Expand(args, out error);
        if (expanded is null)
        {
            return null;
        }

        var (options, failure) = ParseCore(expanded);
        error = failure;
        return options;
    }

    private static (CliOptions? Options, string? Error) ParseCore(string[] args)
    {
        var options = new CliOptions();
        string? error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            string? TakeValue()
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{arg} requires a value.";
                    return null;
                }

                return args[++i];
            }

            switch (arg)
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--version":
                    options.ShowVersion = true;
                    break;
                case "--list-rules":
                    options.ListRules = true;
                    break;
                case "--all-warn":
                    options.AllWarn = true;
                    break;
                case "--whole-assembly":
                    options.WholeAssembly = true;
                    break;
                case "--reference":
                    if (TakeValue() is not { } reference) return (null, error);
                    options.References.Add(reference);
                    break;
                case "--define":
                    if (TakeValue() is not { } define) return (null, error);
                    options.Defines.Add(define);
                    break;
                case "--additionalfile":
                    if (TakeValue() is not { } additionalFile) return (null, error);
                    options.AdditionalFiles.Add(additionalFile);
                    break;
                case "--assembly-name":
                    if (TakeValue() is not { } assemblyName) return (null, error);
                    options.AssemblyName = assemblyName;
                    break;
                case "--ruleset":
                    if (TakeValue() is not { } ruleset) return (null, error);
                    options.RulesetPath = ruleset;
                    break;
                case "--editorconfig":
                    if (TakeValue() is not { } editorConfig) return (null, error);
                    options.EditorConfigPath = editorConfig;
                    break;
                case "--baseline":
                    if (TakeValue() is not { } baseline) return (null, error);
                    options.BaselinePath = baseline;
                    break;
                case "--write-baseline":
                    if (TakeValue() is not { } writeBaseline) return (null, error);
                    options.WriteBaselinePath = writeBaseline;
                    break;
                case "--unity-dll-dir":
                    if (TakeValue() is not { } unityDllDir) return (null, error);
                    options.UnityDllDir = unityDllDir;
                    break;
                case "--fail-on":
                    if (TakeValue() is not { } failOn) return (null, error);
                    if (failOn is not ("none" or "info" or "warning" or "error"))
                    {
                        error = $"--fail-on expects none|info|warning|error, got '{failOn}'.";
                        return (null, error);
                    }

                    options.FailOn = failOn;
                    break;
                case "--format":
                    if (TakeValue() is not { } format) return (null, error);
                    switch (format)
                    {
                        case "text":
                            options.Format = OutputFormat.Text;
                            break;
                        case "json":
                            options.Format = OutputFormat.Json;
                            break;
                        case "sarif":
                            // Reserved by the contract so adding it later is not a
                            // breaking change; giving it today is a usage error.
                            error = "--format sarif is reserved and not implemented yet.";
                            return (null, error);
                        default:
                            error = $"--format expects text|json, got '{format}'.";
                            return (null, error);
                    }

                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"Unknown option '{arg}'.";
                        return (null, error);
                    }

                    options.Files.Add(arg);
                    break;
            }
        }

        if (options.ShowHelp || options.ShowVersion)
        {
            return (options, null);
        }

        if (options.ListRules)
        {
            if (options.Files.Count > 0)
            {
                error = "--list-rules does not take input files.";
                return (null, error);
            }

            return (options, null);
        }

        if (options.Files.Count == 0)
        {
            error = "No input files. Pass one or more .cs paths or patterns, or --list-rules.";
            return (null, error);
        }

        if (options.BaselinePath is object && options.WriteBaselinePath is object)
        {
            error = "--baseline and --write-baseline cannot be given together: one reads the "
                + "contract, the other replaces it.";
            return (null, error);
        }

        // Patterns are expanded here rather than left to the shell, which would make the
        // same command line behave differently depending on where it was typed.
        var resolved = new List<string>();
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var file in options.Files)
        {
            if (FileGlob.HasWildcard(file))
            {
                var matches = FileGlob.Expand(file);
                if (matches.Count == 0)
                {
                    error = $"No files matched: {file}";
                    return (null, error);
                }

                foreach (var match in matches)
                {
                    if (seen.Add(Path.GetFullPath(match)))
                    {
                        resolved.Add(match);
                    }
                }

                continue;
            }

            if (!File.Exists(file))
            {
                error = $"File not found: {file}";
                return (null, error);
            }

            if (seen.Add(Path.GetFullPath(file)))
            {
                resolved.Add(file);
            }
        }

        options.Files.Clear();
        options.Files.AddRange(resolved);

        foreach (var (path, label) in EnumeratePathOptions(options))
        {
            if (path is object && !File.Exists(path))
            {
                error = $"{label} not found: {path}";
                return (null, error);
            }
        }

        if (options.UnityDllDir is object && !Directory.Exists(options.UnityDllDir))
        {
            error = $"--unity-dll-dir not found: {options.UnityDllDir}";
            return (null, error);
        }

        return (options, null);
    }

    private static IEnumerable<(string? Path, string Label)> EnumeratePathOptions(CliOptions options)
    {
        yield return (options.RulesetPath, "--ruleset");
        yield return (options.EditorConfigPath, "--editorconfig");
        foreach (var additionalFile in options.AdditionalFiles)
        {
            yield return (additionalFile, "--additionalfile");
        }
    }
}
