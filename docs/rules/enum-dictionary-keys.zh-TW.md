# Enum 作字典 key —— 一條已不適用的最佳化建議

> [English](enum-dictionary-keys.md) | 繁體中文

**本頁不是規則。** 它記錄一條本專案評估、實測後決定**不做成規則**的建議,
不佔用任何 `UPA` 編號。

## 那條建議

你今天仍會在 Unity 效能文章、論壇回答與 code review 檢查表上看到:

> `Dictionary<TEnum, TValue>` 每次查詢都會裝箱,因為 `EqualityComparer<TEnum>.Default`
> 對 enum 會退回 object 比較器。請自備 `IEqualityComparer<TEnum>`,或直接改用底層整數型別當 key。

它有真實的來源。Unity 2017.3 的「Understanding the managed heap」手冊頁正是這樣寫的,
還附了一個手寫比較器當修法。那一頁對應的是 Unity 當時所搭載的 .NET 3.5 等級 Mono 執行時期。

## 我們量了什麼

這是關於執行時期行為的主張,所以可以驗。本專案在 sandbox 建了量測 harness,
跑遍支援範圍內的所有組合:

| Unity | Scripting backend | API Compatibility Level | 平台 |
|---|---|---|---|
| 2022.3.62f2 | Mono | .NET Standard 2.1 | Editor |
| 2022.3.62f2 | Mono | .NET Framework | Editor |
| 6000.5.3f1 | Mono | .NET Standard 2.1 | Editor |
| 2022.3.62f2 | IL2CPP | .NET Standard 2.1 | Standalone x64 player |
| 6000.5.3f1 | IL2CPP | .NET Standard 2.1 | Standalone x64 player |

取兩種互相獨立的訊號。第一種是 `EqualityComparer<T>.Default` 背後的具體型別——
它的名稱本身就說明了行為,且不受 GC 雜訊干擾。第二種是 20 萬次 `ContainsKey` 的配置差值,
並一併回報 gen0 回收次數,讓「只是下限值」的數據自己標示出來。

五組結果一致:

| Key 型別 | `EqualityComparer<T>.Default` 解析到 | 20 萬次查詢 |
|---|---|---|
| `enum`(int 底層) | `EnumEqualityComparer<T>` | 0 bytes,0 次回收 |
| `enum`(byte 底層) | `EnumEqualityComparer<T>` | 0 bytes,0 次回收 |
| `enum`(long 底層) | `LongEnumEqualityComparer<T>` | 0 bytes,0 次回收 |
| `int`(對照組) | `GenericEqualityComparer<int>` | 0 bytes,0 次回收 |
| `enum` + 顯式 comparer | (傳入的那個 comparer) | 0 bytes,0 次回收 |

執行時期對 enum 有專用的非裝箱比較器,而且還依底層型別大小再細分。沒有東西需要修。

IL2CPP 是最值得驗的一組:它的 full generic sharing 理論上可能讓
`EqualityComparer<T>.Default` 的實體化路徑與 Mono 不同,而 player 端才是這條建議真正
會發生作用的地方。實測顯示沒有。

## 我們反而找到了什麼

同一輪量測也測了 struct key,那裡的成本是真的:

| Key 型別 | `EqualityComparer<T>.Default` 解析到 | 20 萬次查詢 |
|---|---|---|
| struct 未實作 `IEquatable<T>` | `ObjectEqualityComparer<T>` | 有配置(gen0 回收 9–20 次) |
| struct 有 `IEquatable<T>` 與 `GetHashCode` | `GenericEqualityComparer<T>` | 0 bytes,0 次回收 |

所以底層的顧慮——預設比較器可能退回裝箱路徑——是成立的。它只是**不適用於大家一直重複的
那個型別**。如果你在稽核專案裡這一類問題,該看的是 struct key,不是 enum key。

那個情形**是**規則:見 [UPA0028](UPA0028.zh-TW.md)。

## 為什麼是一頁說明,而不是一條預設關閉的規則

一條永遠不會觸發的規則會佔用規則清單的位置,還會誘使某人把它打開。
把「這條建議為何已過時」講清楚比留一個沒人該按的開關有用,
而且讓這個發現有地方安放:這種事很容易被想當然耳,卻很少有人真的去驗。

若日後某個 Unity 版本改變了這件事,產生上述數據的 harness 就在
`sandbox/UnityProject/Assets/Measurement/`,重跑它就是重啟這個問題的方式。

## 相關規則

- [UPA0028](UPA0028.zh-TW.md) —— struct 作集合 key 未實作 `IEquatable<T>`,
  這個顧慮真正有量測支撐的版本。
- [UPA0026](UPA0026.zh-TW.md) —— 在值型別接收者上呼叫繼承方法造成的裝箱,含 enum。
