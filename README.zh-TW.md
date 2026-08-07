# unity-performance-analyzers

> [English](README.md) | 繁體中文

把 Unity 效能與正確性慣例變成編譯期檢查的 Roslyn analyzer 集合。規則會依每個
assembly 引用的套件(UniTask、ZString、R3、DOTween)以及專案是否以 WebGL 為目標
**自動調整**。

以 UPM package 形式發佈。支援 **Unity 2022.3 LTS ~ Unity 6**。

> **狀態:pre-1.0。** 全部 41 條規則已實作,並在 Unity 2022.3 與 Unity 6 的
> sandbox 建置實測通過。rule ID 一經發佈即穩定,永不重用。

## 安裝

Package Manager > *Add package from git URL…*:

```
https://github.com/NeshGames/unity-performance-analyzers.git?path=/package#v0.2.0
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
- 各 preset 的 `.editorconfig` 對照版,供 Rider / Visual Studio 同步嚴重度

Unity 只讀 ruleset——它不會把 `.editorconfig` 傳給編譯器(已於 2022.3 與 Unity 6
實測確認)。asmdef 資料夾內的 `Default.ruleset` 會覆寫全專案那份,只影響該 assembly。

匯入 **Smoke Test** sample 可驗證 analyzer 已載入:它刻意違反多條規則,
Console 應立即亮起警告。

## Rule Manager 視窗

**Tools ▸ Unity Performance Analyzers ▸ Rule Manager** 讓你不必手改 XML 就能管理上述一切:

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
upa_hot_path_attributes = HotPath,PerfCritical
upa_hot_path_include_lambdas = true
upa_enum_switch_allow_default = true
```

解析刻意容錯:`#` 註解、未知 key 與格式錯誤的行一律忽略,無效值落到下一層通道,
重複 key 以最後一筆為準。Rule Manager 的 Options 頁籤會替你編輯這個檔案。

## 規則

每條規則的完整文件:[`docs/rules/`](docs/rules/)。

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
| [UPA0018](docs/rules/UPA0018.zh-TW.md) | 配置陣列的回傳型 API(`Input.touches`、`Animator.parameters`、`Renderer.sharedMaterials`、`Camera.allCameras`) | ✓ |
| [UPA0019](docs/rules/UPA0019.zh-TW.md) | coroutine 內 yield 實值型別(裝箱;Unity 視同 `null`) | |
| [UPA0020](docs/rules/UPA0020.zh-TW.md) | `WaitUntil` / `WaitWhile` 建構時傳入 lambda *(預設關閉)* | |
| [UPA0021](docs/rules/UPA0021.zh-TW.md) | 可用 `sqrMagnitude` 取代的 `magnitude` / `Distance` 比較 | |
| [UPA0022](docs/rules/UPA0022.zh-TW.md) | 逐幀方法內的 `Enum.HasFlag`(Unity Mono 上會裝箱) | ✓ |
| [UPA0023](docs/rules/UPA0023.zh-TW.md) | player 程式碼中宣告 `OnGUI` *(Info,預設關閉)* | |
| [UPA0024](docs/rules/UPA0024.zh-TW.md) | 逐幀方法內的 `Resources.Load` *(預設關閉)* | ✓ |
| [UPA0025](docs/rules/UPA0025.zh-TW.md) | runtime 程式碼中宣告 finalizer | |
| [UPA0026](docs/rules/UPA0026.zh-TW.md) | 實值型別呼叫繼承的 `ToString` / `GetHashCode` / `Equals(object)` / `GetType` 造成裝箱 | ✓ |

### 正確性

| ID | 回報內容 |
|---|---|
| [UPA1000](docs/rules/UPA1000.zh-TW.md) | 葉端類別未 `sealed` *(預設關閉)* |
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

## Repository 結構

| 路徑 | 用途 |
|---|---|
| `src/UnityPerformanceAnalyzers/` | analyzer 組件(netstandard2.0,Roslyn 3.8) |
| `src/UnityPerformanceAnalyzers.CodeFixes/` | IDE 專用 code fix |
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
