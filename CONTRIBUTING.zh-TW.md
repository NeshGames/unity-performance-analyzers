# 參與貢獻

[English](CONTRIBUTING.md)

你能給這個專案最有價值的東西,是**一條對正確程式碼觸發的規則**。
analyzer 的價值由誤報率決定,而那個數字只有從「不是這個 repo 的專案」才看得到。
在這裡,一段能重現的片段比一個 patch 更有價值。

## 需要什麼

- .NET SDK 8.0——除了 sandbox 之外,建置與測試都不需要 Unity
- Unity 2022.3 LTS 或 Unity 6,只有在你要動 `sandbox/` 或想在編輯器裡確認時才需要

```bash
dotnet build -c Release
dotnet test
```

這兩個綠了,其他東西才值得看。測試跑的是最小的 `UnityEngine` 替身
(`src/UnityStubs/`),不需要裝真的 Unity。

---

## 回報誤報

用 **False positive** 範本開一個 issue。以下資訊能讓它修得快:

- 規則編號,以及它觸發的那一行,
- **仍能觸發的最小片段**——這一項直接決定它多快被修掉,
- Unity 版本,以及該組件是 Editor 還是 player 程式碼,
- 該組件引用了哪些套件(UniTask、ZString、R3、DOTween)——有好幾條規則只在其中之一存在時才存在,
- 你設定過的 `upa_*` 選項,
- 你原本預期的是什麼:完全不該報,還是該報在別的地方。

用 CLI 在 Unity 之外一行就能重現,通常比截圖快,而且給得出可以直接引用的結果:

```bash
dotnet run --project src/UnityPerformanceAnalyzers.Cli -c Release -- \
  Assets/Scripts/Thing.cs --all-warn --format json
```

確認成立的誤報屬於 patch 版。在修正出貨之前,`#pragma warning disable UPA####`
或一筆 ruleset 條目就能壓下它,而且兩者事後都不會變成錯的——**規則編號的意義永不改變**。
見[版本與規則治理](docs/versioning.zh-TW.md)。

---

## 提案一條新規則

這裡的規則有一道多數 analyzer 專案沒有的證據門檻:

> **每一項效能主張都必須通過 IL2CPP 的實測。** 從 IL 語意推、從 .NET 行為推、
> 從某個最佳化「應該」怎樣推,都不算證據。Mono 的數字只作對照;
> **判準是 IL2CPP**,因為那是出貨的東西。

所以一份規則提案至少要有:

1. **那個模式**,以程式碼呈現,以及你會改寫成什麼
2. **它的代價,量出來的**——配置或時間,在 IL2CPP 上,並以「改寫後的版本」當對照組。
   沒有對照組的量測分不出「這件事本來就免費」與「我們量錯東西了」
3. **它多常是錯的**——這個模式在哪些情況下是合理的,而規則如何避免在那裡觸發

這道門檻的存在理由是:**前提過期的規則比沒有規則更糟**——
它建議一個買不到東西的改動,而且每次觸發都在花讀者的注意力。
0.8.0 退役兩條、收窄兩條,正是為此。

如果你有模式但沒有量測,還是請開 issue 並直說。量測是這個專案做得動的工作;
知道該量什麼才是比較難的那一半。

---

## 新增一條規則

**編號由維護者配發**——編號一旦出貨就永久保留,不該由 pull request 自行挑選。
請先開規則提案,然後:

**1. analyzer** —— `src/UnityPerformanceAnalyzers/UPA####Something.cs`

繼承共用基底類,而不是直接繼承 `DiagnosticAnalyzer`;基底類會建立 per-compilation 的
context(profile、hot-path 分類、型別查詢)並交給你的 `InitializeCore`。
有三條限制是由測試強制的:

- `SupportedDiagnostics` 必須是 `static readonly ImmutableArray`,不得每次重建
- **不得有實例欄位,也不得有以 `Compilation` 為鍵的快取**——Roslyn 以同一個 analyzer 實例
  服務多個 compilation,而過期的快取產生的輸出**與正確結果長得一模一樣**
- 除了 `ctx.Options.AdditionalFiles` 之外不得做檔案 IO

**2. release tracking** —— 在
`src/UnityPerformanceAnalyzers/AnalyzerReleases.Unshipped.md` 補一列。
少了它建置會失敗;之後若改了嚴重度卻沒同步記錄,也會再失敗一次。

**3. 測試** —— `src/UnityPerformanceAnalyzers.Tests/UPA####SomethingTests.cs`,至少四條:
該觸發時觸發、不該觸發時安靜、一個邊界案例、以及 code fix(若有)。
位置一律用 inline markup(`{|UPA####:...|}`)斷言,不手寫 span。

**4. 雙語文件頁** —— `docs/rules/UPA####.md` 與 `docs/rules/UPA####.zh-TW.md`。
有測試斷言:兩份都存在、與 descriptor 對嚴重度與預設狀態的說法一致、互相連結、
以及對「有沒有 code fix」的說法一致。

**5. README 表格** —— 由程式產生,不要手改:

```bash
dotnet run --project src/UnityPerformanceAnalyzers.RuleManifest -c Release -- --readme .
```

那一句摘要curated 在 `src/UnityPerformanceAnalyzers.RuleManifest/`;
沒有對應條目的規則會讓建置失敗,而不是渲染出一格空白。

**6. presets** —— 同樣由程式產生,來源是同一張表:

```bash
dotnet run --project src/UnityPerformanceAnalyzers.RuleManifest -c Release -- --presets .
```

每條規則都必須被某個 preset 評級,或被列為刻意不評。
**新規則會在出貨後的下一個版本才進 preset**,這樣沒有人的建置會因為一條他還沒讀過的規則而失敗。

**7. code fix(若改寫是機械性的)** —— `src/UnityPerformanceAnalyzers.CodeFixes/`。
只在改寫**可證明等價**時才提供。一個在你沒想到的情況下會改變行為的 fix,比沒有 fix 更糟,
因為它會在沒被閱讀的情況下被套用。

然後 `dotnet build -c Release && dotnet test`。你漏掉七步中的哪一步,meta 測試會告訴你。

---

## 風格

- 程式碼、識別字、commit message、`docs/rules/*.md`:英文
- 診斷訊息放 `Resources/Strings.resx`,不寫在程式碼裡
- 註解解釋**為什麼**;在「讀者會以為你漏想了那件明顯的事」的地方特別值得寫。
  複述程式碼的註解是雜訊
- commit message 說的是「改了什麼、讀者要付什麼代價」,不是「哪些檔案搬到哪」

## 什麼會被退回

- 效能主張沒有量測支撐的規則
- 自身預設嚴重度高於 Warning 的規則——本套件不會自己決定誰的建置該失敗
- 常見情況對、罕見情況錯的 code fix
- 把別的 analyzer 套件 vendor 進來,或抄別的專案的規則說明文字

## 安全性

漏洞請透過 GitHub 的 security advisory 表單私下回報,不要開公開 issue。
見 [SECURITY.md](SECURITY.md)。
