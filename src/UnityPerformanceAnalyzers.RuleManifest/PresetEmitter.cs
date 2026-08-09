using System.Text;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Writes every preset file from <see cref="PresetTable"/>: the four main presets in both
/// formats, the editor-relaxed pair, the webgl-addon pair, and the sandbox verification
/// ruleset. Output is deterministic (fixed ordering, LF line endings) so CI can regenerate
/// and fail on any drift with a plain git diff.
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

    /// <summary>Writes all generated files under the repo root; returns the paths written.</summary>
    public static IReadOnlyList<string> WriteAll(string repoRoot)
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
            Write(Path.Combine(presetDir, preset.Name + ".editorconfig"), MainEditorconfig(preset));
        }

        Write(Path.Combine(presetDir, "editor-relaxed.ruleset"), EditorRelaxedRuleset());
        Write(Path.Combine(presetDir, "editor-relaxed.editorconfig"), EditorRelaxedEditorconfig());
        Write(Path.Combine(presetDir, "webgl-addon.ruleset"), WebGlRuleset());
        Write(Path.Combine(presetDir, "webgl-addon.editorconfig"), WebGlEditorconfig());
        Write(sandboxRuleset, SandboxRuleset());
        return written;
    }

    private static string MainRuleset(Preset preset)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append($"<!-- unity-performance-analyzers preset: {preset.Name}\n");
        sb.Append("     Copy this file to Assets/Default.ruleset (project-wide).\n");
        sb.Append("     A Default.ruleset inside an asmdef folder overrides it for that assembly.\n");
        sb.Append("     To add the WebGL rules: <Include Path=\"webgl-addon.ruleset\" Action=\"Default\" />\n");
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

    private static string MainEditorconfig(Preset preset)
    {
        var sb = new StringBuilder();
        sb.Append($"# unity-performance-analyzers preset: {preset.Name} (IDE parity variant)\n");
        sb.Append("# Unity itself does not read .editorconfig (verified 2022.3/6000.5) - use the\n");
        sb.Append("# .ruleset of the same name for Unity builds. This file keeps Rider/VS in sync\n");
        sb.Append("# and carries the IDE-only hot-path options.\n");
        sb.Append(GeneratedNotice("# "));
        sb.Append("root = true\n\n[*.cs]\n");
        foreach (var row in PresetTable.UpaRows)
        {
            sb.Append($"dotnet_diagnostic.{row.Id}.severity = {PresetTable.ToEditorconfigSeverity(preset.Severity(row))}\n");
        }

        foreach (var id in PresetTable.UntCorrectness)
        {
            sb.Append($"dotnet_diagnostic.{id}.severity = error\n");
        }

        var untPerf = preset.Name == "minimal" ? "none" : preset.Name == "recommended" ? "warning" : "error";
        foreach (var id in PresetTable.UntPerformance)
        {
            sb.Append($"dotnet_diagnostic.{id}.severity = {untPerf}\n");
        }

        sb.Append('\n');
        sb.Append("# Hot-path classification (IDE only; Unity builds use the built-in defaults)\n");
        sb.Append("# upa_hot_path_messages = Update,FixedUpdate,LateUpdate\n");
        sb.Append("# upa_hot_path_attributes = HotPath,PerformanceCritical\n");
        sb.Append("# upa_hot_path_include_lambdas = true\n\n");
        sb.Append("# Rule options\n");
        sb.Append("# upa_enum_switch_allow_default = true\n");
        sb.Append("# upa_addrange_hot_path_only = false\n\n");
        sb.Append("# Editor tooling: relax performance pressure (IDE side; for Unity builds place\n");
        sb.Append("# editor-relaxed.ruleset as Default.ruleset in your Editor asmdef folders)\n");
        sb.Append("[**/Editor/**/*.cs]\n");
        sb.Append("dotnet_analyzer_diagnostic.category-Performance.severity = suggestion\n");
        sb.Append("dotnet_diagnostic.UPA0005.severity = none\n");
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

    private static string EditorRelaxedEditorconfig()
    {
        var sb = new StringBuilder();
        sb.Append("# unity-performance-analyzers: relaxed severities for Editor tooling (IDE parity variant).\n");
        sb.Append("# For Unity builds use editor-relaxed.ruleset in the Editor asmdef folder instead.\n");
        sb.Append("# Merge into the .editorconfig scope that covers your editor assemblies.\n");
        sb.Append(GeneratedNotice("# "));
        sb.Append("[*.cs]\n");
        foreach (var row in PresetTable.UpaRows)
        {
            var severity = PresetTable.IsEditorRelaxedException(row.Id)
                ? PresetTable.ToEditorconfigSeverity(row.Recommended)
                : "none";
            sb.Append($"dotnet_diagnostic.{row.Id}.severity = {severity}\n");
        }

        foreach (var id in PresetTable.WebGlRules)
        {
            sb.Append($"dotnet_diagnostic.{id}.severity = none\n");
        }

        foreach (var id in PresetTable.UntCorrectness)
        {
            sb.Append($"dotnet_diagnostic.{id}.severity = error\n");
        }

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

    private static string WebGlEditorconfig()
    {
        var sb = new StringBuilder();
        sb.Append("# unity-performance-analyzers add-on: WebGL unsupported-API rules (IDE parity variant).\n");
        sb.Append("# Append these lines to your project .editorconfig [*.cs] section.\n");
        sb.Append("# Unity builds use webgl-addon.ruleset instead; the rules need UPA_TARGET_WEBGL defined.\n");
        sb.Append(GeneratedNotice("# "));
        foreach (var id in PresetTable.WebGlRules)
        {
            sb.Append($"dotnet_diagnostic.{id}.severity = warning\n");
        }

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
        var line2 = prefix + "regenerate via the RuleManifest presets mode (see that file's header).";
        var closing = prefix.TrimEnd().StartsWith("#") ? "\n" : " -->\n";
        return line1 + "\n" + line2 + closing;
    }
}
