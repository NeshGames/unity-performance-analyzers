using System.Text;
using UnityPerformanceAnalyzers.Catalog;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Renders the README rule tables from the analyzer assembly plus a curated table of
/// descriptions.
///
/// The tables mix two kinds of fact. Which rules exist, what category they are in, whether
/// they are hot-path scoped, whether they are on by default — all of that comes from the
/// analyzers themselves and had been maintained by hand, which is why the READMEs shipped
/// v0.6.0 claiming 41 rules against a package carrying 45. The plain-English (and Chinese)
/// summary of what each rule reports cannot be derived from anything: the descriptor titles
/// are written for a diagnostic message, not for a table a reader is skimming.
///
/// So the derivable half is generated and the written half is curated here. A rule with no
/// entry fails the build rather than rendering an empty cell, which is what keeps the two
/// halves from drifting apart again.
/// </summary>
public static class ReadmeEmitter
{
    /// <summary>Marks the generated region; everything between the two is rewritten.</summary>
    public const string BeginMarker = "<!-- generated:rules -->";

    /// <summary>Closes the generated region.</summary>
    public const string EndMarker = "<!-- /generated:rules -->";

    /// <summary>Marks the inline rule count in the status line.</summary>
    public const string CountBeginMarker = "<!-- generated:rule-count -->";

    /// <summary>Closes the inline rule count.</summary>
    public const string CountEndMarker = "<!-- /generated:rule-count -->";

    /// <summary>What a rule reports, in both languages, plus anything the metadata cannot say.</summary>
    private sealed record Blurb(
        string English,
        string Chinese,
        string? NoteEnglish = null,
        string? NoteChinese = null,
        string? AwarenessEnglish = null,
        string? AwarenessChinese = null);

