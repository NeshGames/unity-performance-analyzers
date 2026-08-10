namespace UnityPerformanceAnalyzers.Cli;

/// <summary>Output format.</summary>
internal enum OutputFormat
{
    Text,
    Json,

    /// <summary>SARIF 2.1.0, the format every code-scanning service reads.</summary>
    Sarif,

    /// <summary>GitHub workflow commands, which become inline annotations with no upload.</summary>
    Github,
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

    /// <summary>Rewrite the baseline with unused quota removed, then exit.</summary>
    public bool PruneBaseline { get; private set; }

    /// <summary>List the unused entries rather than only counting them.</summary>
    public bool ReportStaleBaseline { get; private set; }

    /// <summary>Exit 1 when the baseline holds quota this run did not use.</summary>
    public bool FailOnStale { get; private set; }

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

    /// <summary>Where to write the generated response file, or null outside that mode.</summary>
    public string? InitArgsPath { get; private set; }

    /// <summary>The Unity project root that --init-args reads. Only meaningful in that mode.</summary>
    public string ProjectDirectory { get; private set; } = ".";
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
                case "--init-args":
                    if (TakeValue() is not { } initArgsPath) return (null, error);
                    options.InitArgsPath = initArgsPath;
                    break;
                case "--project":
                    if (TakeValue() is not { } projectDirectory) return (null, error);
                    options.ProjectDirectory = projectDirectory;
                    break;
                case "--prune-baseline":
                    options.PruneBaseline = true;
                    break;
                case "--report-stale-baseline":
                    options.ReportStaleBaseline = true;
                    break;
                case "--fail-on-stale":
                    options.FailOnStale = true;
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
                            options.Format = OutputFormat.Sarif;
                            break;
                        case "github":
                            options.Format = OutputFormat.Github;
                            break;
                        default:
                            error = $"--format expects text|json|sarif|github, got '{format}'.";
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

        if (options.InitArgsPath is object)
        {
            // Each of the three modes answers a different question, and a command line that
            // asks two of them has no defensible answer to pick.
            if (options.Files.Count > 0 || options.ListRules)
            {
                error = "--init-args generates a response file, so it takes neither input "
                    + "files nor --list-rules.";
                return (null, error);
            }

            return (options, null);
        }

        if (options.ListRules)
        {
            if (options.Files.Count > 0)
            {
                error = "--list-rules does not take input files.";
                return (null, error);
            }

            // SARIF and the workflow commands describe findings, and the catalog mode produces
            // none. Rendering text instead would be a silent substitution in a mode whose whole
            // purpose is machine consumption.
            if (options.Format is OutputFormat.Sarif or OutputFormat.Github)
            {
                error = "--list-rules supports --format text|json only: "
                    + "sarif and github describe findings, not a rule catalog.";
                return (null, error);
            }

            return (options, null);
        }

        if (options.Files.Count == 0)
        {
            error = "No input files. Pass one or more .cs paths or patterns, or --list-rules.";
            return (null, error);
        }

        // Each of the three needs a baseline to act on, and each fails in a different
        // direction without one: pruning would have nothing to rewrite, the report nothing to
        // list, and the gate would pass every run for want of anything to check.
        foreach (var (given, flag) in new[]
                 {
                     (options.PruneBaseline, "--prune-baseline"),
                     (options.ReportStaleBaseline, "--report-stale-baseline"),
                     (options.FailOnStale, "--fail-on-stale"),
                 })
        {
            if (given && options.BaselinePath is null)
            {
                error = $"{flag} needs --baseline <path>: it acts on an existing baseline.";
                return (null, error);
            }
        }

        if (options.PruneBaseline && options.WriteBaselinePath is object)
        {
            error = "--prune-baseline and --write-baseline cannot be given together: one "
                + "removes quota the run did not use, the other replaces the contract with "
                + "everything this run found.";
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
