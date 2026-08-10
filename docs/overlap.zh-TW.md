# 與其他 Unity 分析工具的規則重疊

> [English](overlap.md) | 繁體中文

多數 Unity 專案本來就跑著至少一種其他分析工具。這份文件逐條記錄:同一件事還有誰在報,
以及該怎麼處理。

結論放在最前面,因為它會改變後面那張表的意思。

---

## 先理解這件事

**Rider 的效能檢查與本套件不是同一種輸出。**

Rider 的 Unity 外掛會把 `Update`、`LateUpdate`、`FixedUpdate` 與協程標記為
*performance-critical context*,再以側邊欄記號與高亮標出其中昂貴的操作。
JetBrains 明講那些**不是 warning 也不是 suggestion**——程式碼沒有錯,只是在做一件已知昂貴的事,
而且通常沒有機械式的修法。它們的用途是製造意識。

本套件產出的是**帶嚴重度的編譯器診斷**。它們出現在 Unity Console、可以設成 error、
而且 `upa-cli` 可以據此讓建置失敗。

所以「Rider 已經涵蓋 UPA0001」這句話,對*資訊*而言是真的,對*強制力*而言是假的。
要決定關掉什麼,必須先把這兩件事分開——那正是建議欄在做的事。

**實務結論:與 Rider 的重疊,多數該在 IDE 解決,而不是在 ruleset 解決。**

Unity 讀 `.ruleset`、也會把 additional file 傳給編譯器,但它**不把 `.editorconfig` 傳給編譯器**。
這是量出來的,不是假設:在 Unity 2022.3 與 Unity 6 上,Unity 交給 `csc` 的回應檔都帶
`-ruleset:` 而沒有 `-analyzerconfig:`,只靠 `.editorconfig` 啟用的規則在批次建置中不會回報。
同一次量測也確認了 asmdef 資料夾內的 ruleset 優先於 `Assets/` 的那份。

這個不對稱在這裡很有用:

```
Assets/Default.ruleset      → Unity 編譯 + upa-cli + IDE
.editorconfig               → 只到 IDE
```

於是你可以**在 Rider 已經標示的地方把規則靜音**——也就是重複噪音真正發生的地方——
同時讓它在 Unity 建置中繼續回報、在 CI 中繼續可強制:

```ini
# .editorconfig —— 只作用於 IDE。這些 Rider 已經標了。
[*.cs]
dotnet_diagnostic.UPA0001.severity = none
dotnet_diagnostic.UPA0014.severity = none
dotnet_diagnostic.UPA0015.severity = none
```

`Assets/Default.ruleset` 不要動。你少掉重複的波浪線,關卡還留著。

本頁最後那組 `*-coexist.ruleset` 是給「寧可整個讓渡」的團隊用的;
上面這條 `.editorconfig` 路線才是建議的預設。

---

## 對照的工具

| 工具 | 分析型態 | 執行位置 | 能讓建置失敗嗎? |
|---|---|---|---|
| **Rider / ReSharper**(`resharper-unity`) | 近 Roslyn,含呼叫圖傳播 | IDE | 不能——指示器沒有嚴重度 |
| **Microsoft.Unity.Analyzers**(`UNT####`、`USP####`) | Roslyn analyzer + 診斷抑制器 | IDE + Unity 編譯 | 能,若在 ruleset 中評級 |
| **Project Auditor**(`PAC####`、`PAS####`) | 全專案稽核;程式碼分析跑在 player 組件上,另有一組 Roslyn analyzer 出貨在獨立的 rules 套件裡 | Unity Editor——自 Unity 6.4 起內建 | 由 Editor 執行觸發,含批次模式 |
| **套件自帶**(`UniTask.Analyzer` 等) | 函式庫自己出貨的 Roslyn analyzer | IDE + Unity 編譯 | 能 |
| **unity-performance-analyzers**(`UPA####`) | Roslyn analyzer,依套件與平台條件啟用 | IDE + Unity 編譯 + `upa-cli` | 能,且可在 CI 離線執行 |

