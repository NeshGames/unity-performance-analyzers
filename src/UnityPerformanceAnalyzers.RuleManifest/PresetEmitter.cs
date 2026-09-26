using System.Text;

using UnityPerformanceAnalyzers;
using UnityPerformanceAnalyzers.Catalog;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Writes every preset ruleset from <see cref="PresetTable"/>: the four main presets, the
/// editor-relaxed and webgl-addon rulesets, the coexistence overlays, and the sandbox
/// verification ruleset. Output is deterministic (fixed ordering, LF line endings) so CI can
/// regenerate and fail on any drift, including a file that should no longer exist.
/// </summary>
public static class PresetEmitter
{
    private sealed record Preset(string Name, Func<PresetTable.Row, string> Severity);

    private static readonly Preset[] s_mainPresets =
    {
        new("minimal", r => r.Minimal),
        new("recommended", r => r.Recommended),
        new("strict", r => r.Strict),
        new("cysharp-stack", r => r.Cysharp),
    };

    /// <summary>
    /// The phrase every generated preset carries in its notice, and what marks a file in the
    /// preset directory as this generator's to delete.
    /// </summary>
    public const string OwnershipMarker = "regenerate via the RuleManifest presets mode";

    /// <summary>Writes all generated files under the repo root; returns the paths written.</summary>
    public static IReadOnlyList<string> WriteAll(string repoRoot) => WriteAll(repoRoot, out _);

    /// <summary>
    /// Writes all generated files under the repo root and deletes the generated presets it no
    /// longer produces; returns the paths written and, through <paramref name="removed"/>, the
    /// paths deleted.
    /// </summary>
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

