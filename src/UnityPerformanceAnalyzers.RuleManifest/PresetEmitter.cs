using System.Text;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Writes the two agent-oriented base profiles, the WebGL overlay, the transitional UniTask
/// coexist overlay, and the sandbox verification ruleset.
/// </summary>
public static class PresetEmitter
{
    private sealed record Preset(
        string Name,
        Func<PresetTable.Row, string> Severity,
        string UntSeverity,
        string Guidance);

    private static readonly Preset[] s_mainPresets =
    {
        new(
            "unity",
            r => r.Unity,
            "warning",
            "Safe for Assets/Default.ruleset in agent-driven Unity work; it contains no Error entries."),
        new(
            "ci",
            r => r.Ci,
            "error",
            "CI gate profile. Do not use it as Assets/Default.ruleset when an agent needs a live Editor; Error entries can force Safe Mode."),
    };

    public const string OwnershipMarker = "regenerate via the RuleManifest presets mode";

    public static IReadOnlyList<string> WriteAll(string repoRoot) => WriteAll(repoRoot, out _);

    public static IReadOnlyList<string> WriteAll(string repoRoot, out IReadOnlyList<string> removed)
    {
        var presetDir = Path.Combine(repoRoot, "package", "Samples~", "Ruleset Presets");
        var sandboxRuleset = Path.Combine(repoRoot, "sandbox", "UnityProject", "Assets", "Default.ruleset");
        Directory.CreateDirectory(presetDir);
        Directory.CreateDirectory(Path.GetDirectoryName(sandboxRuleset)!);

        var written = new List<string>();
        void Write(string path, string content)
        {
            File.WriteAllText(path, content);
            written.Add(path);
        }

        foreach (var preset in s_mainPresets)
        {
            Write(Path.Combine(presetDir, preset.Name + ".ruleset"), MainRuleset(preset));
        }

        Write(Path.Combine(presetDir, "webgl.ruleset"), WebGlRuleset());
        Write(sandboxRuleset, SandboxRuleset());
        written.AddRange(CoexistEmitter.Write(presetDir));
        removed = RemoveStale(presetDir, written);
        return written;
    }

    public static IReadOnlyList<string> RemoveStale(string directory, IEnumerable<string> keep)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var kept = new HashSet<string>(keep.Select(Path.GetFullPath), comparer);
        var removed = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (kept.Contains(Path.GetFullPath(file)) || !IsGenerated(file))
            {
                continue;
            }

            File.Delete(file);
            removed.Add(file);
        }

        return removed;
    }

    private static bool IsGenerated(string file)
    {
        var text = File.ReadAllText(file);
        return text.Contains("GENERATED FILE - do not edit.", StringComparison.Ordinal)
            && text.Contains(OwnershipMarker, StringComparison.Ordinal);
    }

    private static string MainRuleset(Preset preset)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append($"<!-- unity-performance-analyzers profile: {preset.Name}\n");
        sb.Append("     ").Append(preset.Guidance).Append("\n");
        sb.Append("     Add WebGL rules with <Include Path=\"webgl.ruleset\" Action=\"Default\" /> when needed.\n");
        sb.Append(GeneratedNotice("     "));
        sb.Append($"<RuleSet Name=\"UPA {preset.Name}\" ToolsVersion=\"10.0\">\n");
        sb.Append("  <Rules AnalyzerId=\"UnityPerformanceAnalyzers\" RuleNamespace=\"UnityPerformanceAnalyzers\">\n");
        foreach (var row in PresetTable.UpaRows)
        {
            sb.Append(RuleLine(row.Id, preset.Severity(row)));
        }

        sb.Append("  </Rules>\n");
        sb.Append(UntRulesBlock(preset.UntSeverity));
        sb.Append("</RuleSet>\n");
        return sb.ToString();
    }

    private static string WebGlRuleset()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<!-- unity-performance-analyzers WebGL overlay.\n");
        sb.Append("     Include next to unity.ruleset or ci.ruleset and define UPA_TARGET_WEBGL.\n");
        sb.Append("     Rules stay Warning here; CI chooses whether warnings fail through the upa-cli fail threshold.\n");
        sb.Append(GeneratedNotice("     "));
        sb.Append("<RuleSet Name=\"UPA webgl\" ToolsVersion=\"10.0\">\n");
        sb.Append("  <Rules AnalyzerId=\"UnityPerformanceAnalyzers\" RuleNamespace=\"UnityPerformanceAnalyzers\">\n");
        foreach (var id in PresetTable.WebGlRules)
        {
            sb.Append(RuleLine(id, "warning"));
        }

        sb.Append("  </Rules>\n");
        sb.Append("</RuleSet>\n");
        return sb.ToString();
    }

    private static string SandboxRuleset()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<!-- Sandbox verification profile: unity.ruleset plus selected off/Info rules forced to Warning.\n");
        sb.Append(GeneratedNotice("     "));
        sb.Append("<RuleSet Name=\"UPA sandbox-verification\" ToolsVersion=\"10.0\">\n");
        sb.Append("  <Rules AnalyzerId=\"UnityPerformanceAnalyzers\" RuleNamespace=\"UnityPerformanceAnalyzers\">\n");
        foreach (var row in PresetTable.UpaRows)
        {
            var severity = PresetTable.SandboxOverrides.TryGetValue(row.Id, out var forced)
                ? forced
                : row.Unity;
            sb.Append(RuleLine(row.Id, severity));
        }

        sb.Append("  </Rules>\n");
        sb.Append(UntRulesBlock("warning"));
        sb.Append("  <Include Path=\"webgl.ruleset\" Action=\"Default\" />\n");
        sb.Append("</RuleSet>\n");
        return sb.ToString();
    }

    private static string UntRulesBlock(string severity)
    {
        var sb = new StringBuilder();
        sb.Append("  <Rules AnalyzerId=\"Microsoft.Unity.Analyzers\" RuleNamespace=\"Microsoft.Unity.Analyzers\">\n");
        foreach (var id in PresetTable.UntCorrectness)
        {
            sb.Append(RuleLine(id, severity));
        }

        foreach (var id in PresetTable.UntPerformance)
        {
            sb.Append(RuleLine(id, severity));
        }

        sb.Append("  </Rules>\n");
        return sb.ToString();
    }

    private static string RuleLine(string id, string severity) =>
        $"    <Rule Id=\"{id}\" Action=\"{PresetTable.ToRulesetAction(severity)}\" />\n";

    private static string GeneratedNotice(string prefix)
    {
        var line1 = prefix + "GENERATED FILE - do not edit. Severities live in PresetTable.cs;";
        var line2 = prefix + OwnershipMarker + " (see that file's header).";
        var closing = prefix.TrimEnd().StartsWith("#", StringComparison.Ordinal) ? "\n" : " -->\n";
        return line1 + "\n" + line2 + closing;
    }
}