兩點結構性差異:

- **Rider 會傳播,我們不會。** Rider 會把「呼叫了昂貴東西」的方法也標成昂貴,
  一路回推到 `Update` 根部,連委派都追。本套件不做流程分析,只看熱路徑方法本身與其 lambda。
  下表標成 *Rider 更強* 的都是這個原因。
- **Project Auditor 是不同的節奏。** 它在一次 Editor 執行中稽核整個專案,
  把落在 `MonoBehaviour.Update` 這類熱路徑內的發現標為 Critical,並允許逐項 Mute。
  它是週期性稽核,不是逐次按鍵或逐個 PR 的檢查。它與本套件的重疊是真的,
  但很少造成重複*噪音*,因為你不會同時看著兩邊。
  下一節說明那條線在哪,以及它可能往哪裡移動。

---

## Project Auditor 與本套件

Project Auditor 是 Unity 自家的分析工具,也是關於本套件最常被問到的那一個。
一句話:**裝它,並預期它回答的是不同的問題。**

**怎麼取得它(2026-08-10 查證)。** Unity 6.4 以後,Project Auditor 已內建於編輯器
——*Window ▸ Analysis ▸ Project Auditor*,不必安裝套件。6.4 之前則是 Package Manager
安裝(`com.unity.project-auditor`)。兩種情況都還需要另一個
**Project Auditor Rules** 套件(`com.unity.project-auditor-rules`),規則本身現在住在那裡。

這個拆分是最近才發生、而且是刻意的:rules 套件自己的 changelog 記著,規則**與它的
Roslyn analyzer** 被移出主套件,理由是「隨著我們把這個工具作為模組併入 Unity 編輯器」。

**各自負責什麼。**

| | Project Auditor | 本套件 |
|---|---|---|
| 範圍 | 整個專案:資產匯入設定、專案設定、shader、build report——以及程式碼 | C# 原始碼,一次一個組件 |
| 程式碼分析 | 跑在 player 組件上,每個發現附反向呼叫階層 | 語法與語意,逐檔,無呼叫圖 |
| 什麼時候 | 你去跑一次稽核 | 每次編譯,以及 IDE 裡的每次按鍵 |
| 沒有 Editor 時 | 不行 | 可以——CI 上的 `upa-cli`,不需授權 |
| 能讓建置失敗 | 由你自己驅動的 Editor 執行 | 能,透過 ruleset |
| 依引用套件調整 | 不會 | 會——UniTask、ZString、R3、DOTween |

左欄裡凡是與程式碼無關的,本套件永遠不會做;而那個反向呼叫階層本身就值得跑一次稽核。
右欄講的則是**答案何時抵達**:在 pull request 上收到的發現,比在季度稽核裡找到的同一個發現便宜。

**這裡尚未確立的事。** 既然那些 Roslyn analyzer 現在出貨在自己的套件裡,它們原則上
**可能**在 Unity 自己的編譯期回報,而不是只在稽核裡出現——那正是本套件使用的同一條通道。
Unity 的文件兩邊都沒說,本專案也沒有實測過。若結果是「會」,上面那條「不同的節奏」
對程式碼類規則就會變弱,重複噪音的問題也會從理論變成實際。這一列請當作未結案。

**不要把舊 repo 當成現況。** GitHub 上的 `Unity-Technologies/ProjectAuditor` 現在自述
已過期且不再支援,並要人改用內建版本,所以它的規則清單不是今天出貨內容的描述。

---

## 信心標記

| 標記 | 意義 |
|---|---|
| ● | 已確認:同一個構造、同一個觸發條件 |
| ◐ | 部分:相鄰的關切點,觸發條件或範圍不同 |
| ○ | 找不到對等物 |
| ? | 相信有重疊,**尚未對實機安裝驗證**——見「維護」 |

