# 版本與規則治理

版本號承諾了什麼、規則編號承諾了什麼,以及哪些東西可能在你腳下改變。

Analyzer 這類東西有個特性:你一行程式碼都沒動,升個版就可能讓昨天還過的建置失敗。
這份文件存在的目的,就是讓那件事永遠不會是個意外。

[English](versioning.md)

---

## 請從 tag 安裝

```
https://github.com/NeshGames/unity-performance-analyzers.git?path=/package#v0.8.0
```

UPM 直接從 tag 解析套件,所以**那個 tag 就是你裝到的版本**。
指向分支則是拿到該分支當下的內容,包含一個只在發佈時才會寫入的 `package.json` 版本——
分支安裝不構成一個版本,本文所有內容都不適用。

CLI 也一樣:請 checkout 與你專案所用套件相同的 tag。從不同修訂建置的 CLI 可能認得
該套件沒有的規則,於是命令列與編輯器會對同一份程式碼給出不同結論。

---

## 版本號的意義

`0.MINOR.PATCH`,而開頭那個 0 是有實際意義的。

| | 1.0 之前 | 1.0 起 |
|---|---|---|
| Patch(`0.8.0` → `0.8.1`) | 修 bug、修誤報、文件 | 同左 |
| Minor(`0.8.0` → `0.9.0`) | 下表任何一項,**包含可能讓建置失敗的改動** | 新規則、規則放寬、新選項 |
| Major | — | 任何破壞相容面的改動 |

1.0 之前,請把每個 minor 都當作可能影響建置、並讀 CHANGELOG。
這是 pre-1.0 套件的代價——與其讓版號暗示,不如直說。

**1.0 的意義**,就是把下面那份相容面清單從慣例變成契約。

---

## 規則編號承諾了什麼

**一個 `UPA####` 只要出現在任何 tag 上,該編號就永久用掉了。**
不會被別的規則重用。退役後 analyzer 實作可以移除,但該 ID 永久保留。

這不是潔癖。你的 ruleset 條目、`.editorconfig` 設定、`#pragma warning disable` 註解、
baseline 條目——全部以編號指稱規則,而它們全都住在**你的** repo 裡,不在這裡。
編號一旦回收,上述每一處都會無聲地指向一條你從沒讀過的規則。

因此「廢止一條規則」的意思是:

- 退役決策確定後,live analyzer 與 catalog entry 可以移除,
- ID 加入永久 retired-ID registry,永不再給其他規則使用,
- 效能規則的退役量測證據保留在 `docs/evidence/retired/`,
- 舊 ruleset、pragma、baseline 條目變成無作用的歷史設定,不會改指向新規則。

目前有七個 retired ID:**UPA0009**、**UPA0011**、**UPA0021**、**UPA0022**、
**UPA1000**、**UPA2001**、**UPA2032**。UPA0009、UPA0021、UPA0022、UPA1000 保留
量測證據;UPA0011 與 UPA2032 保留 AF-06 的退役理由;UPA2001 則是在 hot-path LINQ rule
從 ecosystem group 移到 UPA0013 時退役。這些 retired ID 都不再是 live diagnostic。

---

## 嚴重度政策

**沒有任何規則的自身預設高於 Warning。** 40 條規則中,37 條預設 Warning、3 條 Info。
本套件不會自己決定你的建置該失敗。

Unity 日常 profile 刻意不含 Error。需要 Error gate 時使用 `ci.ruleset`,並只交給
`upa-cli`,避免 analyzer policy 讓 Editor 進入 Safe Mode。

生態規則(`UPA2000`+)與平台規則(`UPA3000`+)出廠即關閉。
當被編譯的組件引用了對應套件、或你定義了 `UPA_TARGET_WEBGL` 時,它們才會啟用——
**逐組件、自動、零設定**。

---

## 哪些東西會變,代價是什麼

| 改動 | 版本 | 你可能要做什麼 |
|---|---|---|
| 新規則、新編號 | Minor | 不用做什麼,除非某個 preset 把它評為 error。新規則會在出貨的**下一個**版本才進 preset |
| 規則**報得更少**——收窄、修掉誤報 | Patch 或 minor | 不用。對應的 baseline 條目會變成過期並被回報 |
| 規則**報得更多**——放寬 | **Minor,絕不 patch** | 這是最可能讓建置失敗的改動。CHANGELOG 會指名該規則與它新抓到什麼 |
| 規則廢止 | Minor | 它停止回報;舊 suppress、ruleset 與 baseline 條目會變成無作用設定,可自行清理 |
| 規則自身的預設嚴重度改變 | Minor | 不用,除非你依賴的是預設值而非 preset |
| preset 內容改變 | Minor | 若你是從 sample 複製的,請重新複製。你改過的那份不會被動到 |
| CLI 引數、退出碼、JSON schema | 1.0 起為 Major | 見下方相容面 |
| baseline 檔案格式 | 1.0 起為 Major | 以 `--write-baseline` 重生 |
| live 規則頁搬家 | 永不 | help 連結是 live diagnostic 的一部分 |

