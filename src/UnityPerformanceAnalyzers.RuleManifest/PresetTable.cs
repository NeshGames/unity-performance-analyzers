namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// The single source of truth for preset severities. Every file under
/// package/Samples~/Ruleset Presets/ and the sandbox Default.ruleset is generated from
/// this table by <see cref="PresetEmitter"/> — edit here, then regenerate with
/// <c>RuleManifest --presets &lt;repo root&gt;</c>; never edit the generated files.
/// Canonical severity values: none / info / warning / error (ruleset Actions capitalize,
/// .editorconfig writes info as suggestion).
/// </summary>
public static class PresetTable
{
    /// <summary>One rule's severity across the four main presets.</summary>
    public sealed record Row(string Id, string Minimal, string Recommended, string Strict, string Cysharp);

    /// <summary>
    /// The four-preset matrix. Deliberate absences: UPA1001 stays at its default Warning
    /// in every preset; UPA3000-3004 live only in the webgl-addon (an explicit entry in a
    /// base preset would override the addon's Include) and in editor-relaxed.
    /// </summary>
    public static readonly Row[] UpaRows =
    {
        new("UPA0001", "none", "warning", "error", "error"),
        new("UPA0002", "none", "warning", "error", "error"),
        new("UPA0003", "none", "warning", "error", "error"),
        new("UPA0004", "none", "warning", "error", "error"),
        new("UPA0005", "none", "none", "warning", "error"),
        new("UPA0006", "none", "warning", "error", "error"),
        new("UPA0007", "none", "warning", "error", "error"),
        new("UPA0008", "none", "warning", "error", "error"),
        new("UPA0009", "none", "warning", "error", "error"),
        new("UPA0010", "none", "warning", "error", "error"),
        new("UPA0011", "none", "none", "warning", "error"),
        new("UPA0012", "none", "none", "warning", "error"),
        new("UPA0013", "none", "none", "none", "error"),
        new("UPA0014", "none", "warning", "error", "error"),
        new("UPA0015", "none", "info", "warning", "warning"),
        new("UPA0016", "none", "warning", "error", "error"),
        new("UPA0017", "none", "warning", "error", "error"),
        new("UPA0018", "none", "warning", "error", "error"),
        new("UPA0019", "none", "warning", "error", "error"),
        new("UPA0020", "none", "none", "warning", "error"),
        new("UPA0021", "none", "warning", "error", "error"),
        // Deprecated: HasFlag does not box on any supported runtime and the
        // bitwise rewrite measures slower on IL2CPP. Held at none everywhere rather than
        // dropped from the table, so a preset actively silences it instead of leaving it
        // to whatever the descriptor happens to say.
        new("UPA0022", "none", "none", "none", "none"),
        new("UPA0023", "none", "none", "info", "warning"),
        new("UPA0024", "none", "none", "warning", "error"),
        new("UPA0025", "none", "warning", "error", "error"),
        new("UPA0026", "none", "warning", "error", "error"),
        new("UPA0027", "none", "warning", "error", "error"),
        new("UPA0028", "none", "warning", "error", "error"),
        // Deliberately capped at warning: an optimization hint, not a correctness problem,
        // and the gain is negligible on small collections.
        new("UPA0029", "none", "warning", "warning", "warning"),
        new("UPA0030", "none", "warning", "error", "error"),
        new("UPA0031", "none", "warning", "error", "error"),
        // Deprecated: sealing a leaf class measured 2.70 ns against 3.00 ns
        // unsealed on IL2CPP, a difference the spread of the same eight runs swallows. Held
        // at none for the reason UPA0022 is: a preset that silences it says so, where a
        // dropped row leaves the answer to whatever the descriptor happens to say.
        new("UPA1000", "none", "none", "none", "none"),
        // recommended is deliberately not "none": the rule was made unconditional so that
        // projects without ZString would still hear about hot-path string building, and
        // leaving it off in the everyday preset put that motivation right back where it
        // started.
        new("UPA2000", "none", "warning", "error", "error"),
        new("UPA2010", "none", "none", "none", "error"),
        new("UPA2011", "none", "none", "none", "error"),
        new("UPA2012", "none", "none", "none", "error"),
        new("UPA2021", "none", "none", "none", "warning"),
        new("UPA2030", "none", "warning", "error", "error"),
        new("UPA2031", "none", "warning", "error", "error"),
        new("UPA2032", "none", "none", "info", "info"),
    };

    /// <summary>
    /// Rules that keep their severity in the editor-relaxed variants. Relaxation exists
    /// because per-frame cost does not matter in editor tooling — a rule that is not about
    /// per-frame cost has no reason to be switched off there. UPA0028 is about how a type is
    /// declared, and a struct used as a dictionary key is just as wrong in an editor window.
    /// </summary>
    public static readonly string[] EditorRelaxedExceptions = { "UPA0028" };

    /// <summary>True when the rule keeps its severity in the editor-relaxed variants.</summary>
    public static bool IsEditorRelaxedException(string id) =>
        Array.IndexOf(EditorRelaxedExceptions, id) >= 0;

    /// <summary>Microsoft.Unity.Analyzers correctness rules: error in every preset.</summary>
    public static readonly string[] UntCorrectness =
    {
        "UNT0006", "UNT0007", "UNT0008", "UNT0010", "UNT0011",
        "UNT0015", "UNT0023", "UNT0029", "UNT0030", "UNT0033", "UNT0043",
    };

    /// <summary>Microsoft.Unity.Analyzers performance rules: none/warning/error/error.</summary>
    public static readonly string[] UntPerformance =
    {
        "UNT0001", "UNT0002", "UNT0017", "UNT0018", "UNT0019", "UNT0022", "UNT0024",
        "UNT0026", "UNT0028", "UNT0032", "UNT0036", "UNT0037", "UNT0041", "UNT0042",
    };

    /// <summary>WebGL rules, present only in the webgl-addon files and editor-relaxed.</summary>
    public static readonly string[] WebGlRules =
    {
        "UPA3000", "UPA3001", "UPA3002", "UPA3003", "UPA3004",
    };

    /// <summary>
    /// Sandbox verification profile = recommended + these overrides (several rules that
    /// default to Info or off are forced to Warning so every hit is visible in the
    /// compiler output during sandbox verification).
    /// </summary>
    public static readonly Dictionary<string, string> SandboxOverrides = new()
    {
        // Off in every preset, and the sandbox is where it finally has something to report:
        // TextMeshPro is in the measurement project as of the A4 measurement, so the rule
        // registers there for the first time.
        ["UPA0012"] = "warning",
        ["UPA0015"] = "warning",
        ["UPA0020"] = "warning",
        ["UPA0023"] = "warning",
        ["UPA0024"] = "warning",
        ["UPA2032"] = "warning",
    };

    /// <summary>Canonical severity → ruleset Action attribute value.</summary>
    public static string ToRulesetAction(string severity) => severity switch
    {
        "none" => "None",
        "info" => "Info",
        "warning" => "Warning",
        "error" => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "unknown canonical severity"),
    };

    /// <summary>Canonical severity → .editorconfig severity value (info becomes suggestion).</summary>
    public static string ToEditorconfigSeverity(string severity) => severity switch
    {
        "none" => "none",
        "info" => "suggestion",
        "warning" => "warning",
        "error" => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "unknown canonical severity"),
    };
}
