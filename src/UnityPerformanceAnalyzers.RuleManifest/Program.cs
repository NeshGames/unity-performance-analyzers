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
/// Generates package/Editor/rules.json — the rule catalog the Rule Manager window reads.
/// The window must not load the analyzer assembly (that would pull Roslyn into the Unity
/// Editor domain), so this tool extracts the catalog at build time instead: rule rows come
/// from the analyzers' SupportedDiagnostics via a project reference, while the UNT groups,
/// per-rule conditions, hot-path flags and option metadata are curated here. Run by the
/// release workflow so the shipped file is always current; a locally stale copy is accepted.
/// </summary>
internal static class Program
{
    // Rule metadata (hot-path scope, activation condition) travels with the analyzers as
    // [HotPathRule]/[ConditionalRule] attributes and is read by reflection below, and the
    // option list comes from UpaOptionCatalog in the same assembly — there is no curated
    // table left to drift. UNT groups come from PresetTable, the same source the preset
    // files are generated from.

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--presets")
        {
            var files = PresetEmitter.WriteAll(args[1]);
            Console.WriteLine($"Wrote {files.Count} preset files under {Path.GetFullPath(args[1])}");
            return 0;
        }

        if (args.Length == 2 && args[0] == "--readme")
        {
            try
            {
                var files = ReadmeEmitter.WriteAll(args[1]);
                Console.WriteLine(files.Count == 0
                    ? "README rule tables already up to date"
                    : $"Rewrote {files.Count} README file(s)");
                return 0;
            }
            catch (InvalidOperationException failure)
            {
                Console.Error.WriteLine("::error::" + failure.Message);
                return 1;
            }
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine(
                "Usage: RuleManifest <output-path> | RuleManifest --presets <repo-root>"
                + " | RuleManifest --readme <repo-root>");
            return 1;
        }

        // The shape of rules.json is a contract with the Rule Manager window, so the catalog
        // is projected onto it rather than serialized directly: a field added to the catalog
        // for one tool's sake must not silently appear in the file another tool parses.
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

        var outputPath = Path.GetFullPath(args[0]);
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
