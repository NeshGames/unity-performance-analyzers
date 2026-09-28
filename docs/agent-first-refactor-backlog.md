# Agent-first Refactor Backlog

這份文件是 **unity-performance-analyzers 後續重構的工作佇列**。

目標不是把專案做成通用公開產品，而是把它收斂成：

> **單一維護者 + Claude/Codex 維護 + Unity 6 + Analyzer Core + upa-cli + 可驗證證據鏈**

## 使用方式

開新對話時直接指定 Task ID，例如：

> 請讀取 `AGENTS.md` 與 `docs/agent-first-refactor-backlog.md`，接著完成 **AF-03**。  
> 建 branch、實作、跑 CI、修到綠燈後 merge main，並更新 backlog 狀態。

規則：

- 一個 Task ID 原則上對應一個 PR。
- 不要同時混入不相依的大型重構。
- 完成任務後，把該項改成 `DONE`，記錄 PR / merge commit。
- 若實作證明原建議不成立，改成 `CANCELLED` 並留下技術理由，不要硬做。
- 所有任務仍受 `AGENTS.md` 約束。
- 性能規則的新增/保留/修改，以 Unity IL2CPP evidence 為準，不以 .NET 推論取代。

---

# 已完成

| ID | 狀態 | 工作 | 結果 |
|---|---|---|---|
| AF-00 | DONE | Unity 6 baseline | 移除 Unity 2022.3 support；UPM minimum = 6000.0；Roslyn floor = 4.3.1，保留 4.3.1 / 4.10 load smoke。PR #3 |
| AF-00A | DONE | Shared agent contract | 新增 `AGENTS.md`，`CLAUDE.md` 改為 Claude-specific supplement。PR #3 |
| AF-00B | DONE | AI-first README | root README 改為 agent repository index；移除 root zh-TW README、README generator / drift machinery。PR #4 |
| AF-01 | DONE | Remove Rule Manager / Editor UI | 移除 `package/Editor/`、generated rules.json、Rule Manager probe/screenshot 與 release/CI catalog 維護。PR #5 / merge `b67c53269bac5f964c2fdccd13f060c38768426b` |

---

# 建議執行順序

```text
AF-01  Rule Manager / Editor UI removal
  ↓
AF-02  Preset model simplification
  ↓
AF-03  Analyzer option channel simplification
  ↓
AF-04  UPA2012 self-coexistence
  ↓
AF-05  Retired / low-ROI rule removal
  ↓
AF-06  Remaining optional-rule audit
  ↓
AF-07  CLI surface simplification
  ↓
AF-08  Baseline subsystem simplification
  ↓
AF-09  Rule metadata / diagnostic string source-of-truth consolidation
  ↓
AF-10  Documentation single-language consolidation
  ↓
AF-11  Release pipeline simplification
  ↓
AF-12  Test-suite cleanup
  ↓
AF-13  Sandbox / measurement cleanup
  ↓
AF-14  Public-project maintenance surface cleanup
  ↓
AF-15  One-command agent verification
```

AF-01～AF-05 是最高價值區段。完成後 repository 維護面積會明顯下降。

---

# P0 — 優先處理

## AF-01 — Remove Rule Manager / Editor UI

**Status:** DONE  
**Priority:** P0  
**Risk:** Medium  
**Depends on:** AF-00B

### 目的

目前 `package/Editor/` 為一般使用者提供 GUI，但實際設定者是你與 coding agents。
這層造成 analyzer metadata → generated `rules.json` → RuleCatalog → Editor GUI 的重複維護鏈。

### 建議移除

- `package/Editor/RuleManagerWindow.cs`
- `package/Editor/RuleCatalog.cs`
- `package/Editor/OptionsFile.cs`
- `package/Editor/RulesetFile.cs`
- `package/Editor/WebGlTargetSupport.cs`
- `package/Editor/rules.json`
- Editor asmdef / related meta
- Rule Manager screenshot
- sandbox Rule Manager probes
- Rule Manager-specific tests
- RuleManifest catalog generation branch

### 必須保留的能力

Rule Manager 曾處理的事情要改成 agent/documented workflow：

