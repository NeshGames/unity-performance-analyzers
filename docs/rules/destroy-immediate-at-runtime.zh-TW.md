# 執行期的 `DestroyImmediate` —— 為什麼沒有為它做規則

> [English](destroy-immediate-at-runtime.md) | 繁體中文

**本頁不是規則。** 它記錄一個本專案評估過、確認為真、但決定不做成規則的問題,以及理由。
未配置 `UPA` 編號。

## 問題是真的

Unity 官方文件的措辭很直接:

> 遊戲中永遠不要對任何物件呼叫 `DestroyImmediate`,請改用 `Object.Destroy`。

`Destroy` 標記物件,等當前的 update 迴圈結束後才拆除;`DestroyImmediate` 就地拆,
在當下正在跑的東西中間。由此有兩個後果:

- 一邊走訪物件集合一邊從中銷毀——清理程式碼很常見的形狀——會在自己腳下改動集合
- 在編輯器中若傳入的是資產,它銷毀的是**資產本身**而非場景實例。那正是它存在的用途,
  而那種破壞性是 `Destroy` 沒有的

[Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers)
沒有涵蓋它。`UNT0030` 聽起來很近——「對 `Transform` 呼叫 `Destroy` 或 `DestroyImmediate`」
——但它管的是引數,不是管呼叫了哪個方法。四十三條 `UNT` 規則裡沒有其他候選。

## 為什麼這裡不做成規則

編輯器程式碼**正確而且經常**使用 `DestroyImmediate`。以組件名收窄能處理其中明顯的一半
——`UPA0023` 與 `UPA0025` 已經以組件名判定「這是不是編輯器程式碼」,理由見各自的頁面。
處理不了的是另一半:

```csharp
public class Spawner : MonoBehaviour     // Assembly-CSharp —— 玩家組件
{
#if UNITY_EDITOR
    void Reset()
    {
        DestroyImmediate(stalePreview);   // 正確,而且常見
    }
#endif
}
```

這是玩家組件,組件名的檢查排除不了它。而 analyzer 執行的時機——在編輯器裡,
也就是任何人看到它輸出的地方——**`UNITY_EDITOR` 是有定義的**,
所以那段被保護的程式碼是活的、會被剖析、會被回報。

偵測不是做不到:Roslyn 會把 directive trivia 留在語法樹裡,規則可以問「這個節點是不是
位於活躍的 `#if UNITY_EDITOR` 區間內」。但本專案目前**沒有任何規則以這種方式判定**,
那套機制會是全新的——新的程式碼,也是規則出錯的新方式。

相對地:執行期呼叫 `DestroyImmediate` 並不是常見的錯誤。它是人們**刻意**選用的方法,
通常是在讀過它的行為之後。一條在專案第一次建置時就報在正確編輯器程式碼上的規則,
會被關掉;而被關掉的規則價值是零。本專案的 1.0 判準是噪音,不是規則數。

## 那該怎麼做

- 執行期程式碼用 `Object.Destroy`。若某個物件非得在下一行之前消失不可,
  那通常代表程式該重構,而不是該同步銷毀
- 編輯器程式碼用 `DestroyImmediate` 是對的。記得對 prefab 或資產呼叫時銷毀的是資產,
  且只有在你主動接上 undo 系統時該變更才可復原
- 若你想要機械化檢查,專案自有的 analyzer 或審查清單可以用**你自己的程式碼庫允許的窄假設**
  去做——而那正是一條要出貨的規則沒有的自由

## 相關

- [UPA0031](UPA0031.zh-TW.md) 報告逐幀路徑上的 `Instantiate` 與 `Destroy`。
  它刻意排除 `DestroyImmediate`:它的兩個訊息都指向物件池,而這裡的答案不是池化