---

## 效能規則(UPA0001–UPA0031)

| UPA | 回報什麼 | Rider | UNT | Project Auditor | 套件自帶 | 建議 |
|---|---|---|---|---|---|---|
| **UPA0001** | 逐幀方法內的 `GetComponent` 家族 | ● *Avoid usage of GetComponent methods in performance critical context* | ◐ UNT0026、◐ UNT0039 | ? PAC——API 資料庫含 `GetComponent` | ○ | Rider 更強(會跨呼叫傳播)。用 Rider 就在 `.editorconfig` 靜音;**ruleset 裡保留**——這是最值得設成關卡的一條 |
| **UPA0002** | 逐幀方法內存取 `name` / `tag` | ◐ *Use CompareTag instead of explicit string comparison*——較窄 | ◐ UNT0002 *Inefficient tag comparison*——較窄 | ? PAC | ○ | 保留。兩個替代方案都只涵蓋「比較」那個形狀;`name` 存取與單純讀 `tag` 兩者都不管 |
| **UPA0003** | 以字串存取 shader / animator 屬性 | ● *Avoid using string based names…* | ● UNT0041(重複呼叫才建議 `StringToHash`) | ? PAC | ○ | 三方真重疊。若你的 ruleset 已評級 UNT0041,可考慮全專案 `UPA0003 = none`;否則保留——UNT0041 需要重複,UPA0003 不需要 |
| **UPA0004** | 逐幀方法內的實體化存取子(`Renderer.material` 等) | ○ | ○ | ? PAC(材質實體化是已知描述子) | ○ | **保留。** 具辨識度的規則;重點是洩漏,不只是成本 |
| **UPA0005** | 直接呼叫 `Debug.Log`(預設關閉) | ● *Avoid usage of Debug.Log methods in performance critical context*——僅限熱路徑 | ○ | ◐ | ○ | 範圍不同:UPA0005 不限熱路徑。反正預設關閉,要開就是刻意開 |
| **UPA0006** | 逐幀方法內的參考型別配置 / boxing | ○ | ○ | ● PAC 的 boxing / 物件配置診斷 | ○ | **保留。** Project Auditor 這塊做得好,但只在批次 Editor 執行時。這是逐 PR 的版本 |
| **UPA0007** | 逐幀方法內的捕捉型 lambda | ○ | ○ | ◐ | ○ | **保留。** ReSharper 的 Heap Allocations Viewer 是另外的選配外掛,不屬於 Unity 支援 |
| **UPA0008** | 迴圈內的 `stackalloc` | ○ | ○ | ○ | ○ | **保留。** 任何地方都沒有對等物 |
| **UPA0009** | `List<T>.Count` 未外提(預設關閉) | ○ | ○ | ○ | ○ | 維持現狀(預設關閉) |
| **UPA0010** | 未給 `maxDistance` / `layerMask` 的 raycast | ◐ *Avoid using allocating versions of Physics Raycast functions*——不同關切點(配置) | ◐ UNT0028 *Use non-allocating physics APIs*——不同關切點 | ? PAC | ○ | **保留。** 沒有別的東西在檢查引數形狀;規則頁註記 UNT0028 涵蓋相鄰的配置問題 |
| **UPA0011** | 以 `SetActive` 切換 UI 顯示(預設關閉) | ○ | ○ | ○ | ○ | 維持現狀 |
| **UPA0012** | TMP 指派 `text` 而非 `SetText`(預設關閉) | ○ | ○ | ○ | ○ | 維持現狀 |
| **UPA0013** | 逐幀方法內的 `System.Linq`(預設關閉) | ○ | ○ | ◐ | ○ | 維持現狀。UnityEngineAnalyzer 沒有 LINQ 規則——`UEA0009` 是 InvokeFunctionMissing,本頁在 2026-08-10 真的去讀它的規則清單之前寫錯了 |
| **UPA0014** | 逐幀方法內的場景搜尋 API | ● *Avoid usage of Find methods in performance critical context*——同一組 API,還附快速修正 | ○ | ? PAC | ○ | Rider 更強且有修正。用 Rider 就在 `.editorconfig` 靜音;ruleset 保留給 CI |
| **UPA0015** | 逐幀方法內的 `Camera.main`(Info) | ● *Camera.main is inefficient…*——附「快取到 `Awake`」的 context action | ○ | ? PAC | ○ | 本來就是 Info,噪音低。用 Rider 就在 `.editorconfig` 靜音 |
| **UPA0016** | `SendMessage` / `BroadcastMessage` | ● *Avoid using string based Method Invocation* | ○ | ? PAC | ○ | 用 Rider 就在 `.editorconfig` 靜音。Unity / CI 保留——這條值得設成 error |
| **UPA0017** | 回傳陣列的 `GetComponents` 多載 | ◐ | ◐ UNT0026 | ? PAC | ○ | **保留。** 「改用 `List<T>` 多載」的建議比兩者都具體 |
| **UPA0018** | 會配置的、回傳陣列的 Unity API | ○ | ◐ UNT0042(`Mesh` 陣列屬性在迴圈內)——單一 API、限迴圈 | ● PAC API 資料庫 | ○ | **保留。** UNT0042 是本規則的其中一例;規則頁補交叉引用 |
| **UPA0019** | 協程 yield 出實質型別 | ○ | ○ | ○ | ○ | **保留——旗艦規則。** 沒有別的東西抓得到,而且失敗形式(Unity 把裝箱值當成 `null`)是正確性 bug,不只是配置 |
| **UPA0020** | `WaitUntil` / `WaitWhile` 內的 lambda(預設關閉) | ○ | ◐ UNT0038 *Cache `WaitForSeconds`*——兄弟關切點、不同 API | ○ | ○ | 維持現狀。規則頁交叉引用 UNT0038 |
| **UPA0021** | 可用 `sqrMagnitude` 的 `magnitude` / `Distance` 比較 | ○ | ◐ UNT0024 *Prefer scalar over vector calculations* | ○ | ○ | **保留。** UNT0024 是不同的改寫。我們這條有 code fix |
| **UPA0022** | `Enum.HasFlag`(已廢止) | — | — | — | — | 已廢止;不納入任何 coexistence ruleset |
| **UPA0023** | player 程式碼中的 `OnGUI`(Info,預設關閉) | ◐ *base.OnGUI() will print "no GUI implemented"*——不同問題 | ○ | ○ | ○ | 維持現狀 |
| **UPA0024** | 逐幀方法內的 `Resources.Load`(預設關閉) | ○ | ○ | ? PAC | ○ | 維持現狀 |
| **UPA0025** | 執行期程式碼中的完成項 | ○ | ○ | ○ | ◐ 一般 C# analyzer(CA1821 只涵蓋*空的*完成項) | **保留。** CA1821 是更窄的情況 |
| **UPA0026** | 對實質型別呼叫繼承來的 `GetType()` 造成裝箱 | ○ | ○ | ● PAC boxing | ○ | **保留。** 我們這條有 code fix,而且逐次編譯就跑 |
| **UPA0027** | 以展開形式呼叫 `params` 多載 | ○ | ○ | ● PAC 有 params 陣列配置的診斷 | ○ | **保留。** 同樣的發現,不同的節奏 |
| **UPA0028** | 未實作 `IEquatable<T>` 的 struct 當集合鍵 | ○ | ○ | ◐ | ○ | **保留——旗艦規則。** 有量測支撐 |
| **UPA0029** | 可用 `AddRange` 取代的複製迴圈 | ○ | ○ | ○ | ○ | **保留** |
| **UPA0030** | 逐幀方法內已知會配置的 `string` / `Enum` 成員 | ○ | ○ | ● PAC API 資料庫 | ○ | **保留** |
| **UPA0031** | 逐幀方法內的 `Instantiate` / `Destroy` | ◐ *Avoid usage of AddComponent…*(兄弟 API)、◐ *Avoid `Object.Instantiate` without Transform Parent*(不同關切點) | ○ | ? PAC | ○ | **保留。** 兩條 Rider 檢查都不是這條規則 |