真正要注意的是第三列。**一條規則開始報得更多,和你的程式碼變差長得一模一樣**,
而若某個 preset 把它評為 error,建置就會失敗。它一律是 minor、一律在 CHANGELOG 具名,
而 baseline 是「先接受它、之後再修」的正規做法:

```bash
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --write-baseline upa-baseline.json
```

---

## 相容面

以下是**別人的檔案、腳本、pipeline 會指名的東西**。1.0 之後,它們只在 major 版變動;
在那之前,變動一律附帶明說此事的 CHANGELOG 條目。

- **規則編號與 help URL**——被 ruleset、`.editorconfig`、pragma、baseline 指名
- **套件名** `com.neshgames.unity-performance-analyzers` 與組件名 `UnityPerformanceAnalyzers`
- **CLI 引數名與退出碼**——`0` 乾淨、`1` 有達門檻的診斷、`2` 用法或執行錯誤
- **`--format json` 的文件形狀**,由它自己的 `schemaVersion` 欄位標版
- **baseline 檔案格式**,同樣在檔案內標版
- **profile 檔名**——`unity`、`ci`、`webgl`

### Roslyn 4.3.1 與 Unity 6 下限

本專案只支援 Unity 6。analyzer 以 Roslyn 4.3.1 編譯,對齊 Unity 6 官方的
analyzer/source-generator 相容下限;load smoke 另外也會在目前 sandbox 使用的
4.10 編譯器上執行。

以比宿主更新的 Roslyn 建置的 analyzer **不會讓建置失敗**。它會噴一個 `CS8032`,
然後**什麼都不做**——沒有診斷、沒有錯誤、沒有任何跡象顯示一整包規則停止執行了。
安靜和乾淨的專案長得一模一樣。因此,只有在我們真正使用的最舊 Unity 6 專案也能載入時,
才會再拉高依賴。

**支援的編輯器:Unity 6。** 發佈 smoke 使用 `.github/smoke/unity-versions.json`
列出的 Editor;compiler-load smoke 則分別守住 4.3.1 下限與目前的 4.10 宿主。

---

## 當一條規則因為「本來就是錯的」而被移除

本套件的每一項效能主張,都必須通過 **IL2CPP** 的實測——那是出貨的遊戲真正在跑的後端。
從 IL 語意推、從 .NET 行為推、從某個最佳化「應該」怎樣推,都不算證據;
Mono 的數字也不算:Mono 只作對照,**判準是 IL2CPP**。

由此得到的是一條治理規則,不是願景:

> **前提過期的規則,比沒有規則更糟。** 它建議一個買不到東西的改動,
> 而且每次觸發都在花你的注意力。

所以規則會被重新量測,被實測推翻的就退役或收窄——**包含已經出貨的規則**。
0.8.0 先停用 UPA0022 與 UPA1000;AF-05A 再移除兩者 dormant analyzer 與 live rule page,
但永久保留 ID 與量測證據。AF-05B 接著在 Unity 6 IL2CPP 實測後退役 UPA0009 與 UPA0021:
兩者收益確實存在,但相對其維護面與改寫風險過小。AF-06 再退役 UPA0011,因為建議的 UI
改寫不保留 GameObject activation 語意;也退役 UPA2032,因為它只是未經 player-build
量測支持的合法 API 風格取捨。UPA0006 的 enum 引數回報也被撤回,UPA0026 則收窄到唯一
還站得住的呼叫。

如果你發現某條規則的建議在 IL2CPP 上並不成立,那是這個專案最想收到的 bug report。

---

## 回報誤報

規則對正確的程式碼觸發就是缺陷,修它是 patch。以下資訊能讓它修得快:

- 規則編號,以及它觸發的那一行,
- 仍能觸發的**最小**片段,
- Unity 版本,以及該組件是 Editor 還是 player 程式碼,
- 你原本預期的是什麼——不該報,還是該報在別的地方。

用 CLI 一行就能在 Unity 之外重現,通常比截圖快:

```bash
upa-cli Assets/Scripts/Thing.cs --all-warn --format json
```

在修正出貨之前,`#pragma warning disable UPA####` 或一筆 ruleset 條目就能壓下它;
而且修正落地後這兩者都不會變成錯的——**編號的意義永不改變**。