- severity → 直接修改 ruleset
- analyzer options → 直接修改 additionalfile
- WebGL → 直接設定 `UPA_TARGET_WEBGL` + WebGL ruleset
- per-asmdef ruleset → agent 直接查檔案

### Definition of Done

- UPM package 不再包含 Editor assembly。
- `RuleManifest --all` 不再生成 `package/Editor/rules.json`。
- build/test/load smoke 全綠。
- `AGENTS.md` / SKILL 不再指導 agent 使用 Rule Manager。
- repo 中沒有 Rule Manager dead references。

---

## AF-02 — Collapse presets to agent-oriented profiles

**Status:** TODO  
**Priority:** P0  
**Risk:** Medium  
**Depends on:** AF-01

### 現況

目前有：

- minimal
- recommended
- strict
- cysharp-stack
- editor-relaxed
- webgl-addon
- vs-coexist
- unitask-coexist

這是面向多種 consumer persona 的公開產品設計。

### 目標

收斂成：

- `unity.ruleset` — Unity Editor 日常使用，避免 Error 導致 Safe Mode。
- `ci.ruleset` — Claude/Codex / CI gate，可以提高嚴重度。
- `webgl.ruleset` — 平台 overlay，僅保留確實需要的 WebGL 規則。

### 建議

- 移除 minimal / recommended / strict / cysharp-stack。
- 移除 editor-relaxed；Editor-only filtering 應由 analyzer 自身處理。
- 移除 vs-coexist。
- unitask-coexist 由 AF-04 取代。
- `PresetTable.Row` 不再維護四欄 persona matrix，改成 Unity / CI。
- sample README 改成 agent workflow。

### Definition of Done

- preset source-of-truth 只有 Unity / CI / WebGL 三個概念。
- Unity ruleset 不因 performance finding 讓 Editor 進 Safe Mode。
- CI ruleset 能作為強制 gate。
- generated preset tests 更新且全綠。
- 無 coexist preset。

---

## AF-03 — Simplify analyzer options to one Unity-effective channel

**Status:** TODO  
**Priority:** P0  
**Risk:** Medium  
**Depends on:** AF-01

### 現況

option precedence：

```text
Rules.UnityPerformanceAnalyzers.additionalfile
→ .editorconfig
→ built-in default
```

Unity compile 本身不使用這套 `.editorconfig` option channel；主要用途是外部 toolchain / CLI。

### 目標

改為：

```text
Rules.UnityPerformanceAnalyzers.additionalfile
→ built-in default
```

### 建議修改

- 移除 analyzer 內 per-SyntaxTree `.editorconfig` option fallback。
- 評估並預設移除 `upa-cli --editorconfig`。
- 移除 `EditorConfigOptionsProvider` 與相關 precedence tests。
- `UpaOptions` 只解析 additional file。
- severity 仍由 ruleset 管理，不混進 options。

### 驗收重點

- Unity compile 與 `upa-cli` 使用同一份 additionalfile 時得到相同 option。
- 不再存在「同一 key 有兩個 config channels」。
- 不影響 Roslyn diagnostic severity ruleset 行為。

### Definition of Done

- `UpaOptions` API 明顯縮小。
- 無 `--editorconfig` dead docs/tests/code。
- option tests 只驗證 additionalfile + defaults。

---

## AF-04 — Make UPA2012 coexist with UniTask in code

**Status:** TODO  
**Priority:** P0  
**Risk:** Medium  
**Depends on:** AF-02

### 問題

目前透過 `unitask-coexist.ruleset` 避免 UPA2012 與 `UniTask.Analyzer` 的重複 diagnostic。

### 目標

coexistence policy 進 analyzer code，不再要求 agent 選對 overlay。

建議行為：

```text
async void / async void lambda       → UPA2012
discarded Task / Task<T>             → UPA2012
discarded UniTask / UniTask<T>       → UniTask.Analyzer（UPA2012 不報）
```

### 要做

- 在 profile 有 UniTask 時，UPA2012 不接管 UniTask-returning discarded call。
- 保留 async void 類 correctness 檢查。
- 移除 unitask coexist preset/docs/tests。
- 加有/無 UniTask reference 的 differential tests。

