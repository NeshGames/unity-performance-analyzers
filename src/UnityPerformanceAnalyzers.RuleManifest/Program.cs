using System.Collections.Immutable;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.Diagnostics;

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
    // Both curated tables below must track the rule set: entries here for IDs the assembly
    // does not export fail the run (see the check in Main), and new rules must be added
    // when they land.

    /// <summary>Rules registered only when the named dependency is present.</summary>
    private static readonly Dictionary<string, string> s_conditions = new()
    {
        ["UPA2010"] = "UniTask",
        ["UPA2011"] = "UniTask",
        ["UPA2021"] = "R3",
        ["UPA3000"] = "WebGL",
        ["UPA3001"] = "WebGL",
        ["UPA3002"] = "WebGL",
        ["UPA3003"] = "WebGL",
        ["UPA3004"] = "WebGL",
    };

    /// <summary>Rules that report on per-frame hot paths only.</summary>
    private static readonly HashSet<string> s_hotPathRules = new()
    {
        "UPA0001", "UPA0002", "UPA0004", "UPA0006", "UPA0007",
        "UPA0009", "UPA0012", "UPA0013", "UPA2000",
    };

    /// <summary>Microsoft.Unity.Analyzers rules the presets manage alongside UPA rules.</summary>
    private static readonly string[] s_untCorrectness =
    {
        "UNT0006", "UNT0007", "UNT0008", "UNT0010", "UNT0011",
        "UNT0015", "UNT0023", "UNT0029", "UNT0030", "UNT0033", "UNT0043",
    };

    private static readonly string[] s_untPerformance =
    {
        "UNT0001", "UNT0002", "UNT0017", "UNT0018", "UNT0019", "UNT0022", "UNT0024",
        "UNT0026", "UNT0028", "UNT0032", "UNT0036", "UNT0037", "UNT0041", "UNT0042",
    };

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
    };

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: RuleManifest <output-path>");
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
                    s_hotPathRules.Contains(descriptor.Id),
                    s_conditions.TryGetValue(descriptor.Id, out var condition) ? condition : null,
                    descriptor.HelpLinkUri);
            }
        }

        var unknownConditionIds = s_conditions.Keys.Concat(s_hotPathRules)
            .Where(id => !rules.ContainsKey(id))
            .Distinct()
            .ToList();
        if (unknownConditionIds.Count > 0)
        {
            // Curated tables referencing IDs the assembly no longer exports means this tool
            // is out of date with the rule set — fail loudly rather than ship a wrong catalog.
            Console.Error.WriteLine($"Curated metadata references unknown rule IDs: {string.Join(", ", unknownConditionIds)}");
            return 1;
        }

        var version = analyzerAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? analyzerAssembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        var manifest = new Manifest(
            version,
            rules.Values.ToArray(),
            new UntGroups(s_untCorrectness, s_untPerformance),
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