        Write(Path.Combine(presetDir, "editor-relaxed.ruleset"), EditorRelaxedRuleset());
        Write(Path.Combine(presetDir, "webgl-addon.ruleset"), WebGlRuleset());
        Write(sandboxRuleset, SandboxRuleset());
        written.AddRange(CoexistEmitter.Write(presetDir));
        removed = RemoveStale(presetDir, written);
        return written;
    }

    /// <summary>
    /// Deletes the generated presets in <paramref name="directory"/> that this run did not
    /// write. Without it a preset dropped from the table stayed in the package, still carrying
    /// its "generated" notice, and a drift check that regenerated and compared saw nothing
    /// wrong: the file it should have flagged was one the generator never looked at again.
    /// </summary>
    /// <remarks>
    /// Only files carrying this generator's notice are touched. The directory also holds the
    /// hand-written READMEs, and anything without the notice is somebody's to delete, not ours.
    /// </remarks>
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
        sb.Append($"<!-- unity-performance-analyzers preset: {preset.Name}\n");
        sb.Append("     Copy this file to Assets/Default.ruleset (project-wide).\n");
        sb.Append("     A Default.ruleset inside an asmdef folder overrides it for that assembly.\n");
        sb.Append("     To add the WebGL rules: <Include Path=\"webgl-addon.ruleset\" Action=\"Default\" />\n");
        if (PresetTable.UpaRows.Any(row => preset.Severity(row) == "error"))
        {
            // Stated in the file because the file is what gets copied. An Error entry fails
            // Unity's compile, and an Editor launched on a project that does not compile opens
            // in Safe Mode, where the Pipeline package the Unity CLI talks to does not load.
            // No double hyphen may appear in an XML comment, so the CLI flags are spelled out.
            sb.Append("\n");
            sb.Append("     Error entries fail Unity's compile. An Editor launched on a project that\n");
            sb.Append("     does not compile opens in Safe Mode, where the Unity CLI cannot reach it.\n");
            sb.Append("     To gate at this level without that, keep recommended in Assets and pass\n");
            sb.Append("     this file to upa-cli in CI as its ruleset, failing on error.\n");
        }

        sb.Append(GeneratedNotice("     "));
        sb.Append($"<RuleSet Name=\"UPA {preset.Name}\" ToolsVersion=\"10.0\">\n");
        sb.Append("  <Rules AnalyzerId=\"UnityPerformanceAnalyzers\" RuleNamespace=\"UnityPerformanceAnalyzers\">\n");
        foreach (var row in PresetTable.UpaRows)
        {
            sb.Append(RuleLine(row.Id, preset.Severity(row)));
        }

        sb.Append("  </Rules>\n");
        sb.Append(UntRulesBlock(preset.Name == "minimal" ? "none" : preset.Name == "recommended" ? "warning" : "error"));
        sb.Append("</RuleSet>\n");
        return sb.ToString();
    }

    private static string EditorRelaxedRuleset()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<!-- unity-performance-analyzers: relaxed ruleset for Editor tooling assemblies.\n");
        sb.Append("     Rulesets have no path scoping, so editor code gets its own file instead:\n");
        sb.Append("     copy this into each Editor asmdef folder and rename it Default.ruleset -\n");
        sb.Append("     it then overrides the project-wide Assets/Default.ruleset for that assembly.\n\n");
        sb.Append("     Performance pressure is irrelevant in editor tooling, so UPA performance\n");
        sb.Append("     and ecosystem rules are off; UNT correctness rules stay at Error. Rules\n");
        sb.Append("     about how a type is declared keep their severity - being in editor code\n");
        sb.Append("     does not make a struct any better as a dictionary key.\n");
        sb.Append(GeneratedNotice("     "));
        sb.Append("<RuleSet Name=\"UPA editor-relaxed\" ToolsVersion=\"10.0\">\n");
        sb.Append("  <Rules AnalyzerId=\"UnityPerformanceAnalyzers\" RuleNamespace=\"UnityPerformanceAnalyzers\">\n");
        foreach (var row in PresetTable.UpaRows)
        {
            var action = PresetTable.IsEditorRelaxedException(row.Id) ? row.Recommended : "none";
            sb.Append(RuleLine(row.Id, action));
        }

        foreach (var id in PresetTable.WebGlRules)
        {
            sb.Append(RuleLine(id, "none"));
        }

        sb.Append("  </Rules>\n");
        sb.Append("  <Rules AnalyzerId=\"Microsoft.Unity.Analyzers\" RuleNamespace=\"Microsoft.Unity.Analyzers\">\n");
        foreach (var id in PresetTable.UntCorrectness)
        {
            sb.Append(RuleLine(id, "error"));
        }

        sb.Append("  </Rules>\n");
        sb.Append("</RuleSet>\n");
        return sb.ToString();
    }

    private static string WebGlRuleset()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<!-- unity-performance-analyzers add-on: WebGL unsupported-API rules.\n");
        sb.Append("     Stack this on top of any base preset by adding, inside your Assets/Default.ruleset:\n");
        sb.Append("       <Include Path=\"webgl-addon.ruleset\" Action=\"Default\" />\n");
        sb.Append("     (place this file next to it; the path is relative to the including ruleset)\n\n");
        sb.Append("     These rules only run when the compilation defines UPA_TARGET_WEBGL - add it in\n");
        sb.Append("     Project Settings > Player > Scripting Define Symbols for every build target so the\n");
        sb.Append("     rules stay active during day-to-day (non-WebGL) development too.\n");
        sb.Append(GeneratedNotice("     "));
        sb.Append("<RuleSet Name=\"UPA webgl-addon\" ToolsVersion=\"10.0\">\n");
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
        sb.Append("<!-- Sandbox verification profile: the recommended preset with several rules that\n");
        sb.Append("     default to Info or off forced to Warning, so every hit is visible in the\n");
        sb.Append("     compiler output during sandbox verification runs.\n");
        sb.Append(GeneratedNotice("     "));
        sb.Append("<RuleSet Name=\"UPA sandbox-verification\" ToolsVersion=\"10.0\">\n");
        sb.Append("  <Rules AnalyzerId=\"UnityPerformanceAnalyzers\" RuleNamespace=\"UnityPerformanceAnalyzers\">\n");
        foreach (var row in PresetTable.UpaRows)
        {
            var severity = PresetTable.SandboxOverrides.TryGetValue(row.Id, out var forced)
                ? forced
                : row.Recommended;
            sb.Append(RuleLine(row.Id, severity));
        }

        sb.Append("  </Rules>\n");
        sb.Append(UntRulesBlock("warning"));
        sb.Append("  <Include Path=\"webgl-addon.ruleset\" Action=\"Default\" />\n");
        sb.Append("</RuleSet>\n");
        return sb.ToString();
    }

    private static string UntRulesBlock(string performanceSeverity)
    {
        var sb = new StringBuilder();
        sb.Append("  <Rules AnalyzerId=\"Microsoft.Unity.Analyzers\" RuleNamespace=\"Microsoft.Unity.Analyzers\">\n");
        foreach (var id in PresetTable.UntCorrectness)
        {
            sb.Append(RuleLine(id, "error"));
        }

        foreach (var id in PresetTable.UntPerformance)
        {
            sb.Append(RuleLine(id, performanceSeverity));
        }

        sb.Append("  </Rules>\n");
        return sb.ToString();
    }

    private static string RuleLine(string id, string severity) =>
        $"    <Rule Id=\"{id}\" Action=\"{PresetTable.ToRulesetAction(severity)}\" />\n";

    // XML comments must not contain "--", so the notice spells the regen command without
    // literal option syntax (the ruleset file is rejected wholesale otherwise, CS8035).
    private static string GeneratedNotice(string prefix)
    {
        var line1 = prefix + "GENERATED FILE - do not edit. Severities live in PresetTable.cs;";
        var line2 = prefix + OwnershipMarker + " (see that file's header).";
        var closing = prefix.TrimEnd().StartsWith("#") ? "\n" : " -->\n";
        return line1 + "\n" + line2 + closing;
    }
}
