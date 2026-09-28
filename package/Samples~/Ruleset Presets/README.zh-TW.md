# Agent 嚴重度設定檔

> [English](README.md) | 繁體中文

設定檔刻意只保留少數幾份，因為主要使用者是 coding agent。

| 檔案 | 用途 |
|---|---|
| `unity.ruleset` | 複製成 `Assets/Default.ruleset`。不包含 Error，避免 analyzer finding 把 Editor 推進 Safe Mode。 |
| `ci.ruleset` | 給 `upa-cli` / CI gate 使用；需要強制的規則可提升成 Error。不要當成 Unity 日常 ruleset。 |
| `webgl.ruleset` | UPA3000–UPA3004 的選用 overlay。定義 `UPA_TARGET_WEBGL` 後 include。 |

`unitask-coexist.ruleset` 只是 AF-04 前的過渡檔。它 include `ci.ruleset` 並關閉
UPA2012，避免與 `UniTask.Analyzer` 對同一個 discarded UniTask 重複診斷。

## Unity

```text
unity.ruleset -> Assets/Default.ruleset
```

asmdef 資料夾內的 `Default.ruleset` 會覆寫全專案設定。若 Editor tooling 或 generated
code 需要不同政策，直接在該 asmdef 資料夾放一份小型自訂 ruleset，不再維護另一套 shipped preset。

## CI

```bash
upa-cli @upa-args.rsp --ruleset ci.ruleset --format json --fail-on error
```

CI profile 與 Unity live ruleset 刻意分開。Unity 裡的 Error analyzer diagnostic 會讓腳本
編譯失敗，啟動時可能進 Safe Mode，automation agent 會因此失去 Editor 控制。

## WebGL

```xml
<Include Path="webgl.ruleset" Action="Default" />
```

同時定義 `UPA_TARGET_WEBGL`。overlay 保持 Warning；CI 是否讓 Warning 失敗由
`upa-cli --fail-on` 決定。

規則行為以 `docs/rules/` 為準；live catalog 使用 `upa-cli --list-rules`。