### Definition of Done

- 有 UniTask 時同一個 discarded UniTask 不出現雙診斷。
- Task 與 async void coverage 不下降。
- coexistence 不再需要 ruleset。

---

## AF-05 — Remove retired and low-ROI rules

**Status:** TODO  
**Priority:** P0  
**Risk:** High  
**Depends on:** AF-02  
**Recommended split:** AF-05A / AF-05B

### AF-05A — Directly remove already-retired rules

建議直接刪：

- **UPA0022** — HasFlag premise 已被 Unity 6 IL2CPP measurement 推翻。
- **UPA1000** — sealed leaf gain 小於 measurement noise。

不要再保留 dormant analyzer implementation。

保留一個很小的 retired-ID source，例如：

```text
RetiredRuleIds:
UPA0022
UPA1000
```

用途只是不讓 agent 未來 reuse ID。

### AF-05B — Remove low-ROI active rules

優先評估並建議刪：

- **UPA0009** — 約 26 KB analyzer，為安全 hoist `List.Count` 需要大量 alias/call/accessor analysis；實測收益約數 ns。
- **UPA0021** — `Distance/magnitude` → `sqrMagnitude` 的 Unity 6 IL2CPP 實測差異很小，且 rewrite 降低可讀性。
- **UPA0023** — OnGUI declaration Info/off-by-default，對目前 agent workflow 價值低。

### 每條刪除時

同步刪：

- analyzer
- tests
- strings/resources
- docs
- preset entry
- release metadata（以 Roslyn release-tracking正確方式記錄 removal）
- sandbox probes / snapshots
- overlap references

### Definition of Done

- ID 不 reuse。
- live catalog 不再包含被刪規則。
- release tracking 正確。
- corpus / generated artifacts 全綠。
- 沒有 dead docs / resource keys。

---

# P1 — 第二階段

## AF-06 — Audit remaining optional / opinionated rules

**Status:** TODO  
**Priority:** P1  
**Risk:** Medium  
**Depends on:** AF-05

逐條重新判斷「如果 Agent 看到它，是否幾乎一定希望修」。

優先審查：

- **UPA0011** UI `SetActive` heuristic — 建議偏向刪。
- **UPA2032** string tween ID — Info / low ROI，建議偏向刪。
- **UPA0005** direct Debug.Log — 保留則定位為 house rule。
- **UPA0010** raycast argument shape — 保留但不應過度升級 severity。
- **UPA0012** TMP SetText — 保留 optional。
- **UPA0013** LINQ — 僅 hot-path / CI profile。
- **UPA0020** WaitUntil lambda — optional。
- **UPA0024** Resources.Load — optional。
- **UPA0029** AddRange — 保留時應維持非常窄的安全 pattern。
- **UPA0031** Instantiate/Destroy — Info 即可。
- **UPA2010 / UPA2011 / UPA2021** — 明確標成 architecture / house rules，不混同 universal correctness。

### Deliverable

建立一份小型 machine-readable rule policy（可併入 AF-09）：

```text
Core
Optional
House
Platform
Retired
```

不是再新增多套 preset。

---

## AF-07 — Simplify upa-cli surface

**Status:** TODO  
**Priority:** P1  
**Risk:** Medium  
**Depends on:** AF-03

### 建議保留

- `--init-args`
- response file
- whole assembly
- changed-files narrowing：`--only` / `--only-from`
- `--fail-on`
- `--list-rules`
- text
- json
- baseline（但由 AF-08 瘦身）

### 建議評估移除

- SARIF
- GitHub-specific output
- 只為公開 distribution 存在的 format / flags

如果你的實際 CI 需要 GitLab annotation，可在確認需求後加 **單一 GitLab-friendly format**，不要保留多平台 UI output。

### Definition of Done

- Agent 的標準路徑是 JSON。
- CLI help 明顯縮短。
- `CliOptions.cs` / `OutputWriter.cs` 複雜度下降。
- exit 0/1/2 contract 不變，除非同一 PR 明確宣布 breaking change。

---

## AF-08 — Simplify baseline subsystem

