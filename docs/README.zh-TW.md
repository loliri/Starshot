<div align="center">

<img src="../src/logo.png" width="120" alt="Starshot Logo">

# Starshot

**下一世代 Windows 原生 HDR 截圖工具**

16bit 全鏈路擷取 · 區域截圖 · AVIF / JPEG XL 編碼 · 色彩管理

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../releases)

[下載](../../releases) · [快速上手](#快速上手) · [功能詳解](#功能詳解) · [從原始碼構建](#從原始碼構建)

**[简体中文](../README.md)** | **[English](README.en.md)** | **繁體中文** | **[日本語](README.ja.md)** | **[Français](README.fr.md)** | **[Русский](README.ru.md)** | **[Español](README.es.md)**

</div>

---

## 為什麼需要 Starshot

Windows 內建的截圖工具（Snipping Tool、Win+Shift+S）在 HDR 顯示器上依然只能截出 8bit SDR 影像——系統合成器把 16bit HDR 幀壓縮輸出，高光被截斷，色域被收窄。市面上常見的截圖工具（ShareX 等）同樣受限於傳統 GDI/BitBlt 截圖管線，無法感知 HDR 資料。

Starshot 直接從 DXGI 層獲取顯示器輸出的原始 `R16G16B16A16Float` scRGB 幀緩衝，完整保留 HDR 亮度資訊（可達數千 nit），編碼為 16bit HDR AVIF 或 JPEG XL，色彩空間寫入 BT2020 + PQ 傳輸函數元資料。同時提供 SDR 顯示器自動降級、區域截圖、多格式批次轉換等通用截圖工具應有的功能。

**核心特點**

- 🎯 **HDR 全鏈路無損**——擷取、編碼、色彩管理全程 16bit，不做有損色調映射
- 🧠 **智慧 HDR/SDR 判定**——maxCLL 直方圖區分真實 HDR 內容與 HDR 格式包裹的 SDR 內容
- ✂️ **區域截圖**——凍結幀多顯示器覆蓋層，視窗偵測 + 放大鏡精確選點
- 📋 **原生剪貼簿**——Win32 原生 API 直寫剪貼簿，避免 WinRT 延遲渲染導致貼上失敗
- 🗂️ **多格式支援**——AVIF / JPEG XL / UHDR JPEG / PNG，含批次轉換工具
- 📦 **免安裝**——解壓即用，無需管理員權限

<div align="center">
<table>
<tr>
<td align="center" width="50%">

**其他工具**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="SDR 截圖，高光截斷、色彩發白">
</td>
<td align="center" width="50%">

**Starshot（Ultra HDR JPEG）**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Starshot Ultra HDR JPEG 透過增益圖完整保留高光細節">
</td>
</tr>
</table>
<sub>畫面來自《明日方舟：終末地》</sub>
</div>
</br>

> [!NOTE]
> 由於 GitHub 平台不支援 AVIF 渲染，因此展示的是 Ultra HDR JPEG。AVIF 原圖可以點選 [這裡檢視](https://r2.cialo.site/endfield/3840x2160.dlaa.avif)。

SDR 顯示器上，Starshot 自動走標準 SDR 截圖路徑，是一款通用截圖工具；HDR 顯示器上，它是目前少數能夠完整保留 HDR 資料的桌面截圖方案。

## 系統需求

- 首選 Windows 11 以獲得最佳體驗
- x64 架構
- **HDR 截圖功能需要 HDR 顯示器**（SDR 顯示器上自動走 SDR 路徑）

## 下載

從 [Releases](../../releases) 下載壓縮檔，解壓後執行根目錄的 `Starshot.exe`（啟動器，自動啟動 `app/` 主程式）。無需安裝，解壓即用。

## 快速上手

| 操作                                           | 預設快捷鍵 |
| ---------------------------------------------- | ---------- |
| 全螢幕截圖                                     | Alt+W      |
| 區域截圖（選區後儲存檔案 + 複製到剪貼簿）      | Alt+Q      |
| 區域僅複製（選區後只複製到剪貼簿，不儲存檔案） | Alt+A      |

所有快捷鍵均可在設定中自訂。

## 功能詳解

### HDR 截圖管線

大多數截圖工具在 HDR 顯示器上也只能截 8bit SDR——系統合成器輸出的 16bit 浮點 scRGB 幀被壓成 SDR，高光截斷、色域收窄。Starshot 截取**原始 HDR 幀緩衝**：

1. **HDR 擷取**：顯示器報告 HDR 時，請求 `R16G16B16A16Float` 像素格式，獲取完整 scRGB 浮點資料（亮度可達數千 nit）
2. **HDR 儲存**：16bit AVIF / JPEG XL，BT2020 色域 + PQ 傳輸函數。高光不截斷，色域不收窄
3. **maxCLL 計算**：Win2D 直方圖效果計算最大內容亮度，用於區分真正 HDR 內容與 HDR 格式的 SDR 內容
4. **色彩管理**：讀取顯示器 ICC profile 解析真實色域基色，寫入檔案的 cICP/ICC chunk。HDR 強制 BT2020；SDR 可開關（開 = 讀 ICC 真實色域，關 = BT709）

#### SDR 內容處理

HDR 顯示器上，桌面和 SDR 應用也以 HDR 格式（R16G16B16A16Float）擷取，但內容亮度實際是 SDR 級別。對此 Starshot 的處理：

- **預設**：仍以 HDR 格式儲存（16bit），**不做 8bit 色調映射**，避免降級偏色
- **SDR 內容刪 HDR 開關**（可選）：啟用後偵測 maxCLL 閾值，內容不達標則自動轉 SDR（遵循使用者設定的 SDR 儲存格式）並刪除 HDR 檔案，節省空間

#### UHDR JPEG 回退

HDR 截圖可同時儲存一份 Ultra HDR JPEG（SDR 基底圖 + HDR gain map），在不支援 HDR 的軟體中也能正常顯示。透過 `Starward.Codec` 的 `UhdrEncoder` 編碼。

#### 區域截圖 HDR 權衡

區域截圖覆蓋層**故意**將 HDR 幀色調映射成 SDR 顯示——因為 WinUI 的 `CanvasControl` 走 SDR 交換鏈，scRGB 浮點直出會偏色/發黑。**儲存的檔案是完整 HDR**，沒動；選區時高光被壓隻影響預覽，不影響輸出。

### 三種截圖模式

| 模式       | 目標                                      | 剪貼簿格式          | 檔案   |
| ---------- | ----------------------------------------- | ------------------- | ------ |
| 全螢幕截圖 | 整塊顯示器（前景視窗/遊標所在螢幕，可切換） | CF_HDROP（檔案）    | 儲存   |
| 區域截圖   | 框選 / 點選視窗                           | CF_DIB（BGRA 位圖） | 儲存   |
| 區域僅複製 | 框選 / 點選視窗                           | CF_DIB（BGRA 位圖） | 不儲存 |

三種模式共享 HDR 偵測、色彩管理、檔名模板、儲存管線、資訊浮窗。

### 區域截圖覆蓋層

- **凍結幀**：先截所有顯示器合成一張位圖，覆蓋層顯示凍結幀——選區時畫面不動，覆蓋層不在截圖裡
- **多顯示器**：覆蓋整個虛擬螢幕，放大鏡和座標框鉗制到遊標所在顯示器（不跨螢幕）
- **視窗偵測**：EnumWindows + DWM cloaked/toolwindow 過濾 + DWM 擴充邊界去陰影 + 客戶區雙候選 + Z 序選擇，點選視窗直接截（QuickCrop）
- **放大鏡**：NearestNeighbor 整數對齊 + 像素網格（15×15 像素，每個 10px），像素清晰可辨
- **動畫螞蟻線 + 即時座標**：選區 X/Y/W/H + 遊標物理座標
- **像素精度**：拖曳框選 +1px，視窗矩形 +0
- ESC / 右鍵取消，Enter 確認視窗懸停

### 剪貼簿

非打包 WinUI 應用的 WinRT `Clipboard.SetContent` 不可靠（延遲渲染 + Flush 問題，內容經常到不了其它應用）。Starshot 直接用 Win32 原生 API（`OpenClipboard` / `SetClipboardData`）：

- **全螢幕截圖**：CF_HDROP（檔案拖放格式），貼上進資源管理器/聊天軟體直接得到檔案
- **區域截圖**：CF_DIB（BGRA 位圖），從覆蓋層裁好的 SDR 位圖直接放剪貼簿，不讀檔案、不重編碼、不二次色調映射
- 任意執行緒可調，10×20ms 重試應對剪貼簿被佔用

### 儲存

- **扁平結構**（無子資料夾），預設 `我的圖片\Starshot`，可自訂
- **SDR 格式**（PNG / AVIF / JPEG XL，預設 PNG）和 **HDR 格式**（AVIF / JPEG XL，預設 AVIF）分開設定
- 品質：中 / 高 / 無損
- XMP 元資料（CreatorTool = Starshot）
- 編碼序列化（SemaphoreSlim），避免並發編碼衝突
- **儲存統計**：設定頁顯示截圖 / 縮圖快取 / 桌布 / 日誌 各自佔用空間，支援重新整理與一鍵清理快取（順帶清理孤兒桌布檔案）

#### 支援的格式

| 格式      | 色深                 | HDR 支援               | 用途               |
| --------- | -------------------- | ---------------------- | ------------------ |
| PNG       | 8bit / 16bit         | HDR 格式可存但相容性差 | SDR 預設，無損     |
| AVIF      | 8bit / 10bit / 12bit | 完整 HDR               | HDR 預設，高壓縮比 |
| JPEG XL   | 8bit / 16bit         | 完整 HDR               | HDR 備選，可逆壓縮 |
| UHDR JPEG | 8bit + gain map      | SDR 相容 HDR 回退      | HDR 額外產出       |

### 檔名模板

全螢幕截圖和區域截圖使用**獨立模板**。

| 佔位符                                                    | 含義                            | 範例                |
| --------------------------------------------------------- | ------------------------------- | ------------------- |
| `{process}`                                               | 處理序名稱（不帶副檔名）        | `explorer`          |
| `{processPath}`                                           | exe 檔名（帶副檔名）            | `explorer.exe`      |
| `{title}`                                                 | 視窗標題（trim + 可設截斷長度） | `Genshin Impact`    |
| `{timestamp}`                                             | Unix 時間戳                     | `1721234567`        |
| `{time}`                                                  | yyyyMMdd_HHmmssff               | `20260718_14302512` |
| `{date}`                                                  | yyyyMMdd                        | `20260718`          |
| `{width}` `{height}`                                      | 影像尺寸（px）                  | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}` | 時間各分量                      |                     |

非法檔名字元統一替換為 `_`。

### 資訊浮窗

截圖後彈出縮圖 + 狀態浮窗（不影響截圖——設了 `WDA_EXCLUDEFROMCAPTURE`，其它截圖工具擷取不到此視窗）：

- **處理中**（旋轉動畫）/ **已儲存**（帶開啟按鈕）/ **已複製**（綠色勾）/ **失敗**
- 多次連拍計數器（如 2/3）
- Composition 動畫滑入/滑出

### 截圖庫

- 多資料夾瀏覽（預設截圖目錄 + 使用者自加資料夾）
- `FileSystemWatcher` 即時感知新增/刪除
- 按日期分組、縮圖懶載入
- 右鍵選單：開啟 / 複製檔案 / 複製為 JPG / 在檔案總管中開啟 / 開啟方式 / 刪除
- 多選 + 拖出 + 批次轉換入口

### 圖片檢視器

- 縮放（滑桿 / 按鈕 / 滑鼠滾輪 / 按兩下適配）、全螢幕模式（F11）
- 上一張 / 下一張（方向鍵、滑鼠滾輪、底部縮圖條）
- 拖入檔案直接開啟
- 右鍵選單：複製 檔案 / 路徑 / 影像、刪除、在檔案總管中開啟、開啟方式
- **編輯面板**：HDR / SDR / Auto 顯示模式切換、SDR 亮度滑桿（100–500 nit）、影像與顯示器資訊
- **格式互轉**：匯出為 PNG / AVIF / JPEG XL（SDR 顯示器）或 UHDR JPEG / AVIF / JPEG XL（HDR 顯示器）
- **色彩管理**：讀取顯示器 ICC profile 與 AdvancedColorInfo

### 批次格式轉換

| 轉換方向                          | 引擎                                 |
| --------------------------------- | ------------------------------------ |
| JPG / PNG → AVIF / JXL            | avifenc.exe / cjxl.exe（CLI）        |
| AVIF / JXL → JPG / PNG            | avifdec.exe / djxl.exe（CLI）        |
| JXR / WEBP / HEIC 等 → AVIF / JXL | 處理序內 ImageSaver（avifEncoderLite） |

### 個性化外觀

- **自訂桌布**：三種模式
  - **指定圖片**：選一張圖，固定顯示
  - **指定影片**：循環靜音播放，主視窗隱藏時自動暫停
  - **資料夾隨機**：每次啟動從資料夾隨機抽一張（圖片或影片混選）
  - 桌布來源遺失自動偵測，清理設定並回退到無桌布 + toast 提示
- **強調色**：
  - **從桌布自動取色**（預設開）：取樣桌布主色作為應用強調色（HSV 飽和度提升）；影片僅取樣首幀，避免顏色閃爍
  - **自訂顏色**：手動取色器覆蓋自動取色
- **主題**：跟隨系統 / 淺色 / 深色
- **亞克力效果**：桌布模式下可選磨砂玻璃隔層或桌布直接透出

### 啟動畫面

啟動時顯示 Logo + 標語，延遲 700ms 後 400ms 淡出。僅首次開啟視窗觸發，從托盤恢復時不重播。

### 系統托盤

- 左鍵顯示主視窗，右鍵彈出選單（顯示 / 退出）
- 關閉主視窗最小化到托盤（可開關）
- `ForceExit` 機制確保托盤「退出」能真正退出

### 開機自啟

- 登錄檔 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，指向啟動器（根目錄 `Starshot.exe`）
- 可選 `--hide` 最小化到托盤啟動（需托盤已開啟）

## 架構

### 目錄結構

```
根目錄/
  Starshot.exe            ← C++ 啟動器（~400KB，啟動 app/ 主程式）
  StarshotDatabase.db     ← SQLite 設定資料庫
  app/
    Starshot.exe          ← 主程式（WinUI 3 / .NET 10）
    *.dll                 ← 依賴庫
    avifenc.exe 等        ← 編解碼工具（來自 Starward.Codec NuGet）
%LOCALAPPDATA%/Starshot/ （預設，可設定）
  log/                    ← 日誌
  cache/                  ← 縮圖快取
```

### 啟動器

C++ 原生程式（~400KB），目前寫死 `app/Starshot.exe`。未來可加 `version.ini` 支援版本化目錄 + 自動清理舊版本。

### 技術棧

| 層             | 技術                                                              |
| -------------- | ----------------------------------------------------------------- |
| UI 框架        | WinUI 3（Windows App SDK 1.8）                                    |
| 執行階段       | .NET 10                                                           |
| 圖形           | Win2D 1.3（D3D11 互操作、HDR 色調映射、直方圖效果）               |
| 編解碼         | Starward.Codec NuGet（libavif / libjxl / UltraHDR P/Invoke 封裝） |
| 資料儲存       | SQLite + Dapper                                                   |
| 日誌           | Serilog                                                           |
| 托盤           | H.NotifyIcon.WinUI                                                |
| 縮圖           | Scighost.WinUI ImageEx + 自訂 CachedImage                         |
| 區域截圖覆蓋層 | Win2D CanvasControl（凍結幀渲染 + 選區繪製）                      |
| 剪貼簿         | Win32 原生 API（OpenClipboard / SetClipboardData）                |
| 啟動器         | C++ 原生（v145 工具集，靜態 CRT）                                 |

### 重入保護

`Interlocked.CompareExchange` 全域守衛，全螢幕/區域/僅複製共用一個 `_isCapturing` 旗標——鍵盤連觸或快速連續按熱鍵不會觸發多次截圖。

### 構建設定

|              | Debug                                       | Release                                                                                         |
| ------------ | ------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| .NET 執行階段 | Framework-dependent（不打包）               | 自包含                                                                                          |
| 原生庫       | 僅 win-x64（RuntimeIdentifier，鋪到輸出根） | 同 Debug                                                                                        |
| Trim         | 否                                          | partial                                                                                         |
| ReadyToRun   | 否                                          | 是                                                                                              |
| 額外清理     | —                                           | 刪除 DirectML.dll / onnxruntime.dll / NpuDetect（Windows App SDK 的 WinML/AI 組件，本應用不用） |
| 輸出路徑     | `build/app/`                                | `build/release/app/` + 啟動器拷到 `build/release/`                                              |
| 大小         | ~80MB                                       | 更小（Trim + 刪 AI 庫）                                                                         |

## 從原始碼構建

### 環境需求

- Visual Studio 2022 / 2026（含 C++ 桌面開發、.NET 桌面開發）
- .NET 10 SDK
- Windows SDK 10.0.26100

### 步驟

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# 構建主程式（輸出到 build/app/）
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# 構建啟動器（輸出到 build/Starshot.exe，需要 VS 的 MSBuild）
"C:\Program Files\Microsoft Visual Studio\<版本>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 執行：build/Starshot.exe（啟動器）或 build/app/Starshot.exe（主程式）

# === Release 發布 ===
# 1. 先構建啟動器（輸出到 build/Starshot.exe）
"C:\Program Files\Microsoft Visual Studio\<版本>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. 發布主程式（輸出到 build/release/app/，自動拷啟動器到 build/release/Starshot.exe + 刪 AI 庫）
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# 完成後的目錄結構：
# build/release/
#   Starshot.exe        ← 啟動器（自動拷貝）
#   app/
#     Starshot.exe      ← 主程式（自包含 + trim + R2R）
#     *.dll / avifenc.exe 等
```

## 已知限制

- 區域截圖覆蓋層 HDR 幀顯示為 SDR（WinUI CanvasControl 走 SDR 交換鏈）；儲存的檔案不受影響
- 自訂桌布按 `UniformToFill` 鋪滿，但 WinUI 的裁剪不置中，目前是**左上**對齊，比如窄（豎向）桌布在寬視窗裡會只顯示上半部分（從頂部裁剪而非置中）
- 區域截圖覆蓋層開啟瞬間，遊標仍是系統預設形狀，**需移動一次滑鼠後十字遊標才出現**（WinUI `ProtectedCursor` 對已在元素上的靜止指標不立即生效，移動一次觸發 pointer 事件後即正常）
- 暫無版本管理 / 自動更新

## 致謝

- [Starward](https://github.com/Scighost/Starward) — 截圖核心、編解碼引擎、視窗框架均源自 Starward，由 [@Scighost](https://github.com/Scighost) 開發
- [ShareX](https://github.com/ShareX/ShareX) — 區域截圖覆蓋層的視窗偵測和互動設計參考

## License

MIT