    private static readonly IReadOnlyDictionary<string, Blurb> s_blurbs = new Dictionary<string, Blurb>(StringComparer.Ordinal)
    {
        ["UPA0001"] = new("`GetComponent` family called in per-frame methods", "逐幀方法內呼叫 `GetComponent` 家族"),
        ["UPA0002"] = new("`name` / `tag` accessed in per-frame methods", "逐幀方法內存取 `name` / `tag`"),
        ["UPA0003"] = new("String-based shader/animator property access", "字串式 shader/animator 屬性存取"),
        ["UPA0004"] = new("Instantiating accessors (`Renderer.material`, …) in per-frame methods", "逐幀方法內使用實例化存取器(`Renderer.material` 等)"),
        ["UPA0005"] = new("Direct `Debug.Log` calls", "直接呼叫 `Debug.Log`"),
        ["UPA0006"] = new("Reference-type allocation / boxing in per-frame methods", "逐幀方法內的參考型別配置 / 裝箱"),
        ["UPA0007"] = new("Capturing lambdas in per-frame methods", "逐幀方法內的捕捉 lambda"),
        ["UPA0008"] = new("`stackalloc` inside a loop", "迴圈內的 `stackalloc`"),
        ["UPA0009"] = new("`List<T>.Count` not hoisted out of `for` loops", "`for` 迴圈未提出 `List<T>.Count`"),
        ["UPA0010"] = new("Raycasts without explicit `maxDistance` / `layerMask`", "Raycast 未明示 `maxDistance` / `layerMask`"),
        ["UPA0011"] = new("`SetActive` used to toggle UI visibility", "用 `SetActive` 切換 UI 可見性"),
        ["UPA0012"] = new("TMP `text` assignment instead of `SetText`", "TMP `text` 指派而非 `SetText`"),
        ["UPA0013"] = new("`System.Linq` calls in per-frame methods", "逐幀方法內的 `System.Linq` 呼叫", "formerly UPA2001", "原 UPA2001"),
        ["UPA0014"] = new("Scene-search APIs (`GameObject.Find`, `FindObjectOfType`, …) in per-frame methods", "逐幀方法內的場景搜尋 API(`GameObject.Find`、`FindObjectOfType` 等)"),
        ["UPA0015"] = new("`Camera.main` in per-frame methods", "逐幀方法內存取 `Camera.main`"),
        ["UPA0016"] = new("`SendMessage` / `SendMessageUpwards` / `BroadcastMessage` calls", "`SendMessage` / `SendMessageUpwards` / `BroadcastMessage` 呼叫"),
        ["UPA0017"] = new("Array-returning `GetComponents` overloads (use the `List<T>` overloads)", "回傳陣列的 `GetComponents` 多載(改用 `List<T>` 多載)"),
        ["UPA0018"] = new("Allocating array-returning Unity APIs (`Input.touches`, `Animator.parameters`, `Texture2D.GetPixels`, …)", "配置陣列的 Unity 回傳型 API(`Input.touches`、`Animator.parameters`、`Texture2D.GetPixels` 等)"),
        ["UPA0019"] = new("Value types yielded from coroutines (boxing; Unity treats them as `null`)", "coroutine 內 yield 實值型別(裝箱;Unity 視同 `null`)"),
        ["UPA0020"] = new("Lambdas in `WaitUntil` / `WaitWhile` construction", "`WaitUntil` / `WaitWhile` 建構時傳入 lambda"),
        ["UPA0021"] = new("`magnitude` / `Distance` compared where `sqrMagnitude` suffices", "可用 `sqrMagnitude` 取代的 `magnitude` / `Distance` 比較"),
        ["UPA0022"] = new(
            "`Enum.HasFlag` in per-frame methods",
            "逐幀方法內的 `Enum.HasFlag`",
            "deprecated: the call allocates nothing on any supported runtime",
            "已廢止:該呼叫在任何支援的執行環境上都不配置"),
        ["UPA0023"] = new("`OnGUI` declared in player code", "player 程式碼中宣告 `OnGUI`"),
        ["UPA0024"] = new("`Resources.Load` in per-frame methods", "逐幀方法內的 `Resources.Load`"),
        ["UPA0025"] = new("Finalizers declared in runtime code", "runtime 程式碼中宣告 finalizer"),
        ["UPA0026"] = new("Value types boxed by inherited `ToString` / `GetHashCode` / `Equals(object)` / `GetType` calls", "實值型別呼叫繼承的 `ToString` / `GetHashCode` / `Equals(object)` / `GetType` 造成裝箱"),
        ["UPA0027"] = new("`params` overloads called in expanded form, which allocate an array per call", "`params` 多載以展開形式呼叫,每次配置一個陣列"),
        ["UPA0028"] = new("Structs used as collection keys without `IEquatable<T>` and `GetHashCode`", "struct 作集合 key 但未實作 `IEquatable<T>` 與覆寫 `GetHashCode`"),
        ["UPA0029"] = new("Copy loops that `AddRange` would do with one allocation", "可用 `AddRange` 一次配置取代的逐個 `Add` 迴圈"),
        ["UPA0030"] = new("Known-allocating `string` and `Enum` members in per-frame methods", "逐幀方法內已知會配置的 `string` / `Enum` 成員"),

        ["UPA0031"] = new("`Instantiate` or `Destroy` in per-frame methods", "逐幀方法內的 `Instantiate` / `Destroy`"),

        ["UPA1000"] = new(
            "Leaf classes not sealed",
            "葉端類別未 `sealed`",
            "deprecated: the gain measured smaller than the noise on IL2CPP",
            "已廢止:IL2CPP 上量到的差距小於雜訊"),
        ["UPA1001"] = new("Enum switches missing declared members", "enum switch 漏列宣告成員"),

        ["UPA2000"] = new("String building in per-frame methods", "逐幀方法內的字串建構", null, null, "ZString switches the advice", "ZString 切換建議句"),
        ["UPA2010"] = new("`async Task` methods", "`async Task` 方法", null, null, "Runs only with UniTask referenced", "僅在引用 UniTask 時執行"),
        ["UPA2011"] = new("Coroutine `IEnumerator` methods on MonoBehaviours", "MonoBehaviour 上的 coroutine `IEnumerator` 方法", null, null, "Runs only with UniTask referenced", "僅在引用 UniTask 時執行"),
        ["UPA2012"] = new("`async void` / discarded task calls", "`async void` / 被捨棄的 task 呼叫", null, null, "UniTask switches the advice", "UniTask 切換建議句"),
        ["UPA2021"] = new("Public `Action` events modelling observable state", "用 public `Action` event 表達可觀察狀態", null, null, "Runs only with R3 referenced", "僅在引用 R3 時執行"),
        ["UPA2030"] = new("Tweens created in per-frame methods", "逐幀方法內建立 tween", null, null, "Runs only with DOTween referenced", "僅在引用 DOTween 時執行"),
        ["UPA2031"] = new("Discarded infinite tweens (`SetLoops(-1)`) without `SetLink`", "被丟棄且無 `SetLink` 的無限 tween(`SetLoops(-1)`)", null, null, "Runs only with DOTween referenced", "僅在引用 DOTween 時執行"),
        ["UPA2032"] = new("String tween IDs", "字串 tween ID", null, null, "Runs only with DOTween referenced", "僅在引用 DOTween 時執行"),

        ["UPA3000"] = new("Threading APIs (`Thread`, `Task.Run`, `Task.Delay`, …) unsupported on WebGL", "WebGL 不支援的執行緒 API(`Thread`、`Task.Run`、`Task.Delay` 等)"),
        ["UPA3001"] = new("`System.Net.Sockets` unsupported on WebGL", "WebGL 不支援的 `System.Net.Sockets`"),
        ["UPA3002"] = new("Synchronous file IO unsupported on WebGL", "WebGL 不支援的同步檔案 IO"),
        ["UPA3003"] = new("`System.Diagnostics.Process` unsupported on WebGL", "WebGL 不支援的 `System.Diagnostics.Process`"),
        ["UPA3004"] = new("Blocking waits on async operations (`WaitForCompletion`, `Task.Wait`, `.Result`, `GetAwaiter().GetResult()`) — deadlock on single-threaded WebGL", "阻塞等待非同步作業(`WaitForCompletion`、`Task.Wait`、`.Result`、`GetAwaiter().GetResult()`)——單執行緒 WebGL 上直接死鎖"),
    };