---

## 正確性規則(UPA1000–UPA1001)

| UPA | 回報什麼 | Rider | UNT | 其他 | 建議 |
|---|---|---|---|---|---|
| **UPA1000** | 葉類別未 sealed(已廢止) | — | — | UnityEngineAnalyzer 有 `UnsealedDerivedClass` | 量測後廢止;不納入 coexistence ruleset |
| **UPA1001** | enum switch 缺少已宣告成員 | ○ | ○ | ● Roslyn 內建的 **IDE0010** / **IDE0072**(*Add missing cases*) | **真的重疊,而且對手不是 Unity 工具。** 若你的專案已評級 IDE0010/IDE0072,設 `UPA1001 = none`。差異:我們這條吃 `upa_enum_switch_allow_default`,而且不像 IDE0010 只在 IDE 出現——它經由 Unity 的編譯器回報 |

---

## 生態規則(UPA2000–UPA2032)

全部預設關閉且依套件條件啟用,所以重疊只在「你同時引用了該套件**並且**啟用了規則」時才成立。

| UPA | 回報什麼 | 套件自帶的對等物 | 建議 |
|---|---|---|---|
| **UPA2000** | 逐幀方法內的字串組建(知道 ZString) | ○ | **保留。** 有 code fix |
| **UPA2010** | `async Task` 方法(已引用 UniTask) | ○ | **保留。** 設計上就是有主張的 |
| **UPA2011** | MonoBehaviour 上的協程 `IEnumerator`(已引用 UniTask) | ○ | **保留。** 設計上就是有主張的 |
| **UPA2012** | `async void` / 被丟棄的 task 呼叫 | ● **`UniTask.Analyzer`** 隨 UniTask 出貨,偵測未 await 的 `UniTask` 回傳呼叫。另有 ◐ **CS4014**、◐ UNT0012 | **全套規則裡最明確的真重複。** 引用 UniTask 就已經有它的 analyzer,同一個問題會得到兩份診斷。**UniTask 存在時建議 `UPA2012 = none`**,除非你就是要那個 `.Forget()` code fix——那是 `UniTask.Analyzer` 沒有的。見下方 `unitask-coexist.ruleset` |
| **UPA2021** | 以公開 `Action` 事件表達可觀察狀態(已引用 R3) | ○ | **保留。** 架構性的,不是機械性的 |
| **UPA2030** | 逐幀方法內建立 tween(DOTween) | ○ | **保留** |
| **UPA2031** | 丟棄無限 tween 而未 `SetLink` | ○ | **保留——旗艦規則。** 這是生命週期 bug,不是風格偏好,而且 DOTween 沒有出貨 analyzer |
| **UPA2032** | 字串型 tween ID(Info) | ○ | **保留** |

