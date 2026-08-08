using System.Collections.Immutable;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.Diagnostics;
using UnityPerformanceAnalyzers;

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
    // [HotPathRule]/[ConditionalRule] attributes and is read by reflection below — there
    // is no curated ID table left to drift. UNT groups come from PresetTable, the same
    // source the preset files are generated from.

    private static readonly OptionRow[] s_options =
    {
        new(
            "upa_hot_path_messages",
            "list",
            "Update,FixedUpdate,LateUpdate,OnGUI,OnAnimatorMove,OnAnimatorIK,OnPreCull," +
            "OnPreRender,OnPostRender,OnRenderObject,OnWillRenderObject,OnRenderImage," +
            "OnTriggerStay,OnTriggerStay2D,OnCollisionStay,OnCollisionStay2D,OnParticleUpdateJobScheduled",
            "Unity messages treated as per-frame hot paths. Replaces the default set."),
        new(
            "upa_hot_path_attributes",
            "list",
            "HotPath,PerformanceCritical",
            "Attribute short names that mark any method as a hot path ('Attribute' suffix optional)."),
        new(
            "upa_hot_path_include_lambdas",
            "bool",
            "true",
            "Treat lambdas and local functions declared inside a hot-path method as hot."),
        new(
            "upa_enum_switch_allow_default",
            "bool",
            "true",
            "For UPA1001, a default branch (or discard arm) counts as exhaustive."),
        new(
            "upa_addrange_hot_path_only",
            "bool",
            "false",
            "Narrow UPA0029 to per-frame code instead of reporting copy loops anywhere."),
    };

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--presets")
        {
            var files = PresetEmitter.WriteAll(args[1]);
            Console.WriteLine($"Wrote {files.Count} preset files under {Path.GetFullPath(args[1])}");
            return 0;
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: RuleManifest <output-path> | RuleManifest --presets <repo-root>");
            return 1;
        }

        var analyzerAssembly = typeof(UPA1001NonExhaustiveEnumSwitchAnalyzer).Assembly;
        var rules = new SortedDictionary<string, RuleRow>(StringComparer.Ordinal);

        foreach (var type in analyzerAssembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            {
                continue;
            }

            var isHotPath = type.GetCustomAttribute<HotPathRuleAttribute>() is not null;
            var condition = type.GetCustomAttribute<ConditionalRuleAttribute>()?.Condition;

            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                // UPA2012 lists two descriptors under one ID; first one wins for catalog data.
                if (rules.ContainsKey(descriptor.Id))
                {
                    continue;
                }

                rules[descriptor.Id] = new RuleRow(
                    descriptor.Id,
                    descriptor.Title.ToString(),
                    descriptor.Category,
                    descriptor.DefaultSeverity.ToString(),
                    descriptor.IsEnabledByDefault,
                    isHotPath,
                    condition,
                    descriptor.HelpLinkUri);
            }
        }

        var version = analyzerAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? analyzerAssembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        var manifest = new Manifest(
            version,
            rules.Values.ToArray(),
            new UntGroups(PresetTable.UntCorrectness, PresetTable.UntPerformance),
            s_options);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        });

        var outputPath = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json + "\n");
        Console.WriteLine($"Wrote {rules.Count} rules to {outputPath} (version {version})");
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
