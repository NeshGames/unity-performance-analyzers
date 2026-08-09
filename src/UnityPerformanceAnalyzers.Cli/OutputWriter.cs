using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using UnityPerformanceAnalyzers.Catalog;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// Renders results. The JSON shape is part of the tool's public contract and stdout
/// carries nothing else, so a caller can pipe it straight into a parser.
/// </summary>
internal static class OutputWriter
{
    /// <summary>Bumped only when an existing field changes meaning or disappears.</summary>
    private const int SchemaVersion = 1;

    // JsonNode.ToJsonString needs an explicit resolver on .NET 8 once the options
    // instance is reused, so the default reflection resolver is set here.
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static void WriteAnalysis(TextWriter stdout, AnalysisResult result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteAnalysisJson(stdout, result);
        }
        else
        {
            WriteAnalysisText(stdout, result);
        }
    }

    private static void WriteAnalysisJson(TextWriter stdout, AnalysisResult result)
    {
        var diagnostics = new JsonArray();
        foreach (var d in result.Diagnostics)
        {
            var properties = new JsonObject();
            foreach (var pair in d.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                properties[pair.Key] = pair.Value;
            }

            diagnostics.Add(new JsonObject
            {
                ["id"] = d.Id,
                ["severity"] = d.Severity,
                ["title"] = d.Title,
                ["message"] = d.Message,
                ["file"] = d.File,
                ["line"] = d.Line,
                ["column"] = d.Column,
                ["endLine"] = d.EndLine,
                ["endColumn"] = d.EndColumn,
                ["helpUri"] = d.HelpUri,
                ["properties"] = properties,
            });
        }

        var compileErrors = new JsonArray();
        foreach (var e in result.CompileErrors)
        {
            compileErrors.Add(new JsonObject
            {
                ["id"] = e.Id,
                ["message"] = e.Message,
                ["file"] = e.File,
                ["line"] = e.Line,
                ["column"] = e.Column,
            });
        }

        var document = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["toolVersion"] = AnalyzerCatalog.ToolVersion,
            ["summary"] = new JsonObject
            {
                ["error"] = Count(result, "error"),
                ["warning"] = Count(result, "warning"),
                ["info"] = Count(result, "info"),
                ["hidden"] = Count(result, "hidden"),
                ["compileErrorCount"] = result.CompileErrorCount,
                ["analyzerFailureCount"] = result.AnalyzerFailures.Length,
                ["baselineSuppressedCount"] = result.BaselineSuppressedCount,
                // null rather than 0 when the run could not tell: 0 means checked and none
                // stale, and a consumer acting on a false 0 deletes quota that is still valid.
                ["baselineStaleCount"] = result.BaselineStaleCount,
            },
            ["excludedRules"] = ToJsonArray(result.ExcludedRules),
            ["analyzerFailures"] = ToJsonArray(result.AnalyzerFailures),
            // Every compile error, not a sample: this channel is read by tools, which have no
            // reason to be spared a long list. Separate from diagnostics on purpose - these
            // are why findings may be missing, not findings themselves.
            ["compileErrors"] = compileErrors,
            ["diagnostics"] = diagnostics,
        };

        stdout.WriteLine(document.ToJsonString(s_json));
    }

    private static void WriteAnalysisText(TextWriter stdout, AnalysisResult result)
    {
        foreach (var d in result.Diagnostics)
        {
            // MSBuild diagnostic format, so IDE and CI problem matchers parse it as-is.
            stdout.WriteLine($"{d.File}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}");
            if (!string.IsNullOrEmpty(d.HelpUri))
            {
                stdout.WriteLine($"  {d.HelpUri}");
            }
        }

        if (result.Diagnostics.Length > 0)
        {
            stdout.WriteLine();
        }

        var counts = new List<string>();
        AppendCount(counts, Count(result, "error"), "error");
        AppendCount(counts, Count(result, "warning"), "warning");
        AppendCount(counts, Count(result, "info"), "info");
        var summary = counts.Count == 0 ? "No diagnostics." : string.Join(", ", counts) + ".";

        if (!result.ExcludedRules.IsEmpty)
        {
            summary += $" Not checked without --whole-assembly: {string.Join(", ", result.ExcludedRules)}.";
        }

        if (result.BaselineSuppressedCount > 0)
        {
            summary += $" {result.BaselineSuppressedCount} suppressed by the baseline.";
        }

        if (!result.AnalyzerFailures.IsEmpty)
        {
            summary += $" {result.AnalyzerFailures.Length} analyzer(s) failed to run - results are incomplete.";
        }

        stdout.WriteLine(summary);

        // Withheld when the analysis was incomplete: a depressed diagnostic count inflates
        // staleness, and following the advice would delete quota that is still doing its job.
        if (result.BaselineStaleCount is > 0L and { } stale)
        {
            stdout.WriteLine(
                $"{stale} baseline entr{(stale == 1 ? "y no longer matches" : "ies no longer match")}"
                + " - regenerate with --write-baseline.");
        }
    }

    public static void WriteRules(TextWriter stdout, ImmutableArray<UpaRule> rules, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            var array = new JsonArray();
            foreach (var rule in rules)
            {
                array.Add(new JsonObject
                {
                    ["id"] = rule.Id,
                    ["title"] = rule.Title,
                    ["category"] = rule.Category,
                    ["defaultSeverity"] = rule.DefaultSeverity,
                    ["enabledByDefault"] = rule.EnabledByDefault,
                    ["hotPath"] = rule.HotPath,
                    ["condition"] = rule.Condition,
                    ["compilationWide"] = rule.CompilationWide,
                    ["helpUri"] = rule.HelpUri,
                });
            }

            var document = new JsonObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["toolVersion"] = AnalyzerCatalog.ToolVersion,
                ["rules"] = array,
            };

            stdout.WriteLine(document.ToJsonString(s_json));
            return;
        }

        foreach (var rule in rules)
        {
            var flags = new List<string>();
            if (!rule.EnabledByDefault) flags.Add("off by default");
            if (rule.HotPath) flags.Add("hot path");
            if (rule.Condition is object) flags.Add($"needs {rule.Condition}");
            if (rule.CompilationWide) flags.Add("whole assembly");

            var suffix = flags.Count == 0 ? string.Empty : $"  [{string.Join("; ", flags)}]";
            stdout.WriteLine($"{rule.Id}  {rule.DefaultSeverity,-7}  {rule.Title}{suffix}");
        }

        stdout.WriteLine();
        stdout.WriteLine($"{rules.Length} rules (version {AnalyzerCatalog.ToolVersion}).");
    }

    public static string Help() => new StringBuilder()
        .AppendLine("upa-cli - run the Unity performance analyzers without Unity.")
        .AppendLine()
        .AppendLine("Usage:")
        .AppendLine("  upa-cli <file...> [options]")
        .AppendLine("  upa-cli --list-rules [--format text|json]")
        .AppendLine("  upa-cli --version | --help")
        .AppendLine()
        .AppendLine("Input files accept * ? and ** patterns, expanded by this tool rather than the")
        .AppendLine("shell - quote them so they behave the same everywhere. A pattern matching nothing")
        .AppendLine("is an error.")
        .AppendLine()
        .AppendLine("Any argument may be supplied from a response file. A whole assembly's worth of")
        .AppendLine("references and defines does not fit on a Windows command line, which caps at")
        .AppendLine("32767 characters:")
        .AppendLine("  upa-cli @args.rsp")
        .AppendLine("One argument per line; blank lines and lines starting with # are ignored. Lines")
        .AppendLine("are taken literally - no quoting, so a path containing spaces is written plain.")
        .AppendLine("Arguments expand where the @file appears, so ordering rules are unchanged, and a")
        .AppendLine("response file cannot reference another one.")
        .AppendLine()
        .AppendLine("Options:")
        .AppendLine("  --reference <name|path> A name makes the package look present, which is all the")
        .AppendLine("                          package-conditional rules check. A path to a DLL loads the")
        .AppendLine("                          real assembly, which source calling into it needs in order")
        .AppendLine("                          to resolve. Repeatable, and the two forms can be mixed.")
        .AppendLine("                            --reference UniTask")
        .AppendLine("                            --reference Assets/Plugins/DOTween/DOTween.dll")
        .AppendLine("  --define <symbol>       Add a preprocessor symbol (repeatable).")
        .AppendLine("                            --define UPA_TARGET_WEBGL")
        .AppendLine("  --assembly-name <name>  Compilation assembly name (default: Assembly-CSharp). Rules that")
        .AppendLine("                          only apply to player code skip *.Editor assemblies.")
        .AppendLine("                            --assembly-name MyGame.Tools.Editor")
        .AppendLine("  --ruleset <path>        Apply severities from a .ruleset file.")
        .AppendLine("                            --ruleset Assets/Default.ruleset")
        .AppendLine("  --editorconfig <path>   Apply severities and upa_* analyzer options from an .editorconfig.")
        .AppendLine("                            --editorconfig .editorconfig")
        .AppendLine("  --additionalfile <path> Pass an additional file, such as the universal options file")
        .AppendLine("                          (repeatable).")
        .AppendLine("                            --additionalfile Assets/Rules.UnityPerformanceAnalyzers.additionalfile")
        .AppendLine("  --unity-dll-dir <dir>   Use a real Unity managed directory instead of the bundled stubs.")
        .AppendLine("                            --unity-dll-dir <UnityEditor>/Data/Managed/UnityEngine")
        .AppendLine("  --all-warn              Force every rule on at warning, overriding ruleset and")
        .AppendLine("                          editorconfig. Useful to see what the off-by-default rules say.")
        .AppendLine("  --whole-assembly        Treat the given files as a complete assembly: enables the")
        .AppendLine("                          whole-assembly rules, and makes compile errors fatal.")
        .AppendLine("  --baseline <path>       Suppress the violations recorded in a baseline file, so only")
        .AppendLine("                          new ones are reported. Paths inside it are relative to the")
        .AppendLine("                          baseline's own directory, so it works from anywhere.")
        .AppendLine("                            --baseline upa-baseline.json")
        .AppendLine("  --write-baseline <path> Record the current violations as the baseline. Requires")
        .AppendLine("                          --whole-assembly, and refuses when the analysis was")
        .AppendLine("                          incomplete or when the existing file covers files this run")
        .AppendLine("                          did not analyze. Exits 0 on success.")
        .AppendLine("                            --write-baseline upa-baseline.json --whole-assembly")
        .AppendLine("  --fail-on <level>       Threshold for exit code 1: none|info|warning|error.")
        .AppendLine("                          Default: warning. Use none to report without failing.")
        .AppendLine("  --format <format>       text|json. Default: text. JSON is the only thing on stdout.")
        .AppendLine("  --list-rules            Print the rule catalog of this build instead of analyzing.")
        .AppendLine("  --version               Print the tool version.")
        .AppendLine("  --help, -h              Print this help.")
        .AppendLine()
        .AppendLine("Examples:")
        .AppendLine("  upa-cli Assets/Scripts/Player.cs")
        .AppendLine("  upa-cli \"Assets/Scripts/**/*.cs\" --whole-assembly --format json --fail-on error")
        .AppendLine("  upa-cli Loader.cs --reference UniTask --define UPA_TARGET_WEBGL")
        .AppendLine("  upa-cli Tween.cs --reference Assets/Plugins/DOTween/DOTween.dll")
        .AppendLine("  upa-cli Player.cs --ruleset Assets/Default.ruleset --all-warn")
        .AppendLine("  upa-cli --list-rules --format json")
        .AppendLine()
        .AppendLine("Exit codes: 0 clean, 1 diagnostics at or above --fail-on, 2 usage or execution")
        .AppendLine("error - including an analyzer that failed to run, whatever --fail-on says.")
        .AppendLine()
        .AppendLine("Using this as a gate? Pass --whole-assembly with an assembly's complete source")
        .AppendLine("set: it enables the whole-assembly rules and turns a broken compilation into an")
        .AppendLine("error instead of a clean result that was never really verified.")
        .AppendLine()
        .AppendLine("What this tool approximates:")
        .AppendLine("  - The file list is not an assembly boundary, so rules that need the whole")
        .AppendLine("    assembly are skipped unless --whole-assembly is given.")
        .AppendLine("  - --reference <name> only makes the package look present; pass the DLL by path")
        .AppendLine("    when the code actually calls into it, or those types stay unresolved.")
        .AppendLine("  - Unresolved types weaken the analysis, so compile errors are only tolerated")
        .AppendLine("    without --whole-assembly; with it they exit 2 rather than report success.")
        .AppendLine("  - Unity's own compilation remains the final authority.")
        .ToString();

    private static int Count(AnalysisResult result, string severity) =>
        result.Diagnostics.Count(d => d.Severity == severity);

    private static void AppendCount(List<string> into, int count, string label)
    {
        if (count > 0)
        {
            into.Add($"{count} {label}{(count == 1 ? string.Empty : "s")}");
        }
    }

    private static JsonArray ToJsonArray(ImmutableArray<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