**Status:** TODO  
**Priority:** P1  
**Risk:** Medium  
**Depends on:** AF-07

### 核心需求只保留

```bash
upa-cli ... --baseline upa-baseline.json
upa-cli ... --update-baseline upa-baseline.json
```

### 建議移除

- `--prune-baseline`
- `--report-stale-baseline`
- `--fail-on-stale`
- quota/stale UX 若不是核心必要

Agent 可以直接 review baseline JSON diff。

### Definition of Done

- 既有專案仍能 freeze existing findings。
- 新 violation 仍能被 gate。
- baseline classes / tests 顯著減少。
- baseline format 保持 deterministic。

---

## AF-09 — Consolidate rule metadata and diagnostic strings

**Status:** TODO  
**Priority:** P1  
**Risk:** High  
**Depends on:** AF-05, AF-06

### 問題

一條 rule 的資訊目前分散於：

- analyzer descriptor
- `Strings.resx`
- `Strings.cs`
- attributes
- preset table
- docs
- release tracking

### 目標

建立單一 machine-readable / code-native rule definition source，例如：

```csharp
RuleDefinition(
    Id,
    Title,
    Message,
    Description,
    Category,
    Claim,
    DefaultSeverity,
    Profile,
    Condition)
```

multi-message rule（例如 UPA2012）共用同一 rule metadata。

### 建議評估

因 diagnostic 實際只使用英文：

- 移除 `Strings.resx` / `Strings.cs` mirror。
- `DiagnosticDescriptor` 從 canonical definition 建立。
- 若 Roslyn analyzer correctness rules 對 localization 有硬要求，以 build evidence 決定是否保留最薄 resource layer。

### Definition of Done

- 新增一條 rule 不需要手動同步多份 metadata。
- catalog / CLI / preset generator 可從同一 source 讀取。
- resource consistency test 若 source 不再存在就刪除。
- build 0 warnings。

---

## AF-10 — Make rule documentation single-canonical-language

**Status:** TODO  
**Priority:** P1  
**Risk:** Low  
**Depends on:** AF-05

### 建議

只保留：

- `docs/rules/<ID>.md`（英文 canonical technical doc）

移除：

- `docs/rules/<ID>.zh-TW.md`
- bilingual link tests
- 其他雙語 policy 文件的同步要求

理由：

- diagnostics / code / agent instructions 都以英文技術語境運作。
- 中文需要時由 AI 即時翻譯即可。
- 每條規則兩份文件是高頻 drift surface。

### 例外

如果你實際經常直接閱讀中文規則文件，可以 CANCEL 此任務；這不是架構必要項。

### Definition of Done

- 每條 live rule 只有一份 canonical doc。
- help URI 不變或正確更新。
- RuleDocumentationTests 不再要求 translation pairing。

---

# P2 — 基礎設施瘦身

## AF-11 — Simplify release pipeline

**Status:** TODO  
**Priority:** P2  
**Risk:** High  
**Depends on:** AF-07, AF-09

### 先決定 distribution model

建議預設：

- UPM：Git tag
- CLI：如果只有你與 agents 使用，優先 repo build / 單一 release artifact
- NuGet：若沒有真實需求則移除

### 可刪候選

- NuGet OIDC trusted-publishing preflight
- public NuGet publish branch
- 3-platform self-contained binary matrix（若實際不用）
- 過度複雜的 publish recovery path
- public-product release-note ceremony

### 必須保留

- build
- test
- generated drift
- analyzer compiler-load smoke
- Unity 6 package smoke / 明確 override policy
- tag 內容包含 analyzer DLL
- package version sync

### Definition of Done

release workflow 能用一頁左右 mental model 說明，而不是一個獨立產品。

---

## AF-12 — Test-suite cleanup after surface removal

**Status:** TODO  
**Priority:** P2  
**Risk:** Low  
**Depends on:** AF-01～AF-11 的相關任務

### 強烈保留

- per-rule positive / negative regression tests
- `HotPathDetectorTests`
- `EditorOnlyMethodTests`
- `UpaAnalyzerContractTests`
- differential tests
- corpus regression
- compiler load smoke
- IL2CPP measurement
- analyzer-cost measurement