---

## 平台規則(UPA3000–UPA3004)

| UPA | 回報什麼 | 還有別的嗎? |
|---|---|---|
| **UPA3000** | WebGL 不支援的執行緒 API | ○ |
| **UPA3001** | WebGL 不支援的 `System.Net.Sockets` | ○ |
| **UPA3002** | WebGL 不支援的同步檔案 IO | ○ |
| **UPA3003** | WebGL 不支援的 `System.Diagnostics.Process` | ○ |
| **UPA3004** | 對非同步作業的阻塞等待——在單執行緒的 WebGL 上會死結 | ○ |

**與任何人的任何東西都沒有重疊。** 沒有任何 Unity 分析工具會依建置目標平台切換規則。
Project Auditor 的分析會收一個目標平台,但不帶這組規則;
Rider 與 Microsoft.Unity.Analyzers 都與平台無關。

以 WebGL 為目標時,絕對不要關掉這些。UPA3004 抓的是**當機/卡死**而不是變慢,
而且那個失敗只在瀏覽器建置裡出現。

---

## 其他工具抓得到、而我們抓不到的

推薦本套件,就等於推薦它周圍的那些工具。以下是真實的缺口,列出來免得有人用痛的方式發現。

**Rider**
- 對 `UnityEngine.Object` 子類別做 null 比較(每次比較都有一次原生呼叫)
- 可能意外繞過引擎物件的生命週期檢查
- 多維陣列存取效率
- 乘法順序(`float * Vector3` 對 `Vector3 * float`)
- 冗餘的 Unity 事件函式(空的訊息本體)
- 冗餘的 `SerializeField` / `HideInInspector` / `InitializeOnLoad` / `FormerlySerializedAs`
- `Object.Instantiate` 未給 parent 後接 `SetParent`
- shader keyword 啟用
- **以上全部都有呼叫圖傳播**——這是能力差距,不是規則差距