    /// <summary>Rewrites both READMEs in place; returns the files it changed.</summary>
    public static IReadOnlyList<string> WriteAll(string repoRoot)
    {
        var rules = UpaRuleCatalog.Rules();
        var missing = rules.Where(rule => !s_blurbs.ContainsKey(rule.Id)).Select(rule => rule.Id).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "No README description for: " + string.Join(", ", missing) +
                ". Add one to ReadmeEmitter — a rule nobody can describe is a rule nobody will use.");
        }

        var written = new List<string>();
        foreach (var (fileName, chinese) in new[] { ("README.md", false), ("README.zh-TW.md", true) })
        {
            var path = Path.Combine(repoRoot, fileName);
            var original = File.ReadAllText(path);
            var updated = Replace(original, BeginMarker, EndMarker, "\n" + Render(rules, chinese));
            updated = Replace(updated, CountBeginMarker, CountEndMarker, rules.Count.ToString());

            if (updated != original)
            {
                File.WriteAllText(path, updated);
                written.Add(path);
            }
        }

        return written;
    }

    private static string Replace(string text, string begin, string end, string body)
    {
        var start = text.IndexOf(begin, StringComparison.Ordinal);
        var stop = text.IndexOf(end, StringComparison.Ordinal);
        if (start < 0 || stop < 0 || stop < start)
        {
            throw new InvalidOperationException($"Markers {begin} … {end} not found in order.");
        }

        return text.Substring(0, start + begin.Length) + body + text.Substring(stop);
    }

    private static string Render(IReadOnlyList<UpaRule> rules, bool chinese)
    {
        var sb = new StringBuilder();

        Section(sb, rules, chinese, "Performance",
            chinese ? "### 效能(除註明外預設啟用)" : "### Performance (on by default unless noted)",
            chinese ? new[] { "ID", "回報內容", "僅熱路徑" } : new[] { "ID", "Reports", "Hot-path only" },
            annotateDefaultOff: true);

        sb.Append(chinese
            ? "\n> 非規則:[Enum 作字典 key](docs/rules/enum-dictionary-keys.zh-TW.md) 說明「enum key 會裝箱、\n> 要自備 comparer」這條流傳已久的建議為何已不適用,附 Mono 與 IL2CPP 的實測數據——\n> 以及真正該處理的地方在哪。\n"
            : "\n> Not a rule: [Enum dictionary keys](docs/rules/enum-dictionary-keys.md) documents why the\n> familiar \"enum keys box, supply a comparer\" advice no longer applies, with measurements\n> across Mono and IL2CPP — and where the cost actually is.\n");

        Section(sb, rules, chinese, "Correctness",
            chinese ? "### 正確性" : "### Correctness",
            chinese ? new[] { "ID", "回報內容" } : new[] { "ID", "Reports" },
            annotateDefaultOff: true);

        Section(sb, rules, chinese, "Ecosystem",
            chinese ? "### 生態(全部預設關閉;建議內容依引用套件調整)" : "### Ecosystem (all off by default; advice adapts to referenced packages)",
            chinese ? new[] { "ID", "回報內容", "套件感知" } : new[] { "ID", "Reports", "Package awareness" },
            annotateDefaultOff: false);

        Section(sb, rules, chinese, "Platform",
            chinese ? "### 平台(預設關閉;僅在定義 `UPA_TARGET_WEBGL` 時執行)" : "### Platform (off by default; run only when `UPA_TARGET_WEBGL` is defined)",
            chinese ? new[] { "ID", "回報內容" } : new[] { "ID", "Reports" },
            annotateDefaultOff: false);

        sb.Append(chinese
            ? "\n套件偵測以引用的 assembly 名稱為準(`UniTask`、`ZString`、`R3`、`DOTween`)——\nper-assembly、全自動、零設定。\n"
            : "\nPackage detection is by referenced assembly name (`UniTask`, `ZString`, `R3`,\n`DOTween`) — per-assembly, automatic, zero configuration.\n");

        return sb.ToString();
    }

    private static void Section(
        StringBuilder sb,
        IReadOnlyList<UpaRule> rules,
        bool chinese,
        string category,
        string heading,
        string[] columns,
        bool annotateDefaultOff)
    {
        sb.Append('\n').Append(heading).Append("\n\n");
        sb.Append("| ").Append(string.Join(" | ", columns)).Append(" |\n");
        sb.Append('|').Append(string.Concat(Enumerable.Repeat("---|", columns.Length))).Append('\n');

        foreach (var rule in rules.Where(r => r.Category == category))
        {
            var blurb = s_blurbs[rule.Id];
            var link = chinese ? $"docs/rules/{rule.Id}.zh-TW.md" : $"docs/rules/{rule.Id}.md";
            var text = (chinese ? blurb.Chinese : blurb.English) + Annotation(rule, blurb, chinese, annotateDefaultOff);

            sb.Append("| [").Append(rule.Id).Append("](").Append(link).Append(") | ").Append(text);

            if (columns.Length == 3)
            {
                var third = category == "Ecosystem"
                    ? (chinese ? blurb.AwarenessChinese : blurb.AwarenessEnglish) ?? string.Empty
                    : rule.HotPath ? "✓" : string.Empty;
                sb.Append(third.Length > 0 ? " | " + third : " |");
            }

            sb.Append(" |\n");
        }
    }

    /// <summary>
    /// The parenthetical after the description. Derived from the descriptor so it cannot
    /// contradict the rule, except for the note, which says something the metadata does not
    /// know (a former ID, for instance).
    /// </summary>
    private static string Annotation(UpaRule rule, Blurb blurb, bool chinese, bool annotateDefaultOff)
    {
        var parts = new List<string>();

        if (rule.DefaultSeverity == "Info")
        {
            parts.Add("Info");
        }

        if (annotateDefaultOff && !rule.EnabledByDefault)
        {
            parts.Add(chinese ? "預設關閉" : "off by default");
        }

        var note = chinese ? blurb.NoteChinese : blurb.NoteEnglish;
        var head = string.Join(chinese ? "," : ", ", parts);

        if (note is { Length: > 0 })
        {
            head = head.Length > 0 ? head + (chinese ? ";" : "; ") + note : note;
        }

        return head.Length > 0 ? " *(" + head + ")*" : string.Empty;
    }
}
