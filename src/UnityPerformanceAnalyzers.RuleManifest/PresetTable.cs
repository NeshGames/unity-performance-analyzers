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
        // Held at none everywhere until an IL2CPP measurement exists. Grading it in a preset
        // would put the unmeasured claim back in front of users through the channel that
        // actually decides severity, which is the whole reason the descriptor alone is not
        // enough. Restore the none/warning/error/error row together with the measurement.
        new("UPA0021", "none", "none", "none", "none"),
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
        // info, matching the descriptor. Five findings on real games, none of them a per-frame
        // create or destroy; strict and cysharp do not raise it, because raising a rule the
        // corpus has never seen fire correctly is how a gate starts costing more than it saves.
        new("UPA0031", "none", "info", "info", "info"),
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
    /// <remarks>
    /// Read from the analyzers' own <c>[UpaClaim]</c> rather than kept as a list, and not
    /// derived from the diagnostic category either. Category gives the wrong answer: UPA0019
    /// is filed under Performance and reports a defect — Unity reads a boxed yield as
    /// <c>null</c> — so a category rule quietly downgrades it in editor folders while the
    /// analyzer's own editor-only filter, which does read the claim, leaves it reporting. Two
    /// mechanisms disagreeing about one rule is exactly the silent failure this release is
    /// about, so they now read the same source.
    /// </remarks>
    public static readonly string[] EditorRelaxedExceptions =
        UpaClaims.RuleIdsClaiming(UpaClaimKind.Correctness);

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

    /// <summary>
    /// One coexistence overlay: the rules to silence because another tool in the same
    /// project already reports them.
    /// </summary>
    /// <param name="Name">File stem, written as <c>{Name}-coexist.ruleset</c>.</param>
    /// <param name="Defers">The tool being deferred to, named in the file's header.</param>
    /// <param name="Base">The preset this file includes. Chosen per overlay, because a
    /// rule the base already silences makes the entry inert.</param>
    /// <param name="Rules">Rule ids to silence, with the reason for each.</param>
    /// <param name="Caveat">The cost of using this file, stated in the file itself.</param>
    public sealed record Coexist(
        string Name,
        string Defers,
        string Base,
        (string Id, string Why)[] Rules,
        string Caveat);

    /// <summary>
    /// Coexistence overlays. Each generated file <em>includes</em> the base preset and then
    /// overrides it, which is the only direction that works: a ruleset entry in the parent
    /// beats the same entry in an included file, so a file meant to be included cannot
    /// silence anything the base preset already grades. Measured on 2026-08-10 with this
    /// repository's own CLI — the inverted arrangement left the rule reporting.
    /// <para>
    /// What is deliberately absent matters as much as what is here. Rider's coverage of
    /// UPA0001, UPA0002 and UPA0003 is narrower than the rule it would silence, and UPA0001
    /// is the single rule most worth gating, so none of the three is in the Rider file.
    /// </para>
    /// </summary>
    public static readonly Coexist[] Coexists =
    {
        new(
            "rider",
            "Rider's Unity performance inspections",
            "recommended",
            new[]
            {
                // Inert under the recommended base, which already holds UPA0005 at none.
                // Kept so switching the Include to strict or cysharp-stack still defers it.
                ("UPA0005", "Avoid usage of Debug.Log methods in performance critical context"),
                ("UPA0014", "Avoid usage of Find methods in performance critical context, with quick-fixes"),
                ("UPA0015", "Camera.main is inefficient in frequently called methods, with a cache-to-Awake action"),
                ("UPA0016", "Avoid using string based Method Invocation"),
            },
            "Rider's indicators carry no severity and cannot fail a build. This file removes "
            + "these rules from Unity compiles and from upa-cli as well, which leaves a tool "
            + "that cannot gate as your only coverage. The .editorconfig variant next to this "
            + "file silences them in the IDE only, and is the better default."),
        // Deliberately empty. This overlay used to defer UPA0003 to UNT0041, and measurement
        // on real game code showed that trade is a bad one: of fourteen UPA0003 findings only
        // one was on an Animator - the single thing UNT0041 can see - and that one was a false
        // positive. The other thirteen were Material and MaterialPropertyBlock calls, and all
        // three of the rule's true positives were among them. Deferring bought one false
        // positive and gave up every real finding.
        //
        // Roslyn severities cannot be scoped to the Animator overloads, so a partial deferral
        // is not available and nothing is left to defer. The file is kept rather than removed
        // so paths published in earlier releases keep resolving, and so the reason is written
        // down where the next person to notice the overlap will look.
        new(
            "vs",
            "Microsoft.Unity.Analyzers",
            "recommended",
            System.Array.Empty<(string, string)>(),
            "Nothing is deferred. The one overlap - UPA0003 against UNT0041 - was measured on "
            + "three real Unity games and is not worth taking: UNT0041 only sees Animator, "
            + "which was one finding in fourteen and a false positive, while the rule's three "
            + "true positives were all Material and MaterialPropertyBlock calls it cannot see. "
            + "Include this file if you want the recommended preset; it adds nothing else."),
        new(
            "unitask",
            // cysharp-stack rather than recommended: UPA2012 is off in every other preset,
            // so an overlay over recommended would silence something already silent.
            "UniTask.Analyzer",
            "cysharp-stack",
            new[]
            {
                ("UPA2012", "UniTask ships an analyzer that detects unawaited UniTask-returning calls"),
            },
            "UPA2012 is off by default and only the cysharp-stack preset turns it on, which "
            + "is why this file includes that preset rather than recommended. Silencing it "
            + "also gives up the .Forget() code fix, which UniTask.Analyzer does not offer."),
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