**Microsoft.Unity.Analyzers**
- 廣泛的正確性與型別安全:訊息簽章(UNT0006、UNT0033)、`InitializeOnLoad`(UNT0009、UNT0015)、
  `SerializeField` 有效性(UNT0013)、非靜態上的 `MenuItem`(UNT0020)、
  對 `Transform` 呼叫 `Destroy`(UNT0030)、條件編譯拼字(UNT0043)
- Unity 物件上的 null 合併 / 傳播 / 模式比對(UNT0007、UNT0008、UNT0023、UNT0029)
- Transform 位置與旋轉的取得/設定效率(UNT0022、UNT0032、UNT0036、UNT0037)
- 空的 Unity 訊息(UNT0001)、`SetPixels`(UNT0017)、熱訊息中的反射(UNT0018)
- **23 個診斷抑制器**(`USP0001`–`USP0023`),阻止一般 C# analyzer 對 Unity 程式碼產生無意義的回報
  ——序列化欄位被當成未使用、訊息被當成可移除,諸如此類。
  **本套件不複製其中任何一項,而你不該在沒有它的情況下寫 Unity 程式碼。**

**Project Auditor**
- 程式碼以外的一切:資產匯入設定、專案設定、shader、build report
- 程式碼分析跑在 player 組件上,看的是編譯結果而不是語法樹
- 每個發現都有反向呼叫階層

三個都裝。本套件的設計是**並存**,不是取代。

---

## Coexistence ruleset

已隨 **Ruleset Presets** sample 出貨,每組兩個檔:一個 `.ruleset`(所有地方都讓渡)
與一個同名 `.editorconfig`(只在 IDE 讓渡)。基於本頁開頭的理由,**先考慮 `.editorconfig` 那個**。

**方向與直覺相反,而且這件事很重要。** 每個 coexistence ruleset 是**去 include 基礎 preset**,
而不是被 preset include——因為**包含者的規則條目會蓋過被包含檔案裡同一條**,
而每個基礎 preset 都對每條規則評級。一個被 preset include 的檔案什麼都靜不掉,
外觀卻完全正確。這是出貨前用 `upa-cli` 量出來的:基礎把 UPA0001 評為 Warning
並 include 一個把它設成 `None` 的覆蓋檔,UPA0001 仍然回報;把兩者對調就靜音了。

所以做法是:把 coexistence 檔**與它的基礎 preset**一起複製到 `Assets/`,
再把 coexistence 檔改名為 `Default.ruleset`。要換基礎,只需改一行 `Include`。

### `rider-coexist.ruleset` —— include `recommended`

設為 `None`:**UPA0005、UPA0014、UPA0015、UPA0016**。

