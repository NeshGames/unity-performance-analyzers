namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Single source of truth for the agent-oriented severity profiles. Consumer configuration has
/// three concepts only: Unity, CI and the WebGL overlay.
/// </summary>
public static class PresetTable
{
    public sealed record Row(string Id, string Unity, string Ci);

    public static readonly Row[] UpaRows =
    {
        new("UPA0001", "warning", "error"),
        new("UPA0002", "warning", "error"),
        new("UPA0003", "warning", "error"),
        new("UPA0004", "warning", "error"),
        new("UPA0005", "none", "error"),
        new("UPA0006", "warning", "error"),
        new("UPA0007", "warning", "error"),
        new("UPA0008", "warning", "error"),
        new("UPA0010", "warning", "error"),
        new("UPA0011", "none", "error"),
        new("UPA0012", "none", "error"),
        new("UPA0013", "none", "error"),
        new("UPA0014", "warning", "error"),
        new("UPA0015", "info", "warning"),
        new("UPA0016", "warning", "error"),
        new("UPA0017", "warning", "error"),
        new("UPA0018", "warning", "error"),
        new("UPA0019", "warning", "error"),
        new("UPA0020", "none", "error"),
        new("UPA0023", "none", "warning"),
        new("UPA0024", "none", "error"),
        new("UPA0025", "warning", "error"),
        new("UPA0026", "warning", "error"),
        new("UPA0027", "warning", "error"),
        new("UPA0028", "warning", "error"),
        new("UPA0029", "warning", "warning"),
        new("UPA0030", "warning", "error"),
        new("UPA0031", "info", "info"),
        new("UPA2000", "warning", "error"),
        new("UPA2010", "none", "error"),
        new("UPA2011", "none", "error"),
        new("UPA2012", "none", "error"),
        new("UPA2021", "none", "warning"),
        new("UPA2030", "warning", "error"),
        new("UPA2031", "warning", "error"),
        new("UPA2032", "none", "info"),
    };

    public static readonly string[] UntCorrectness =
    {
        "UNT0006", "UNT0007", "UNT0008", "UNT0010", "UNT0011", "UNT0015", "UNT0023", "UNT0029", "UNT0030", "UNT0033", "UNT0043",
    };

    public static readonly string[] UntPerformance =
    {
        "UNT0001", "UNT0002", "UNT0017", "UNT0018", "UNT0019", "UNT0022", "UNT0024", "UNT0026", "UNT0028", "UNT0032", "UNT0036", "UNT0037", "UNT0041", "UNT0042",
    };

    public static readonly string[] WebGlRules =
    {
        "UPA3000", "UPA3001", "UPA3002", "UPA3003", "UPA3004",
    };

    public static readonly Dictionary<string, string> SandboxOverrides = new()
    {
        ["UPA0012"] = "warning",
        ["UPA0015"] = "warning",
        ["UPA0020"] = "warning",
        ["UPA0023"] = "warning",
        ["UPA0024"] = "warning",
        ["UPA2032"] = "warning",
    };

    public static string ToRulesetAction(string severity) => severity switch
    {
        "none" => "None",
        "info" => "Info",
        "warning" => "Warning",
        "error" => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "unknown canonical severity"),
    };
}
