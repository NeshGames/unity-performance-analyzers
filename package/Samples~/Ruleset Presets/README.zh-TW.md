# Severity Presets

> [English](README.md) | 繁體中文

Ruleset 才是 Unity 真正讀取的通道:Unity 會把 `Assets/Default.ruleset`
(以及各 asmdef 資料夾的 ruleset)傳給 C# 編譯器,而 `.editorconfig` 檔案
**完全不會**被傳入(已在 2022.3 與 Unity 6 上驗證)。這裡的 `.editorconfig`
變體只用來讓 Rider / Visual Studio 的嚴重度與你的 ruleset 保持同步。

## 挑選 preset

| Preset | 用途 |
|---|---|
| `minimal` | Unity 正確性規則(`UNT` 群組)設為 error;預設啟用的正確性規則維持其 warning。其餘全部關閉——安全的首次安裝選擇。 |
| `recommended` | 另加 UPA 效能規則設為 warning。日常使用的預設選擇。 |
| `strict` | 效能規則升為 error;那些因為對專案有所要求而預設關閉的規則——例如需要 logging 包裝類別、需要葉端類別 sealed——開始回報。 |
| `cysharp-stack` | 另加生態規則設為 error(UniTask/ZString/R3 採用)。適用於決心採用 Cysharp 技術棧的程式碼庫。 |

## 安裝

1. 從 Package Manager 視窗匯入本 sample。
2. 把選定的 preset 複製到專案中,命名為 `Assets/Default.ruleset`。
3. 選用的 per-assembly 覆寫:在任一 asmdef 資料夾內放置 `Default.ruleset`——
   它只會針對該 assembly 取代專案層級的檔案。

## WebGL 規則

`webgl-addon.ruleset` 將平台規則——threading、sockets、同步檔案 IO、
`Process`、阻塞等待——設為 warning。要疊加在任何基礎 preset 上,將它複製到你的
`Assets/Default.ruleset` 旁,並在 `<RuleSet>` 元素內加入:

```xml
<Include Path="webgl-addon.ruleset" Action="Default" />
```

接著在 **Project Settings > Player > Scripting Define Symbols** 為每個建置目標
加入 `UPA_TARGET_WEBGL`——如此規則在日常開發中就會保持啟用,而不是只在
Active Build Target 為 WebGL 時才生效。

## Editor 工具程式碼

Ruleset 無法以路徑限定範圍。把 `editor-relaxed.ruleset` 複製到每個 Editor asmdef
資料夾並改名為 `Default.ruleset`:效能規則在那裡會安靜下來,而 `UNT`
正確性規則維持 error。

## IDE 一致性(`.editorconfig` 變體)

把對應的 `.editorconfig` 複製到專案根目錄(若已有既存檔案則合併)。
它也帶有 `upa_hot_path_*` 選項——那些只有 IDE 會採納;Unity 建置一律使用
內建的 hot-path 預設值。

## 備註

- `UNT####` 嚴重度只在 Microsoft.Unity.Analyzers 存在之處生效
  (例如 Visual Studio Tools for Unity 隨附的版本);否則這些條目為惰性設定。
- 生態規則只在引用了對應套件(UniTask / ZString / R3 / DOTween)的 assembly 中執行;未引用時這些 preset 條目
  為惰性設定。其他條件式生態規則(UniTask / R3)亦同。