在 `recommended` 基礎下,UPA0005 那條是無作用的(該 preset 本來就把它設為 `none`);
之所以還列著,是為了讓你把 `Include` 換成 `strict` 或 `cysharp-stack` 時它依然生效。

刻意**不**納入:UPA0001、UPA0002、UPA0003。Rider 對這三條的涵蓋範圍都比對應的 UPA 規則窄
(見上表),而 UPA0001 是最值得設成 CI 關卡的一條。

> 請優先考慮本頁開頭的 `.editorconfig` 路線。這個 ruleset 連 Unity 與 `upa-cli` 也一併關掉,
> 等於讓「一個無法讓建置失敗的工具」成為這幾條的唯一涵蓋。確定可以接受再用。

### `vs-coexist.ruleset` —— include `recommended`

設為 `None`:**UPA0003**(讓渡給 UNT0041)。

刻意很小。Microsoft.Unity.Analyzers 主要是正確性規則與抑制器,真正的效能重疊只有一條。

### `unitask-coexist.ruleset` —— include `cysharp-stack`

設為 `None`:**UPA2012**(讓渡給 `UniTask.Analyzer`)。

這一個 include 的是 `cysharp-stack` 而不是 `recommended`,因為那是唯一會把 UPA2012 打開的 preset
——在其他任何基礎上,這個檔案都只是在靜音一件本來就靜著的事。

靜音它同時也放棄了 `.Forget()` code fix,而 `UniTask.Analyzer` 沒有提供那個。

---

## 維護

這份文件是對**別人的軟體**做出的主張,所以它需要和本 repo 其他主張一樣的對待。

**待驗證項目**

- [ ] 表中每個 `?`:對實機的 Project Auditor 安裝確認(Unity 6.4+,含
      `com.unity.project-auditor-rules`),並記錄實際的 `PAC####` 編號,而不是描述
- [ ] rules 套件裡的 Roslyn analyzer 究竟會不會在 Unity 自己的編譯期回報,
      還是只在稽核中出現。文件兩邊都沒說,而它決定了上面那個節奏論點對程式碼類規則是否仍成立
- [x] 「Project Auditor 自 Unity 6.4 起內建」以及「`com.unity.project-auditor-rules`
      是這個工具現在真的需要的套件」:已於 2026-08-10 對 Unity 的套件文件與 rules 套件
      changelog 查證。這兩句在有人去看之前就已經寫在本頁上了
- [ ] 確認 `UniTask.Analyzer` 針對 UPA2012 的診斷編號與確切觸發條件
- [ ] 確認 IDE0010 / IDE0072 是否會在 Unity 的編譯器下觸發,或僅限 IDE
      ——這決定了「建議關掉 UPA1001」該說得多強
- [ ] 每個 Rider 大版本重新核對其檢查清單——JetBrains 會定期新增
- [x] `.editorconfig` 只到 IDE 的不對稱:已在 2022.3 與 Unity 6 實測。
      新的 Unity 大版本出來時要重驗,因為本頁開頭那整段建議都建立在它上面
- [x] coexistence ruleset 的 include 方向:已用本 repo 的 CLI 實測,並由測試守住

**維護方式**

- 每次發佈都複查;Rider 與 Microsoft.Unity.Analyzers 一年都出貨數次
- 新增 UPA 規則時,這裡要補一列。**由測試強制**:任一語言缺列即建置失敗
- 兩種語言以相同的逐規則涵蓋度受檢

**出處**

- Microsoft.Unity.Analyzers 規則與抑制器索引:`microsoft/Microsoft.Unity.Analyzers`,`doc/index.md`
- Rider 檢查:`JetBrains/resharper-unity` wiki,*Performance critical context and costly methods* 及其連結頁
- Project Auditor:`com.unity.project-auditor` 套件手冊(內建於 Unity 6.4 這句出自該處),
  以及 `com.unity.project-auditor-rules` 的 changelog(什麼被搬進去、為什麼,出自該處)
