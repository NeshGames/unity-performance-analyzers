using System.Collections.Immutable;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.Diagnostics;
using UnityPerformanceAnalyzers;
using UnityPerformanceAnalyzers.Catalog;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Generates package/Editor/rules.json and the ruleset presets. The Rule Manager must not load
/// the analyzer assembly into the Editor domain, so its catalog is extracted at build time.
/// The root README is intentionally not generated: it is an AI-oriented repository index rather
/// than a second rule database.
/// </summary>
internal static class Program
{
    private const string Usage =
        "Usage: RuleManifest --all <repo-root> | RuleManifest <output-path>"
        + " | RuleManifest --presets <repo-root>";

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--all")
        {
            var root = args[1];
            return WritePresets(root) == 0
                && WriteCatalog(Path.Combine(root, "package", "Editor", "rules.json")) == 0
                ? 0
                : 1;
        }

        if (args.Length == 2 && args[0] == "--presets")
        {
            return WritePresets(args[1]);
        }

        if (args.Length != 1 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        return WriteCatalog(args[0]);
    }

    private static int WritePresets(string repoRoot)
    {
        var files = PresetEmitter.WriteAll(repoRoot, out var removed);
        Console.WriteLine($"Wrote {files.Count} preset files under {Path.GetFullPath(repoRoot)}");
        foreach (var file in removed)
        {
            Console.WriteLine($"Removed {file}: generated, and no longer produced");
        }

        return 0;
    }

    private static int WriteCatalog(string path)
    {
        var rules = UpaRuleCatalog.Rules()
            .Select(rule => new RuleRow(
                rule.Id,
                rule.Title,
                rule.Category,
                rule.DefaultSeverity,
                rule.EnabledByDefault,
                rule.HotPath,
                rule.Condition,
                rule.HelpUri))
            .ToArray();

        var version = UpaRuleCatalog.Version;

        var manifest = new Manifest(
            version,
            rules,
            new UntGroups(PresetTable.UntCorrectness, PresetTable.UntPerformance),
            UpaOptionCatalog.Options
                .Select(option => new OptionRow(
                    option.Key,
                    option.Kind.ToString().ToLowerInvariant(),
                    option.Default,
                    option.Description))
                .ToArray());

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        });

        var outputPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json + "\n");
        Console.WriteLine($"Wrote {rules.Length} rules to {outputPath} (version {version})");
        return 0;
    }

    private sealed record Manifest(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("upa")] RuleRow[] Upa,
        [property: JsonPropertyName("unt")] UntGroups Unt,
        [property: JsonPropertyName("options")] OptionRow[] Options);

    private sealed record RuleRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("defaultSeverity")] string DefaultSeverity,
        [property: JsonPropertyName("enabledByDefault")] bool EnabledByDefault,
        [property: JsonPropertyName("hotPath")] bool HotPath,
        [property: JsonPropertyName("condition")] string? Condition,
        [property: JsonPropertyName("helpUri")] string HelpUri);

    private sealed record UntGroups(
        [property: JsonPropertyName("correctness")] string[] Correctness,
        [property: JsonPropertyName("performance")] string[] Performance);

    private sealed record OptionRow(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("default")] string Default,
        [property: JsonPropertyName("description")] string Description);
}
