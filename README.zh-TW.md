# unity-performance-analyzers

> [English](README.md) | 繁體中文

<!-- badges -->
[![Release](https://img.shields.io/github/v/release/NeshGames/unity-performance-analyzers?sort=semver&label=release)](https://github.com/NeshGames/unity-performance-analyzers/releases/latest)
[![Build](https://github.com/NeshGames/unity-performance-analyzers/actions/workflows/pr.yml/badge.svg?branch=main)](https://github.com/NeshGames/unity-performance-analyzers/actions/workflows/pr.yml)
![Unity 2022.3 LTS – Unity 6](https://img.shields.io/badge/Unity-2022.3%20LTS%20%E2%80%93%20Unity%206-black)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE.md)
<!-- /badges -->

把 Unity 效能與正確性慣例變成編譯期檢查的 Roslyn analyzer 集合。規則會依每個
assembly 引用的套件(UniTask、ZString、R3、DOTween)以及專案是否以 WebGL 為目標
**自動調整**。

以 UPM package 形式發佈。支援 **Unity 2022.3 LTS ~ Unity 6**。

![Unity Console 列出兩個腳本的效能警告](.github/images/console-warnings.png)

規則跑在 Unity 自己的編譯裡,所以 Console 會報、IDE 打字時就會報、CI 上用 `upa-cli`
不需要 Editor 也不需要授權。哪些規則能讓建置失敗,由 ruleset 決定。

> **狀態:pre-1.0。** 全部 <!-- generated:rule-count -->46<!-- /generated:rule-count --> 條規則已實作,並在 Unity 2022.3 與 Unity 6 的
> sandbox 建置實測通過。其中兩條——UPA0022 與 UPA1000——已廢止,除非專案自行開啟否則
> 不回報任何東西,理由寫在各自的規則頁。rule ID 一經發佈即穩定,永不重用。

## 安裝

Package Manager > *Add package from git URL…*:

```
https://github.com/NeshGames/unity-performance-analyzers.git?path=/package#v0.8.1
```

analyzer 會自動套用到**專案內的每一個 assembly**——不需要任何 asmdef reference
(package 刻意不含 asmdef;放了反而會把作用範圍限縮到有引用它的 assembly)。

接著選一份嚴重度 preset(見下)。不裝 preset 時,只有預設啟用的規則會以 Warning 回報。

## 嚴重度 preset

在 Package Manager 視窗匯入 **Ruleset Presets** sample,把選定的 preset 複製為
`Assets/Default.ruleset`:

| Preset | 定位 |
|---|---|
| `minimal` | Unity 正確性規則(`UNT` 群)設 error;UPA1001 維持預設 warning——最保守的起手式 |
| `recommended` | + UPA 效能規則設 warning。日常預設 |
| `strict` | 效能規則升為 error;表態性規則開始回報 |
| `cysharp-stack` | + 生態規則設 error(UniTask/ZString/R3 採用) |

同一個 sample 還附:

- `webgl-addon.ruleset` — 以 ruleset 原生 `<Include>` 疊加 UPA3000–3004 到任一 preset;
  需要 `UPA_TARGET_WEBGL` scripting define(見 sample 內說明)
- `editor-relaxed.ruleset` — 放進 Editor asmdef 資料夾改名 `Default.ruleset`,
  讓工具程式碼不受效能規則干擾
- `rider-coexist`、`vs-coexist`、`unitask-coexist` —— 把「你專案裡另一個工具已經在報」的規則
  讓渡出去。每一個都**去 include** 它的基礎 preset(兩個檔一起複製到 `Assets/`,
  再把 coexist 那個改名為 `Default.ruleset`),因為包含者的條目會贏——
  反過來寫的檔案外觀正確但什麼都靜不掉。同名的 `.editorconfig` 只在 IDE 讓渡,
  多數情況那才是你要的:見[與其他工具的規則重疊](docs/overlap.zh-TW.md)
- 各 preset 的 `.editorconfig` 對照版,供 Rider / Visual Studio 同步嚴重度

Unity 只讀 ruleset——它不會把 `.editorconfig` 傳給編譯器(已於 2022.3 與 Unity 6
實測確認)。asmdef 資料夾內的 `Default.ruleset` 會覆寫全專案那份,只影響該 assembly。

匯入 **Smoke Test** sample 可驗證 analyzer 已載入:它刻意違反多條規則,
Console 應立即亮起警告。

## Rule Manager 視窗

**Tools ▸ Unity Performance Analyzers ▸ Rule Manager** 讓你不必手改 XML 就能管理上述一切:

![Rule Manager 視窗列出每條規則的嚴重度、作用範圍與套件條件](.github/images/rule-manager.png)

- **Rules 頁籤**——每條 UPA 規則各有嚴重度下拉(依類別分組、附條件徽章),
  外加 Microsoft.Unity.Analyzers 摺疊區;preset 一鍵套用(直接讀取 package 內容,
  不需先匯入 sample);WebGL 開關會同時維護**所有** build target 的
  `UPA_TARGET_WEBGL` define 與 `webgl-addon.ruleset` 的 Include。
  asmdef 資料夾的 ruleset 覆寫以唯讀清單列出。
- **Options 頁籤**——編輯通用選項檔(見下一節),可選同步寫入 `.editorconfig`。

視窗對 `Assets/Default.ruleset` 的改寫是保守的:其他 analyzer 的條目、
`<Include>` 與註解都原樣保留。

## Analyzer 選項(通用選項檔)

`Assets/Rules.UnityPerformanceAnalyzers.additionalfile` 以 `key = value` 形式承載
所有 analyzer 選項,**Unity 建置與 IDE 分析都會讀取**——Unity 會把 additional file
傳給編譯器,`.editorconfig` 則不會。判定逐 key 進行:選項檔優先於
`.editorconfig`,再優先於內建預設值。

```ini
upa_hot_path_messages = Update,FixedUpdate,LateUpdate,OnTriggerEnter
upa_hot_path_attributes = HotPath,PerformanceCritical
upa_hot_path_include_lambdas = true
upa_enum_switch_allow_default = true
```

| Key | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `upa_hot_path_messages` | 逗號分隔清單 | `Update`、`FixedUpdate`、`LateUpdate`、`OnGUI`、`OnAnimatorMove`、`OnAnimatorIK`、`OnPreCull`、`OnPreRender`、`OnPostRender`、`OnRenderObject`、`OnWillRenderObject`、`OnRenderImage`、`OnTriggerStay`、`OnTriggerStay2D`、`OnCollisionStay`、`OnCollisionStay2D`、`OnParticleUpdateJobScheduled` | 哪些 MonoBehaviour 訊息算逐幀。**整組取代**預設值——要保留的標準訊息也得自己列出。影響全部熱路徑規則 |
| `upa_hot_path_attributes` | 逗號分隔清單 | `HotPath`、`PerformanceCritical` | 哪些 attribute 短名可把任意方法標記為熱路徑,以名稱比對、`Attribute` 後綴可省略。讓非訊息方法也能納入 |
| `upa_hot_path_include_lambdas` | `true` / `false` | `true` | 熱路徑方法內宣告的 lambda 與區域函式是否算熱。若你會把它們拿到他處呼叫,設 `false` |
| `upa_enum_switch_allow_default` | `true` / `false` | `true` | UPA1001 中,`default` 分支(或 discard arm)是否算涵蓋其餘成員 |

解析刻意容錯:`#` 註解、未知 key 與格式錯誤的行一律忽略,無效值落到下一層通道,
重複 key 以最後一筆為準。Rule Manager 的 Options 頁籤會替你編輯這個檔案。

## 規則

從 [UnityEngineAnalyzer](https://github.com/vad710/UnityEngineAnalyzer) 過來?
[遷移指南](docs/migration-unityengineanalyzer.zh-TW.md)逐條對照它的十六條規則,
包含在這裡沒有對應的那八條。

發現某條規則對正確的程式碼觸發?那是這個專案最想收到的回報——見[參與貢獻](CONTRIBUTING.zh-TW.md)。

**這些規則會讓你的編譯多花多少。** 以編譯器自己的 `-reportanalyzer`,在兩個支援的編輯器上
對 sandbox 專案量測:

| | Unity 6(6000.5.3f1) | Unity 2022.3 LTS |
|---|---|---|
| 該次執行的組件編譯數 | 31 | 16 |
| 全部 analyzer 的 CPU 時間 | 4.09 s | 1.33 s |
| **其中這 46 條規則** | **0.95 s(23%)** | **1.33 s(100%)** |
| Unity 自己內建的 analyzer | 2.60 s | 該版本沒有 |
| 規則中位數 | 17 ms | 12 ms |

在 Unity 6 上,**Unity 本來就會跑的那些 analyzer,成本是本套件全部規則的 2.7 倍**。
2022.3 沒有內建 analyzer 可比,所以那個數字就是全部的 analyzer 帳單。

這些是「整次重編譯、跨所有組件的 CPU 時間總和」,不是你等待的時間:analyzer 會並行執行,
同樣兩次執行,編譯器自己回報的總時間是 2.19 s 與 1.12 s。
**語料是 sandbox 專案,它很小**——大型正式專案的數字本專案還沒量過,量到之前不會公布。
以 `sandbox/measure-analyzer-cost.sh` 重現。

**診斷訊息有繁體中文版**,套件本身就帶著這份翻譯。你看不看得到,取決於是誰在問:

| | 語言 |
|---|---|
| Unity Console | **一律英文。** Unity 把編譯器語言固定為 `en-US`,而且會把它附加在專案 `csc.rsp` 之後,所以你設什麼都蓋不過去 |
| `upa-cli` | 一律英文。這個工具刻意以不變語系執行,才能在沒有 ICU 的極簡 CI 容器上啟動得起來 |
| Rider / Visual Studio | IDE 自己的語言——這才是這份翻譯真正要落地的地方 |

所以「IDE 是中文、Console 是英文」是預期結果,不是裝壞了。

每條規則的完整文件:[`docs/rules/`](docs/rules/)。
版本號與規則編號各自承諾了什麼、升版可能在你腳下改變什麼:
[版本與規則治理](docs/versioning.zh-TW.md)。

<!-- generated:rules -->

### 效能(除註明外預設啟用)

| ID | 回報內容 | 僅熱路徑 |
|---|---|---|
| [UPA0001](docs/rules/UPA0001.zh-TW.md) | 逐幀方法內呼叫 `GetComponent` 家族 | ✓ |
| [UPA0002](docs/rules/UPA0002.zh-TW.md) | 逐幀方法內存取 `name` / `tag` | ✓ |
| [UPA0003](docs/rules/UPA0003.zh-TW.md) | 字串式 shader/animator 屬性存取 | |
| [UPA0004](docs/rules/UPA0004.zh-TW.md) | 逐幀方法內使用實例化存取器(`Renderer.material` 等) | ✓ |
| [UPA0005](docs/rules/UPA0005.zh-TW.md) | 直接呼叫 `Debug.Log` *(預設關閉)* | |
| [UPA0006](docs/rules/UPA0006.zh-TW.md) | 逐幀方法內的參考型別配置 / 裝箱 | ✓ |
| [UPA0007](docs/rules/UPA0007.zh-TW.md) | 逐幀方法內的捕捉 lambda | ✓ |
| [UPA0008](docs/rules/UPA0008.zh-TW.md) | 迴圈內的 `stackalloc` | |
| [UPA0009](docs/rules/UPA0009.zh-TW.md) | `for` 迴圈未提出 `List<T>.Count` *(預設關閉)* | ✓ |
| [UPA0010](docs/rules/UPA0010.zh-TW.md) | Raycast 未明示 `maxDistance` / `layerMask` | |
| [UPA0011](docs/rules/UPA0011.zh-TW.md) | 用 `SetActive` 切換 UI 可見性 *(預設關閉)* | |
| [UPA0012](docs/rules/UPA0012.zh-TW.md) | TMP `text` 指派而非 `SetText` *(預設關閉)* | ✓ |
| [UPA0013](docs/rules/UPA0013.zh-TW.md) | 逐幀方法內的 `System.Linq` 呼叫 *(預設關閉;原 UPA2001)* | ✓ |
| [UPA0014](docs/rules/UPA0014.zh-TW.md) | 逐幀方法內的場景搜尋 API(`GameObject.Find`、`FindObjectOfType` 等) | ✓ |
| [UPA0015](docs/rules/UPA0015.zh-TW.md) | 逐幀方法內存取 `Camera.main` *(Info)* | ✓ |
| [UPA0016](docs/rules/UPA0016.zh-TW.md) | `SendMessage` / `SendMessageUpwards` / `BroadcastMessage` 呼叫 | |
| [UPA0017](docs/rules/UPA0017.zh-TW.md) | 回傳陣列的 `GetComponents` 多載(改用 `List<T>` 多載) | ✓ |
| [UPA0018](docs/rules/UPA0018.zh-TW.md) | 配置陣列的 Unity 回傳型 API(`Input.touches`、`Animator.parameters`、`Texture2D.GetPixels` 等) | ✓ |
| [UPA0019](docs/rules/UPA0019.zh-TW.md) | coroutine 內 yield 實值型別(裝箱;Unity 視同 `null`) | |
| [UPA0020](docs/rules/UPA0020.zh-TW.md) | `WaitUntil` / `WaitWhile` 建構時傳入 lambda *(預設關閉)* | |
| [UPA0021](docs/rules/UPA0021.zh-TW.md) | 可用 `sqrMagnitude` 取代的 `magnitude` / `Distance` 比較 | |
| [UPA0022](docs/rules/UPA0022.zh-TW.md) | 逐幀方法內的 `Enum.HasFlag` *(預設關閉;已廢止:該呼叫在任何支援的執行環境上都不配置)* | ✓ |
| [UPA0023](docs/rules/UPA0023.zh-TW.md) | player 程式碼中宣告 `OnGUI` *(Info,預設關閉)* | |
| [UPA0024](docs/rules/UPA0024.zh-TW.md) | 逐幀方法內的 `Resources.Load` *(預設關閉)* | ✓ |
| [UPA0025](docs/rules/UPA0025.zh-TW.md) | runtime 程式碼中宣告 finalizer | |
| [UPA0026](docs/rules/UPA0026.zh-TW.md) | 實值型別呼叫繼承的 `ToString` / `GetHashCode` / `Equals(object)` / `GetType` 造成裝箱 | ✓ |
| [UPA0027](docs/rules/UPA0027.zh-TW.md) | `params` 多載以展開形式呼叫,每次配置一個陣列 | ✓ |
| [UPA0028](docs/rules/UPA0028.zh-TW.md) | struct 作集合 key 但未實作 `IEquatable<T>` 與覆寫 `GetHashCode` | |
| [UPA0029](docs/rules/UPA0029.zh-TW.md) | 可用 `AddRange` 一次配置取代的逐個 `Add` 迴圈 | |
| [UPA0030](docs/rules/UPA0030.zh-TW.md) | 逐幀方法內已知會配置的 `string` / `Enum` 成員 | ✓ |
| [UPA0031](docs/rules/UPA0031.zh-TW.md) | 逐幀方法內的 `Instantiate` / `Destroy` | ✓ |

> 非規則:[Enum 作字典 key](docs/rules/enum-dictionary-keys.zh-TW.md) 說明「enum key 會裝箱、
> 要自備 comparer」這條流傳已久的建議為何已不適用,附 Mono 與 IL2CPP 的實測數據——
> 以及真正該處理的地方在哪。

### 正確性

| ID | 回報內容 |
|---|---|
| [UPA1000](docs/rules/UPA1000.zh-TW.md) | 葉端類別未 `sealed` *(預設關閉;已廢止:IL2CPP 上量到的差距小於雜訊)* |
| [UPA1001](docs/rules/UPA1001.zh-TW.md) | enum switch 漏列宣告成員 |

### 生態(全部預設關閉;建議內容依引用套件調整)

| ID | 回報內容 | 套件感知 |
|---|---|---|
| [UPA2000](docs/rules/UPA2000.zh-TW.md) | 逐幀方法內的字串建構 | ZString 切換建議句 |
| [UPA2010](docs/rules/UPA2010.zh-TW.md) | `async Task` 方法 | 僅在引用 UniTask 時執行 |
| [UPA2011](docs/rules/UPA2011.zh-TW.md) | MonoBehaviour 上的 coroutine `IEnumerator` 方法 | 僅在引用 UniTask 時執行 |
| [UPA2012](docs/rules/UPA2012.zh-TW.md) | `async void` / 被捨棄的 task 呼叫 | UniTask 切換建議句 |
| [UPA2021](docs/rules/UPA2021.zh-TW.md) | 用 public `Action` event 表達可觀察狀態 | 僅在引用 R3 時執行 |
| [UPA2030](docs/rules/UPA2030.zh-TW.md) | 逐幀方法內建立 tween | 僅在引用 DOTween 時執行 |
| [UPA2031](docs/rules/UPA2031.zh-TW.md) | 被丟棄且無 `SetLink` 的無限 tween(`SetLoops(-1)`) | 僅在引用 DOTween 時執行 |
| [UPA2032](docs/rules/UPA2032.zh-TW.md) | 字串 tween ID *(Info)* | 僅在引用 DOTween 時執行 |

### 平台(預設關閉;僅在定義 `UPA_TARGET_WEBGL` 時執行)

| ID | 回報內容 |
|---|---|
| [UPA3000](docs/rules/UPA3000.zh-TW.md) | WebGL 不支援的執行緒 API(`Thread`、`Task.Run`、`Task.Delay` 等) |
| [UPA3001](docs/rules/UPA3001.zh-TW.md) | WebGL 不支援的 `System.Net.Sockets` |
| [UPA3002](docs/rules/UPA3002.zh-TW.md) | WebGL 不支援的同步檔案 IO |
| [UPA3003](docs/rules/UPA3003.zh-TW.md) | WebGL 不支援的 `System.Diagnostics.Process` |
| [UPA3004](docs/rules/UPA3004.zh-TW.md) | 阻塞等待非同步作業(`WaitForCompletion`、`Task.Wait`、`.Result`、`GetAwaiter().GetResult()`)——單執行緒 WebGL 上直接死鎖 |

套件偵測以引用的 assembly 名稱為準(`UniTask`、`ZString`、`R3`、`DOTween`)——
per-assembly、全自動、零設定。
<!-- /generated:rules -->

## Code fix

九條規則附自動修正,由 IDE 在診斷出現處提供:

![IDE 提供 UPA0003 的修正,含預覽與 Fix All 範圍](.github/images/ide-inline.png)

診斷訊息跟隨 IDE 的語言——上圖是繁體中文,本套件自帶這份翻譯。
Unity Console 一律英文,那是 Unity 自己的設定,專案蓋不過去。

![套用後:ID 被快取在發出呼叫的那個型別上](.github/images/codefix-result.png)

Fix All 在同一型別內同一名稱只產生一個欄位。這正是它在真的會出現這條規則的檔案裡用得下去的
原因:在上面那個範例專案上跑,兩個型別、三處呼叫,產生**兩個**欄位——一個型別一個,
不是一次呼叫一個。


| 規則 | 修正 |
|---|---|
| UPA0003 | 把 shader / animator 名稱快取成呼叫端型別上的 `static readonly int`,並改用整數多載 |
| UPA0019 | `yield return <裝箱值>` → `yield return null` |
| UPA0021 | 改比較平方長度,免去開根號 |
| UPA0026 | `x.GetType()` → `typeof(T)`,僅在丟掉接收者不改變執行內容時提供 |
| UPA0009 | 把 `list.Count` 提升為迴圈前宣告的區域變數 |
| UPA0029 | 把複製陣列的迴圈換成 `AddRange`,僅在不可能別名時提供 |
| UPA2031 | 在被丟棄的無限 tween 後附加 `.SetLink(gameObject)` |
| UPA2012 | 在未 await 的 UniTask 呼叫後附加 `.Forget()` |
| UPA2000 | `"a: " + n` → `ZString.Concat("a: ", n)`,僅在有非字串運算元時提供 |

修正位於隨 analyzer 一同散布的第二顆組件。Unity 會把兩顆都交給編譯器;
修正本身是 IDE 專用的,編譯器用不到。

UPA0029 的修正只在來源為陣列時提供:兩個 `List<T>` 參考可能在執行期是同一個 list,
那時改寫會改變自我複製的行為。理由見[該規則文件](docs/rules/UPA0029.zh-TW.md)。

## 調校與抑制

每份規則文件都有「如何設定或抑制」章節。速查:

- **單一呼叫點**:`#pragma warning disable UPA0006` / `#pragma warning restore UPA0006`
- **單一 assembly**:在該 asmdef 資料夾放 `Default.ruleset`(參考 `editor-relaxed.ruleset`)
- **整個專案**:修改 `Assets/Default.ruleset` 裡該規則的那一行
- **熱路徑判定**(哪些方法算逐幀)與其他所有 analyzer 選項:寫在通用選項檔
  (見上方「Analyzer 選項」一節)——Unity 建置與 IDE 一體生效;
  `.editorconfig` 仍可作為 IDE 端備援。

熱方法內的冷分支(延遲初始化、罕見除錯路徑)仍會被回報——analyzer 不做流程分析。
針對這類位置請就地抑制,而不是關掉整條規則。

## 與其他工具的關係

多數 Unity 專案本來就跑著 Rider、Microsoft.Unity.Analyzers 或 Project Auditor。
[`docs/overlap.md`](docs/overlap.md) 逐條記錄了「還有誰會報同一件事、該怎麼辦」——
包含那個讓共存變便宜的不對稱:Unity 會把 `.ruleset` 傳給編譯器、**不會**傳 `.editorconfig`,
所以一條規則可以只在 IDE 靜音、同時仍然守著 build。

## Microsoft.Unity.Analyzers 相容性

preset 也為 `UNT####` 規則分級;這些條目只在專案裡有 Microsoft.Unity.Analyzers 時生效
(例如 Visual Studio Tools for Unity 內建的那份)。若你自行安裝到專案,它的 Roslyn
需求不得超過 Unity 內建編譯器,否則會以**無聲的** `CS8032` 警告失效:

| Unity | 內建 Roslyn | 可安全使用的 Microsoft.Unity.Analyzers |
|---|---|---|
| 2022.3 LTS / Unity 6 | 4.3.1(6000.5:4.10) | 最新(1.27.0)——**唯 1.23.0 除外** |
| 2021.3 LTS *(本 package 不支援)* | 3.9 | ≤ 1.22.0 |

⚠️ **絕對不要安裝 Microsoft.Unity.Analyzers 1.23.0**:它引用 Roslyn 4.14,
目前沒有任何 Unity 版本內建到這個版本——裝了在所有 Unity 上都無聲失效。

本 package 自身以 Roslyn 3.8 為目標,在所有支援的 Unity 版本都能載入。

## 命令列驗證工具(`upa-cli`)

在 Unity 之外、一秒內跑完同一套 analyzer——CI 或本機快速檢查不必開 Editor,
也不必等專案匯入。

目前尚未上架 NuGet。套件 id 與命令名一經發佈即永久固定,其下推出的每個版本亦然,
因此這一步留到 1.0。在那之前有兩條路。

**直接下載。** 每個 [release](https://github.com/NeshGames/unity-performance-analyzers/releases/latest)
都附上各平台可直接執行的壓縮檔:

| 平台 | 檔案 |
|---|---|
| Linux | `upa-cli-<version>-linux-x64.tar.gz` |
| macOS(Apple silicon) | `upa-cli-<version>-osx-arm64.tar.gz` |
| Windows | `upa-cli-<version>-win-x64.zip` |

它們是 self-contained 的——不必裝 .NET、不必 clone、不必建置——而且每一份都是在它所針對的
平台上建置,並在該平台上真的分析過一個檔案之後,才會被掛上 release。

**或自行建置**:

```bash
git clone https://github.com/NeshGames/unity-performance-analyzers.git
cd unity-performance-analyzers
dotnet build UnityPerformanceAnalyzers.sln -c Release
dotnet run --project src/UnityPerformanceAnalyzers.Cli -c Release --no-build -- --version
```

兩條路都要對齊專案所用的套件版本——切到該 tag,或取該 release 的壓縮檔。
以不同修訂版而來的 CLI 可能認得那份套件沒有的規則,命令列與 Editor 就會對同一份
程式碼給出不同答案。

以下範例寫成 `upa-cli`;從 clone 執行時,對應
`dotnet run --project src/UnityPerformanceAnalyzers.Cli -c Release --no-build --`。

```bash
# 分析檔案(有達門檻的診斷即 exit 1)
upa-cli Assets/Scripts/Player.cs

# CI 關卡:針對某個組件的完整原始碼集
# (pattern 要加引號:由工具自己展開,各種 shell 行為才一致)
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --format json --fail-on error

# 模擬「專案引用了 UniTask、且以 WebGL 為目標」
upa-cli Assets/Scripts/Loader.cs --reference UniTask --define UPA_TARGET_WEBGL

# 直接把發現標在 pull request 的 diff 上,不需上傳步驟
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --format github

# 這份建置認得哪些規則?
upa-cli --list-rules
```

想直接從原始碼跑?下面每個 `upa-cli` 都換成
`dotnet run --project src/UnityPerformanceAnalyzers.Cli --`。

退出碼:`0` 乾淨、`1` 有達到 `--fail-on`(預設 `warning`)的診斷、
`2` 用法或執行錯誤——**包含任一 analyzer 執行失敗**,且與 `--fail-on` 無關:
崩掉的規則根本沒產出可衡量的發現,這次執行就不能算乾淨。

### 引數

| 引數 | 說明 | 範例 |
|---|---|---|
| `<file...>` | 要分析的 `.cs` 檔(至少一個)。含 `*`、`?`、`**` 的 pattern **由工具自己展開**——請加引號避免 shell 搶先展開,如此在各種 shell 行為一致。pattern 沒對到任何檔案視為錯誤 | `upa-cli "Assets/**/*.cs"` |
| `--reference <名稱\|路徑>` | 給**名稱**只讓套件「看起來存在」,這正是條件式規則檢查的東西;給 **DLL 路徑**則載入真實組件,呼叫該套件 API 的程式碼才解析得出來。可重複,兩種形式可混用 | `--reference UniTask`<br>`--reference Assets/Plugins/DOTween/DOTween.dll` |
| `--define <符號>` | 加入前處理符號。可重複 | `--define UPA_TARGET_WEBGL` |
| `--assembly-name <名稱>` | 編譯組件名,預設 `Assembly-CSharp`。player 程式碼規則會跳過 `*.Editor` 組件 | `--assembly-name MyGame.Tools.Editor` |
| `--ruleset <路徑>` | 套用 `.ruleset` 的嚴重度 | `--ruleset Assets/Default.ruleset` |
| `--editorconfig <路徑>` | 套用 `.editorconfig` 的嚴重度**與** `upa_*` analyzer 選項 | `--editorconfig .editorconfig` |
| `--additionalfile <路徑>` | 傳入 additional file(例如通用選項檔)。可重複 | `--additionalfile Assets/Rules.UnityPerformanceAnalyzers.additionalfile` |
| `@<路徑>` | 由檔案供給引數,每行一個,在 `@` 出現的位置展開。一整個組件的引用與 define 放不進 Windows 的命令列 | `upa-cli @args.rsp` |
| `--unity-dll-dir <目錄>` | 改用真實 Unity 組件目錄,而非內建 stub | `--unity-dll-dir <UnityEditor>/Data/Managed/UnityEngine` |
| `--all-warn` | 強制所有規則以 warning 開啟,蓋過 ruleset 與 editorconfig | `--all-warn` |
| `--whole-assembly` | 宣告這組檔案構成完整組件:啟用整組件規則,且編譯錯誤變致命 | `--whole-assembly` |
| `--fail-on <等級>` | 退出碼 1 的門檻:`none`、`info`、`warning`(預設)、`error` | `--fail-on error` |
| `--baseline <path>` | 壓下 baseline 檔中已記錄的違規,只回報新增的 | `--baseline upa-baseline.json` |
| `--write-baseline <path>` | 把目前的違規寫成 baseline。需搭配 `--whole-assembly`;成功時以 0 結束 | `--write-baseline upa-baseline.json --whole-assembly` |
| `--prune-baseline` | 與 `--baseline` 併用:移除本次未用到的額度後以 0 結束。**只減不增**——期間新出現的違規不會被吸收 | `--baseline upa-baseline.json --prune-baseline --whole-assembly` |
| `--report-stale-baseline` | 逐筆列出過期條目,不只印總數 | `--baseline upa-baseline.json --report-stale-baseline` |
| `--fail-on-stale` | baseline 有未用額度時以 1 結束;判定不了時以 2 結束 | `--baseline upa-baseline.json --fail-on-stale` |
| `--format <格式>` | `text`(預設)、`json`、`sarif` 或 `github`——見[接進 CI](#接進-ci) | `--format sarif` |
| `--list-rules` | 印出這份建置的規則目錄,不做分析 | `upa-cli --list-rules --format json` |
| `--init-args <路徑>` | 依 Unity 實際編譯該組件的引數產生回應檔後結束。需要該專案在 Unity 裡編譯過至少一次 | `upa-cli --init-args upa-args.rsp` |
| `--project <目錄>` | `--init-args` 的 Unity 專案根目錄,預設為目前目錄 | `--project ../MyGame` |
| `--version` | 印出工具版本 | `upa-cli --version` |
| `--help`、`-h` | 印出用法 | `upa-cli --help` |

嚴重度優先序(弱→強):ruleset 的 `<IncludeAll>` 全域動作 → ruleset 的具名條目 →
`--editorconfig`(可依檔案樣式分別設定)→ `--all-warn`。

### 接進 CI

有兩種輸出格式是給「本工具以外的機器」看的。上面那個 JSON 是本工具自己的形狀,
除了本專案沒有別的消費端。

**`--format sarif`** 輸出 SARIF 2.1.0——GitHub code scanning、Azure DevOps、Sonar、
Qodana 都吃這個格式。在 GitHub 上,發現會變成 diff 上的註記、在多次執行之間以 alert
的形式留存,並帶著規則的說明連結:

```yaml
- run: upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --format sarif --fail-on none > upa.sarif
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: upa.sarif
```

那裡的 `--fail-on none` 是刻意的:上傳步驟必須跑得到,而分析步驟一旦失敗就會跳過它。
要擋 PR 就擋在 alert 上,或用門檻再跑一次。

**`--format github`** 印出 workflow command,同樣把發現標在 diff 上,
但不需要上傳步驟、不需要 token、不需要額外權限:

```yaml
- run: upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --format github
```

代價是註記只屬於該次執行(不是可追蹤的 alert),且 GitHub 對單一步驟能渲染的註記數量
有上限。換來的是一行 YAML。

兩種格式都原樣輸出呼叫端給的檔案路徑,所以請**在 repo 根目錄執行並給相對路徑**——
絕對路徑會標到服務端找不到的檔案上。被 baseline 壓下的違規在兩種格式中都不會出現,
但該次執行仍會回報它藏了幾筆。

### 凍結存量違規

在已經累積幾百個命中的專案裡打開這些規則,是多數導入停下來的地方。
baseline 記錄「今天有什麼」,之後只回報新增的:

```bash
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --write-baseline upa-baseline.json
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --baseline upa-baseline.json --fail-on warning
```

當成關卡用時,比對那一行同樣要帶 `--whole-assembly`。沒有它,編譯錯誤就不致命,
而編譯錯誤正是規則安靜下來的原因——規則以解析後的符號比對,少一個引用只會讓它
不觸發,而不是報錯。接著 baseline 把剩下的也壓掉,整個執行以 0 結束:一個對著
「工具其實沒能分析的程式碼」亮綠燈的關卡。若只是拿單一改動檔去對契約,
不帶 `--whole-assembly` 仍然是對的用法——那不是關卡。

`upa-baseline.json` 要進版控——這是團隊共享的契約,不是本機快取。
內容是明文,可以在 diff 裡讀、可以審;裡面的路徑相對於它自己所在的目錄,
所以從哪個工作目錄跑、在哪台機器上跑,結果都一樣。

一筆違規由「檔案 + 規則 + 所在型別與成員 + 空白壓縮後的該行原始碼」識別,
**刻意不含行號**,所以搬動程式碼或重新排版不會讓存量整批冒出來。
需要知道的代價:成員更名或搬移會讓它的違規重新算成新增;
同一個成員裡兩行完全相同的違規共用一筆記錄,修掉一筆又在同成員新增一筆不會被發現。

寫入 baseline 需要 `--whole-assembly`,且在 analyzer 失敗或編譯不過時拒絕寫入——
一次低報的分析會把它沒看到的存量也一併凍結。
以部分檔案重生同樣被拒絕;但已被刪除或改名的檔案,其條目會直接移除。

**讓債看得出來在變小。** baseline 只會壓制,所以一筆「真的修好了」的違規,它的條目會**永遠留著**。
半年後那個檔案變成沒人敢碰的化石,而且——更糟的是——**沒有人看得到債在減少**。
兩個指令處理這件事:

```bash
# 哪些條目已經對不到任何東西了?
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --baseline upa-baseline.json   --report-stale-baseline

# 把本次沒用到的額度移除
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --baseline upa-baseline.json   --prune-baseline
```

`--prune-baseline` **只減不增**,而這正是它與 `--write-baseline` 重生的差別。
重生會把本次找到的**全部**違規凍結進契約,包含這段期間新出現的那些——
一個為了「讓檔案變小」而使用的指令,結果安靜地把契約變大了。
清除則是移除未用到的額度,並讓新違規繼續回報。

它的拒絕條件與寫入相同,再加上一條在這裡更重要的:
**本次執行必須涵蓋 baseline 中仍存在於磁碟上的每一個檔案**。
從單一變更檔清除,會對其餘所有檔案都找不到違規,並把那當成債已還清。
檔案已被刪除或改名的條目會被移除,與重生時的行為一致。

`--fail-on-stale` 把它變成關卡:有未用額度時以 1 結束,
執行過於不完整、判定不了時以 **2** 結束——因為一個被問「baseline 過期了嗎」的關卡,
不該在它其實想說「我查不了」的時候回答「沒有」。

一個值得先知道的意外:條目的鍵含所在成員,所以**改成員名會同時製造一筆過期與一筆新違規**。
清除時兩件事都會發生——舊的那筆被刪掉,新的那筆不會被吸收。

**要當關卡用?** 請帶 `--whole-assembly` 與該組件的完整原始碼集,
**並為程式碼實際呼叫到的每個套件各給一個 `--reference <DLL 路徑>`**
(若用到 stub 未涵蓋的 Unity API,再加 `--unity-dll-dir`)。這幾者合起來
才把「參考用」變成「可信賴」:整組件規則會開始回報,編譯不過則以 exit 2 結束,
不會回報一份它其實沒能驗證的乾淨結果。少給某個套件 DLL 時,工具會因未解型別
失敗並逐條列出,而不是安靜地少報。

這些引數的量超過命令列裝得下的。一個真實組件的份量——該專案的原始碼、它的 define、
每個套件一個引用——動輒數萬字元,而 Windows 的上限是 32,767。所以這個檔由工具產生:

```bash
cd MyUnityProject
upa-cli --init-args upa-args.rsp --assembly-name Assembly-CSharp
upa-cli @upa-args.rsp --format sarif > upa.sarif
```

`--init-args` 讀的是 **Unity 實際用來編譯該組件的東西**——全部 scripting define、
全部引用、完整原始碼集——來源是 Unity 自己的建置交給 C# 編譯器的回應檔。
不必安裝任何東西、也不必重新產生專案檔:每次編譯都會寫出它,
所以只要這個專案在 Unity 裡開過一次就已經有了。

產出的檔案每行一個引數、`#` 開頭為註解,路徑相對於專案根目錄——請從那裡執行。
唯一與機器綁定的是 `--unity-dll-dir`,它指向你的 Unity 安裝位置;在 CI 上請改指向
該機器的 Unity。改了套件、define 或編輯器版本之後要重生。過期的檔案會在「那個搬走的
引用」上直接失敗,而不是安靜地少分析一些東西。

引數在 `@file` 出現的位置展開,因此寫在它後面的引數仍然蓋得過檔案裡的設定——
`--format`、`--fail-on`、`--baseline` 因此仍然是呼叫端的決定,而不是專案的事實。

**與 Unity 建置的差異**——最終權威仍是 Unity 自己的編譯:

- 傳入的檔案清單不等於 assembly 邊界,因此需要整個組件才能判定的規則預設略過,
  要跑請加 `--whole-assembly`。這樣的規則只有 UPA1000,而它已廢止,
  所以這個旗標現在主要影響的是寫 baseline、以及編譯錯誤是否致命。
- `--reference <名稱>` 只讓套件「看起來存在」,足以啟用該套件的規則,
  但該套件的 API 仍無法解析。程式碼真的有呼叫時,請改給 DLL 路徑——
  `--reference Assets/Plugins/DOTween/DOTween.dll`。
- 引用了未傳入之型別的檔案會產生編譯錯誤,而這會削弱分析:規則以解析後的符號比對,
  型別解析失敗時可能靜默漏報。工具只回報數量並繼續執行——**但 `--whole-assembly`
  例外**:既然你宣告了這是完整編譯單元,它會以 exit 2 結束,而不是讓關卡放行
  一份沒被好好分析的程式碼。

## Repository 結構

| 路徑 | 用途 |
|---|---|
| `src/UnityPerformanceAnalyzers/` | analyzer 組件(netstandard2.0,Roslyn 3.8) |
| `src/UnityPerformanceAnalyzers.CodeFixes/` | IDE 專用 code fix |
| `src/UnityPerformanceAnalyzers.Cli/` | `upa-cli`——不透過 Unity 執行規則 |
| `src/UnityPerformanceAnalyzers.Tests/` | xUnit analyzer 測試(net8.0) |
| `src/UnityStubs/` | 測試用的最小 UnityEngine 手寫替身 |
| `package/` | UPM 發佈根目錄 |
| `sandbox/UnityProject/` | 消費端驗證專案(Unity 2022.3) |
| `docs/rules/` | 各規則文件 |

## 建置

```bash
dotnet build UnityPerformanceAnalyzers.sln -c Release
dotnet test UnityPerformanceAnalyzers.sln -c Release --filter "Category!=RequiresUnity"
```

## 授權

MIT——見 [LICENSE.md](LICENSE.md)。第三方關係記載於
[`package/Third Party Notices.md`](package/Third%20Party%20Notices.md)。
