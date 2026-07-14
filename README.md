# FontReplacerMod

這是一款為 **Schedule I** 開發的 [MelonLoader](https://www.google.com/search?q=https://github.com/LavaGang/MelonLoader) 模組（Mod），可將你的自定義字體追加為備用字體（Fallback），讓遊戲在遇到缺字時自動動態生成所需的字形。

本來想採用直接替換字體的方式，但在實際測試發現會導致部分遊戲文字會被物體遮住。加上我不想去製作字體，最終改為採用將自定義字體設為備用字體中。

## 功能特點

* 將自定義字體注入到所有 TextMeshPro UI 和場景文本的備用字體清單（Fallback Fonts）中
* 動態字形圖集（Dynamic glyph atlas）—— 透過備用機制自動生成中日韓原版缺少的字元
* 保持原版遊戲字體風格，補全缺字
* 在每個場景載入時自動套用與綁定

## 運行需求

* [Schedule I](https://www.google.com/search?q=https://store.steampowered.com/app/3164500/Schedule_I/) (Steam)
* [MelonLoader](https://www.google.com/search?q=https://github.com/LavaGang/MelonLoader) v0.6+
* .NET 6.0 SDK (僅從原始碼編譯時需要)

## 安裝說明

### 使用已發佈版本

1. 從 [Releases](https://www.google.com/search?q=../../releases) 下載 `FontReplacerMod.dll`。
2. 將其放入 `<Schedule I>/Mods/` 資料夾中。
3. 將你的 `.ttf` 或 `.otf` 字體檔案放入 `<Schedule I>/UserData/FontReplacer/` 資料夾中。
4. 啟動遊戲。

### 從原始碼編譯

將此專案 Clone 或複製到你的 Schedule I 遊戲目錄中：

```
<Schedule I>/FontReplacerMod/

```

然後進行編譯：

```bash
dotnet build -c Release

```

編譯系統會透過登錄檔（Registry）、Steam 預設路徑以及常見的磁碟機代號自動偵測遊戲路徑。如果偵測失敗，請手動指定路徑：

```bash
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Schedule I\"

```

編譯完成的 DLL 檔案會直接輸出至 `<Schedule I>/Mods/`。

## 使用方法

1. 在安裝好 MelonLoader 的情況下啟動一次遊戲（以生成代理組件 Proxy Assemblies）。
2. 將你的 `.ttf` 或 `.otf` 字體檔案放入 `<Schedule I>/UserData/FontReplacer/` 資料夾中。
3. 啟動遊戲 —— 模組會自動將該字體載入並設為全域備用字體。當遊戲遇到原版字體不支援的字元時，會自動由你的字體進行動態渲染。

你可以查看 MelonLoader 主控台（Console）以確認狀態訊息。

## 授權條款

MIT