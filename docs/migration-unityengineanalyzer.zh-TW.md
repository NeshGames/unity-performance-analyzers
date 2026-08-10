# 從 UnityEngineAnalyzer 過來

[English](migration-unityengineanalyzer.md)

[UnityEngineAnalyzer](https://github.com/vad710/UnityEngineAnalyzer)(`UEA####`)是第一個
把 Unity 效能慣例寫成 Roslyn analyzer 的專案,本套件涵蓋了它的大部分領域。
如果你要搬過來,這一頁說明:它的哪些規則在這裡有對應、哪些由你本來就該裝的工具涵蓋、
哪些單純沒有涵蓋。

**它的現況(查於 2026-08-10)**:MIT 授權、285 顆星、**自 2019 年 10 月 22 日起沒有新的
commit**。該 repo **並未封存**。這就是不用猜也能說的全部:以一般的意義而言它已無人維護,
但沒有人宣告它結束。

---

## 逐條對照

| UnityEngineAnalyzer | 它在找什麼 | 這裡 |
|---|---|---|
| `UEA0001` DoNotUseOnGUI | 遊戲程式碼中的 `OnGUI` | [UPA0023](rules/UPA0023.zh-TW.md)——Info,預設關閉 |
| `UEA0002` DoNotUseStringMethods | 會配置的 `string` 成員 | [UPA0030](rules/UPA0030.zh-TW.md)——封閉名單,且僅限逐幀 |
| `UEA0003` EmptyMonoBehaviourMethod | 空的 Unity 訊息 | **沒有。** 由 Microsoft.Unity.Analyzers 的 `UNT0001` 負責 |
| `UEA0004` UseCompareTag | tag 比較 | [UPA0002](rules/UPA0002.zh-TW.md) 範圍更廣——它連 `name` 與 `tag` 的讀取本身都報,不只比較。直接對應的是 `UNT0002` |
| `UEA0005` DoNotUseFindMethodsInUpdate | 逐幀的場景搜尋 | [UPA0014](rules/UPA0014.zh-TW.md) |
| `UEA0006` DoNotUseCoroutines | 協程 | [UPA2011](rules/UPA2011.zh-TW.md),但**只在組件引用了 UniTask 時**——一條叫你別再寫協程的規則,在沒有替代品的專案裡沒有用處 |
| `UEA0007` DoNotUseForEachInUpdate | 逐幀的 `foreach` | **沒有。** 當某個 `foreach` 真的會配置(在介面型別的集合上裝箱列舉器)時,[UPA0006](rules/UPA0006.zh-TW.md) 報的是**那次配置本身**,而不是這個迴圈 |
| `UEA0008` UnsealedDerivedClass | 葉類別未 sealed | **曾經有,後來廢止。** UPA1000 出貨過、在 IL2CPP 上量測過,收益無法與雜訊區分。數字見 [UPA1000](rules/UPA1000.zh-TW.md) |
| `UEA0009` InvokeFunctionMissing | `Invoke("Name")` 指向不存在的方法 | **沒有。** [UPA0016](rules/UPA0016.zh-TW.md) 涵蓋的是 `SendMessage` 家族,不含 `Invoke` |
| `UEA0010` DoNotUseStateNameInAnimator | 以字串指定 animator 狀態名 | [UPA0003](rules/UPA0003.zh-TW.md) |
| `UEA0011` DoNotUseStringPropertyNames | 以字串指定 shader 屬性名 | [UPA0003](rules/UPA0003.zh-TW.md) |
| `UEA0012` CameraMainIsSlow | `Camera.main` | [UPA0015](rules/UPA0015.zh-TW.md)——Info,因為 Unity 2020.2 之後會快取該查詢 |
| `UEA0013` UseNonAllocMethods | 非配置版本的 physics 多載 | **沒有。** 由 `UNT0028` 負責;[UPA0010](rules/UPA0010.zh-TW.md) 對同一批呼叫檢查的是另一件事——查詢範圍有沒有被限縮 |
| `UEA0014` AudioSourceMuteUsesCPU | `AudioSource.mute` | **沒有** |
| `UEA0015` InstantiateTakeParent | `Instantiate` 未指定 parent | **沒有。** Rider 有對應的檢查。[UPA0031](rules/UPA0031.zh-TW.md) 報的是逐幀路徑上的 `Instantiate`,關切點不同 |
| `UEA0016` VectorMagnitudeIsSlow | 只需比較平方卻用了 `magnitude` | [UPA0021](rules/UPA0021.zh-TW.md)——而且有 code fix |

**十六條裡有八條有直接對應。** 三條由值得並存的其他工具負責、三條在任何地方都沒有對應、
一條是刻意不做,還有一條曾經存在於此、被量測拿掉。

---

## 重疊的部分,差在哪裡

**這裡的規則多數限定在逐幀程式碼。** `UEA0002` 不管出現在哪裡都報會配置的字串方法;
UPA0030 只在逐幀的 Unity 訊息、以及你標記為熱路徑的方法裡報。報得比較少,
而報出來的是那些成本會重複的地方。

**需要套件的規則,只在套件存在時才存在。** 在沒有引用 UniTask 的組件裡,
UPA2011 不是被停用,而是**根本不存在**。沒有東西要設定,也沒有東西會看到。

**前提不再成立的規則會被移除。** UEA0008 與 UPA1000 是同一條規則。
我們這條對 IL2CPP 量過:sealed 2.70 ns、未 sealed 3.00 ns,而散布範圍是 1.28 ns,
順序還反轉過一次——於是它被廢止,而不是留著。
什麼時候會發生這種事,見[版本與規則治理](versioning.zh-TW.md)。

---

## 搬過來的步驟

1. 移除 UnityEngineAnalyzer 的套件或 DLL。兩者並存,那八條重疊的規則會給你兩份報告。
2. 安裝本套件並選一個 preset——沒有 preset 的話,只有預設開啟的規則會以 Warning 回報。
3. 若還沒裝,請裝
   [Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers)。
   它涵蓋 `UEA0003` 與 `UEA0013`,而且它的 23 個診斷抑制器會阻止一般 C# analyzer 對
   Unity 程式碼產生無意義的回報。本套件不複製其中任何一項。
4. 若在既有專案上報告量很大,請先凍結,而不是先全部修完:

   ```bash
   upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --write-baseline upa-baseline.json
   ```

5. `#pragma warning disable UEA####` 在這裡不起任何作用——編號不同。
   請搜出它們,用上表翻譯,或直接刪掉再看看報什麼。

抑制註解**不會**提供自動轉換,將來也不會:`#pragma` 裡的編號是某個人針對某一行做的決定,
而把它映射到兩條範圍不同的規則之間,會讓那兩條規則都沒被問到的程式碼被靜音。