### 跟功能一起刪

- 已移除 GUI / docs / release ceremony 的 contract tests
- 雙語同步 tests
- migration/public-contributor ceremony tests
- 不再存在 feature 的 self-tests

### 原則

不是追求 test count 下降，而是：

> 每個 test 都能回答「哪個可信的 regression 會讓它變紅？」

---

## AF-13 — Clean sandbox and historical measurements

**Status:** TODO  
**Priority:** P2  
**Risk:** Low  
**Depends on:** AF-05

### 建議

Unity 6-only 之後：

- 移除 committed Unity 2022 measurement reports。
- sandbox scripts 不再帶舊版分支。
- corpus snapshots 更新到 live rule set。
- measurement filenames / docs 僅描述 current baseline。
- 對重要規則保留 IL2CPP control evidence。

### 注意

不要刪掉能解釋「為什麼某 rule 被 retire」的 evidence，必要時搬到：

`docs/evidence/retired/`

而不是留整套舊 runtime support。

---

## AF-14 — Remove public-project maintenance ceremony

**Status:** TODO  
**Priority:** P2  
**Risk:** Low  
**Depends on:** AF-11

候選：

- `CODE_OF_CONDUCT.md`
- `SECURITY.md`（若 repo 仍公開且你希望接收 disclosure，可保留）
- public issue templates
- duplicate `CONTRIBUTING.zh-TW.md`
- migration docs for external legacy users
- public-versioning promises that不再符合 single-user internal direction

### 不要誤刪

- `.claude-plugin/` / `.codex-plugin/` / `skills/`：這些是 AI workflow，不是 public ceremony。
- rule docs：仍是 agent 的 correctness context。

### Definition of Done

repo root 只剩維護/執行真的會用到的入口。

---

## AF-15 — Add one-command agent verification

**Status:** TODO  
**Priority:** P2  
**Risk:** Low  
**Depends on:** AF-01～AF-12

### 目的

目前 `AGENTS.md` 列多條驗證指令；未來指令仍可能 drift。

建立一個 canonical command，例如：

```bash
./scripts/agent-verify.sh
```

內容：

1. build
2. tests
3. generated artifact regeneration + drift
4. analyzer load smoke
5. 必要 package/CLI smoke

Unity Editor / IL2CPP 因需要本機 Editor，不放進一般快速 verify；另提供：

```bash
./scripts/unity-verify.sh
```

### Definition of Done

- `AGENTS.md` 只需指向一個主要 command。
- CI 盡量呼叫同一 script，而不是再複製步驟。
- script 本身失敗方向保守：無法證明 clean 就 non-zero。

---

# 建議暫時不要做

以下不是目前優先事項：

- 新增更多 performance rules。
- 做 call graph / interprocedural analyzer。
- 重做 IDE code fixes。
- 擴回 Unity 2022/2021。
- 增加更多 preset persona。
- 為每個 CI provider 增加 output format。
- 為一般公開使用者重新建立大型 docs website。

先完成 maintenance-surface reduction，再考慮新 rule。

---

# 每個後續對話的完成模板

Agent 完成 Task 時，最後應回報：

```text
Task:
Branch:
PR:
Merge commit:

Changed:
Removed:
Kept intentionally:

Validation:
- build
- tests
- generated drift
- analyzer load smoke
- Unity/IL2CPP (if applicable)

Backlog:
- task marked DONE
- newly discovered follow-up IDs
```

若 CI 綠燈且 task 沒有需要人工判斷的產品決策，可直接 merge `main`。

---

# 最終目標狀態

理想 repository mental model：

```text
AGENTS.md
README.md (AI index)
src/
  Analyzer Core
  upa-cli
  Tests
package/
  Analyzer DLL
  2 core rulesets + WebGL overlay
skills/
sandbox/
  Unity 6 evidence
.github/
  compact CI/release
docs/
  canonical rule docs
```

核心判斷標準：

> 如果某個 subsystem 主要是在幫「不存在的外部使用者」提供選項，而不是提升 analyzer 正確性、
> agent 自動修正品質或 Unity 6 驗證可信度，就應優先刪除或合併。
