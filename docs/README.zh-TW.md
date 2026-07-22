<div align="center">

<img src="../src/logo.png" width="120" alt="Starshot Logo">

# Starshot

**新一代 Windows 原生 HDR 截圖工具**

**Next-generation Windows-native HDR Screenshot Tool**

16bit 全鏈路擷取 · 區域截圖 · AVIF / JPEG XL 編碼 · 色彩管理

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](https://github.com/loliri/Starshot?tab=MIT-1-ov-file)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../../releases)

[下載](../../../releases) · [快速上手](#快速上手) · [功能詳解](#功能詳解) · [從原始碼建構](#從原始碼建構)

**[English](../README.md)** | **[简体中文](README.zh-CN.md)** | **繁體中文** | **[日本語](README.ja.md)** | **[Français](README.fr.md)** | **[Русский](README.ru.md)** | **[Español](README.es.md)**

</div>

---

## 為什麼需要 Starshot

Windows 內建的截圖工具（Snipping Tool、Win+Shift+S）在 HDR 顯示器上依然只能截出 8bit SDR 圖像——系統合成器把 16bit HDR 幀壓縮輸出，高光被截斷，色域被收窄。市面上常見的截圖工具（ShareX 等）同樣受限於傳統 GDI/BitBlt 截圖管線，無法感知 HDR 資料。

Starshot 直接從 DXGI 層獲取顯示器輸出的原始 `R16G16B16A16Float` scRGB 幀緩衝，完整保留 HDR 亮度資訊（可達數千 nit），編碼為 16bit HDR AVIF 或 JPEG XL，色彩空間寫入 BT2020 + PQ 傳輸函數中繼資料。同時提供 SDR 顯示器自動降級、區域截圖、多格式批次轉換等通用截圖工具應有的功能。

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

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="SDR screenshot showing clipped highlights and washed out colors">
</td>
<td align="center" width="50%">

**Starshot（Ultra HDR JPEG）**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Starshot Ultra HDR JPEG preserving full highlight detail via gain map">
</td>
</tr>
</table>
<sub>畫面來自《明日方舟：終末地》</sub>
</div>
</br>

> [!NOTE]
> 由於 GitHub 平台不支援 AVIF 渲染，因此展示的是 Ultra HDR JPEG。AVIF 原圖可以點選 [這裡查看](https://r2.cialo.site/endfield/3840x2160.dlaa.avif)。

SDR 顯示器上，Starshot 自動走標準 SDR 截圖路徑，是一款通用截圖工具；HDR 顯示器上，它是目前少數能夠完整保留 HDR 資料的桌面截圖方案。

## 系統需求

- Windows 10 / 11，首選 Windows 11 以獲得最佳體驗
- x64 架構
- **HDR 截圖功能需要 HDR 顯示器**（SDR 顯示器上自動走 SDR 路徑）

## 下載

從 [Releases](../../../releases) 下載壓縮檔，解壓後執行根目錄的 `Starshot.exe` 啟動器。無需安裝，解壓即用。

## 軟體截圖

![Screenshot](Screenshot.jpg)

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
- **SDR 內容刪 HDR 開關**（可選）：啟用後檢測 maxCLL 閾值，內容不達標則自動轉 SDR（遵循使用者設定的 SDR 儲存格式）並刪除 HDR 檔案，節省空間

#### UHDR JPEG 回退

HDR 截圖可同時儲存一份 Ultra HDR JPEG（SDR 基圖 + HDR gain map），在不支援 HDR 的軟體中也能正常顯示。透過 `Starward.Codec` 的 `UhdrEncoder` 編碼。

#### 區域截圖 HDR 權衡

區域截圖覆蓋層**故意**將 HDR 幀色調映射成 SDR 顯示——因為 WinUI 的 `CanvasControl` 走 SDR 交換鏈，scRGB 浮點直出會偏色/發黑。**儲存的檔案是完整 HDR**，沒動；選區時高光被壓只影響預覽，不影響輸出。

### 三種截圖模式

| 模式       | 目標                                      | 剪貼簿格式          | 檔案   |
| ---------- | ----------------------------------------- | ------------------- | ------ |
| 全螢幕截圖 | 整塊顯示器（前景視窗/游標所在螢幕，可切換） | CF_HDROP（檔案）    | 儲存   |
| 區域截圖   | 框選 / 單擊視窗                           | CF_DIB（BGRA 點陣圖） | 儲存   |
| 區域僅複製 | 框選 / 單擊視窗                           | CF_DIB（BGRA 點陣圖） | 不儲存 |

三種模式共享 HDR 檢測、色彩管理、檔案名稱範本、儲存管線、資訊浮窗。

### 區域截圖覆蓋層

- **凍結幀**：先截所有顯示器合成一張點陣圖，覆蓋層顯示凍結幀——選區時畫面不動，覆蓋層不在截圖裡
- **多顯示器**：覆蓋整個虛擬螢幕，放大鏡和座標框鉗制到游標所在顯示器（不跨螢幕）
- **視窗偵測**：EnumWindows + DWM cloaked/toolwindow 過濾 + DWM 擴展邊界去陰影 + 客戶區雙候選 + Z 序選擇，單擊視窗直接截（QuickCrop）
- **放大鏡**：NearestNeighbor 整數對齊 + 像素網格（15×15 像素，每個 10px），像素清晰可辨
- **動畫螞蟻線 + 即時座標**：選區 X/Y/W/H + 游標物理座標
- **像素精度**：拖曳框選 +1px，視窗矩形 +0
- ESC / 右鍵取消，Enter 確認視窗懸停

### 剪貼簿

非打包 WinUI 應用的 WinRT `Clipboard.SetContent` 不可靠（延遲渲染 + Flush 問題，內容經常到不了其它應用）。Starshot 直接用 Win32 原生 API（`OpenClipboard` / `SetClipboardData`）：

- **全螢幕截圖**：CF_HDROP（檔案拖放格式），貼上進資源管理器/聊天軟體直接得到檔案
- **區域截圖**：CF_DIB（BGRA 點陣圖），從覆蓋層裁好的 SDR 點陣圖直接放剪貼簿，不讀檔案、不重編碼、不二次色調映射
- 任意執行緒可調，10×20ms 重試應對剪貼簿被佔用

### 儲存

- **扁平結構**（無子資料夾），預設 `我的圖片\Starshot`，可自訂
- **SDR 格式**（PNG / AVIF / JPEG XL，預設 PNG）和 **HDR 格式**（AVIF / JPEG XL，預設 AVIF）分開設定
- 品質：中 / 高 / 無損
- XMP 中繼資料（CreatorTool = Starshot）
- 編碼序列化（SemaphoreSlim），避免並發編碼衝突
- **儲存統計**：設定頁顯示截圖 / 縮圖快取 / 桌布 / 日誌 / 備份 各自佔用空間，支援重新整理與一鍵清理快取（順帶清理孤兒桌布檔案）

#### 支援的格式

| 格式      | 色深                 | HDR 支援               | 用途               |
| --------- | -------------------- | ---------------------- | ------------------ |
| PNG       | 8bit / 16bit         | HDR 格式可存但相容性差 | SDR 預設，無損     |
| AVIF      | 8bit / 10bit / 12bit | 完整 HDR               | HDR 預設，高壓縮比 |
| JPEG XL   | 8bit / 16bit         | 完整 HDR               | HDR 備選，可逆壓縮 |
| UHDR JPEG | 8bit + gain map      | SDR 相容 HDR 回退      | HDR 額外產出       |

### 檔案名稱範本

全螢幕截圖和區域截圖使用**獨立範本**。

| 佔位符                                                    | 含義                            | 範例                |
| --------------------------------------------------------- | ------------------------------- | ------------------- |
| `{process}`                                               | 處理程序名稱（不帶副檔名）      | `explorer`          |
| `{processPath}`                                           | exe 檔案名稱（帶副檔名）        | `explorer.exe`      |
| `{title}`                                                 | 視窗標題（trim + 可設截斷長度） | `Genshin Impact`    |
| `{timestamp}`                                             | Unix 時間戳                     | `1721234567`        |
| `{time}`                                                  | yyyyMMdd_HHmmssff               | `20260718_14302512` |
| `{date}`                                                  | yyyyMMdd                        | `20260718`          |
| `{width}` `{height}`                                      | 圖像尺寸（px）                  | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}` | 時間各分量                      |                     |

非法檔案名稱字元統一替換為 `_`。

### 資訊浮窗

截圖後彈出縮圖 + 狀態浮窗（不影響截圖——設了 `WDA_EXCLUDEFROMCAPTURE`，其它截圖工具擷取不到此視窗）：

- **處理中**（旋轉動畫）/ **已儲存**（帶開啟按鈕）/ **已複製**（綠色勾）/ **失敗**
- 多次連拍計數器（如 2/3）
- Composition 動畫滑入/滑出

### 截圖庫

- 多資料夾瀏覽（預設截圖目錄 + 使用者自加資料夾）
- `FileSystemWatcher` 即時感知新增/刪除
- 按日期分組、縮圖懶載入
- 右鍵選單：開啟 / 複製檔案 / 複製為 JPG / 在資源管理器中開啟 / 開啟方式 / 刪除
- 多選 + 拖出 + 批次轉換入口

### 圖片檢視器

- 縮放（滑桿 / 按鈕 / 滑鼠滾輪 / 雙擊適配）、全螢幕模式（F11）
- 上一張 / 下一張（方向鍵、滑鼠滾輪、底部縮圖條）
- 拖入檔案直接開啟
- 右鍵選單：複製 檔案 / 路徑 / 圖像、刪除、在資源管理器中開啟、開啟方式
- **編輯面板**：HDR / SDR / Auto 顯示模式切換、SDR 亮度滑桿（100–500 nit）、圖像與顯示器資訊
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
  - **指定影片**：迴圈靜音播放，主視窗隱藏時自動暫停
  - **資料夾隨機**：每次啟動從資料夾隨機抽一張（圖片或影片混選）
  - 桌布來源遺失自動檢測，清理設定並回退到無桌布 + toast 提示
- **強調色**：
  - **從桌布自動取色**（預設開）：取樣桌布主色作為應用強調色（HSV 飽和度提升）；影片僅取樣首幀，避免顏色閃爍
  - **自訂顏色**：手動取色器覆蓋自動取色
- **主題**：跟隨系統 / 淺色 / 深色
- **亞克力效果**：桌布模式下可選磨砂玻璃隔層或桌布直接透出

### 啟動畫面

啟動時顯示 Logo + 標語，延遲 700ms 後 400ms 淡出。僅首次開啟視窗觸發，從工作列恢復時不重播。

### 系統工作列

- 左鍵顯示主視窗，右鍵彈出選單（顯示 / 退出）
- 關閉主視窗最小化到工作列（可開關）
- `ForceExit` 機制確保工作列"退出"能真正退出

### 開機自啟

- 登錄檔 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，指向啟動器（根目錄 `Starshot.exe`）
- 可選 `--hide` 最小化到工作列啟動（需工作列已開啟）
- 開關即時讀登錄檔（不快取資料庫）：工作管理員停用只動 StartupApproved、不刪 Run 項，開關仍顯示開
- 啟動時檢測自啟項指向的 exe 是否存在，不存在則自動清除啟動項並 toast 提示

### 檢查更新

- 啟動時節流檢查（≥24h + 開關開啟）GitHub Releases 最新版，或 About 頁手動檢查
- 更新用 SharpCompress 真流式解壓（網路流直連，不落 zip），逐 entry 直接寫到根目錄 — 失敗還原，成功重啟啟動器帶 `--clean` 清舊
- 僅 CI/CD release 檢查（讀 `version.ini` 版本號）；本機建構（無 `version.ini`，`AppVersion = Local`）按 0.0.0 處理，可更新到任意 CI/CD release
- 版本大小寫約定：GitHub tag、zip 名、`app-{version}/` 目錄一律小寫（如 `0.3.1-preview`）；`version.ini` 內容保留原始大小寫（`0.3.1-Preview`，About 頁顯示用），啟動器讀取時自己轉小寫定位目錄

## 架構

### 目錄結構

```
根目錄/
  Starshot.exe            ← C++ 啟動器（讀 version.ini 決定啟哪個 app 目錄）
  StarshotDatabase.db     ← SQLite 設定資料庫
  version.ini             ← 版本號（僅 CI/CD release 有，本機建構無）
  app-{version}/          ← 主程式目錄（CI/CD release 版本化，本機建構為 app/）
    Starshot.exe          ← 主程式（WinUI 3 / .NET 10）
    *.dll                 ← 依賴庫
    avifenc.exe 等        ← 編解碼工具（來自 Starward.Codec NuGet）
  backup/                 ← 資料庫備份
%LOCALAPPDATA%/Starshot/ （預設，可設定）
  log/                    ← 日誌
  bg/                     ← 桌布
  thumb/                  ← 縮圖快取
```

### 啟動器

C++ 原生程式（~400KB）。讀 `version.ini` 決定啟動 `app-{version}/Starshot.exe`（無 version.ini 則 `app/`，debug/local 建構）。帶 `--clean`（或 `--clean=<pid>`）參數啟動時走訪 `app-*` 目錄刪除非當前版本。

### 工作列與後台啟動

- `--hide` 自啟時不建立 MainWindow，全域熱鍵註冊到 SystemTrayWindow 的 hwnd（工作列視窗作為常駐宿主）
- H.NotifyIcon.WinUI 的 TaskbarIcon 依賴 Window 的一次 Show 觸發 `Loaded` 才註冊圖示；初始化時套 `WS_EX_LAYERED + alpha=0` 讓視窗透明地完成這次 Show，避免 `--hide` 自啟時可見閃爍
- C++ 啟動器用 `argv[1..]` 重新拼接透傳命令

### 技術堆疊

| 層             | 技術                                                              |
| -------------- | ----------------------------------------------------------------- |
| UI 框架        | WinUI 3（Windows App SDK 1.8）                                    |
| 執行階段       | .NET 10                                                           |
| 圖形           | Win2D 1.3（D3D11 互操作、HDR 色調映射、直方圖效果）              |
| 編解碼         | Starward.Codec NuGet（libavif / libjxl / UltraHDR P/Invoke 封裝） |
| 資料儲存       | SQLite + Dapper                                                   |
| 日誌           | Serilog                                                           |
| 工作列         | H.NotifyIcon.WinUI                                                |
| 縮圖           | Scighost.WinUI ImageEx + 自訂 CachedImage                         |
| 區域截圖覆蓋層 | Win2D CanvasControl（凍結幀渲染 + 選區繪製）                      |
| 剪貼簿         | Win32 原生 API（OpenClipboard / SetClipboardData）                |
| 啟動器         | C++ 原生（v145 工具集，靜態 CRT）                                 |

### 重入保護

`Interlocked.CompareExchange` 全域守衛，全螢幕/區域/僅複製共用一個 `_isCapturing` 旗標——鍵盤連觸或快速連續按熱鍵不會觸發多次截圖。

### 建構設定

|              | Debug                                       | Release                                                                                         |
| ------------ | ------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| .NET Runtime | Framework-dependent（不打包）               | 自包含                                                                                          |
| 原生庫       | 僅 win-x64（RuntimeIdentifier，鋪到輸出根） | 同 Debug                                                                                        |
| Trim         | 否                                          | partial                                                                                         |
| ReadyToRun   | 否                                          | 是                                                                                              |
| 額外清理     | —                                           | 刪除 DirectML.dll / onnxruntime.dll / NpuDetect（Windows App SDK 的 WinML/AI 組件，本應用不用） |
| 輸出路徑     | `build/app/`                                | `build/release/app/` + 啟動器拷到 `build/release/`                                              |
| 大小         | ~80MB                                       | 更小（Trim + 刪 AI 庫）                                                                         |

## 從原始碼建構

### 環境需求

- Visual Studio 2022 / 2026（含 C++ 桌面開發、.NET 桌面開發）
- .NET 10 SDK
- Windows SDK 10.0.26100

### 步驟

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# 建構主程式（輸出到 build/app/）
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# 建構啟動器（輸出到 build/Starshot.exe，需要 VS 的 MSBuild）
"C:\Program Files\Microsoft Visual Studio\<版本>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 執行：build/Starshot.exe（啟動器）或 build/app/Starshot.exe（主程式）

# === Release 發佈 ===
# 1. 先建構啟動器（輸出到 build/Starshot.exe）
"C:\Program Files\Microsoft Visual Studio\<版本>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. 發佈主程式（輸出到 build/release/app/，自動拷啟動器到 build/release/Starshot.exe + 刪 AI 庫）
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
- 區域截圖覆蓋層開啟瞬間，游標仍是系統預設形狀，**需移動一次滑鼠後十字游標才出現**（WinUI `ProtectedCursor` 對已在元素上的靜止指標不立即生效，移動一次觸發 pointer 事件後即正常）
- 雙屏 DPI 不一致（如主屏 150%、副屏 125%）時，區域截圖覆蓋層在副屏的座標會錯位（放大鏡 / 選區對不上）。規避：統一雙屏縮放比例

## 國際化（i18n）

翻譯基於 `src/Starshot.Language/` 下的 `.resx` 資源檔（`Lang.resx` 為英文預設，`Lang.zh-CN.resx` 等為各語言）。另外還需在 `GeneralSetting` 的語言 ComboBox 加選項 + `LanguageIndex` 映射。

歡迎貢獻翻譯：fork 儲存庫 → 複製 `Lang.resx` 為 `Lang.{你的語言}.resx` → 翻譯 → 提交 PR。

## 開發說明

本專案正處於開發階段，功能可能隨時變動，請隨時關注更新！

歡迎參與：

- 發現 Bug？[提交 Issue](../../../issues/new)
- 有功能建議？[發起討論](../../../issues/new)
- 想貢獻程式碼？歡迎提交 [Pull Request](../../../pulls)

## 常見問題

<details>
<summary><b>截圖庫（首頁）圖片顏色異常 / 亂色</b></summary>

這通常是 Windows 系統圖像解碼器（AVIF / HEIF / JPEG XL 擴展）的問題，不是 Starshot 的 bug。嘗試在 Microsoft Store 中搜尋並更新以下組件：

- **AV1 Video Extension**
- **HEIF Image Extensions**
- **HEVC Video Extensions**
- **Webp Image Extensions**

更新後重啟 Starshot。如果問題持續，請 [提交 Issue](../../../issues/new) 並附上截圖。

</details>

<details>
<summary><b>截圖顏色和螢幕上看到的不一樣</b></summary>

如果你使用的是 HDR 顯示器，請確認 Windows HDR 開關已開啟（設定 → 系統 → 顯示 → HDR）。HDR 截圖功能僅在 HDR 模式下生效。

</details>

<details>
<summary><b>截圖後剪貼簿貼上不出來</b></summary>

Starshot 使用 Win32 原生剪貼簿 API 寫入，理論上比 WinRT 更可靠。如果仍貼上失敗，可能是目標應用不支援對應的剪貼簿格式（CF_HDROP 檔案 / CF_DIB 點陣圖）。嘗試貼上到資源管理器（檔案）或小畫家（點陣圖）驗證。

</details>

## 致謝

- [Starward](https://github.com/Scighost/Starward) — 截圖核心、編解碼引擎、視窗框架均源自 Starward，由 [@Scighost](https://github.com/Scighost) 開發
- [ShareX](https://github.com/ShareX/ShareX) — 區域截圖覆蓋層的視窗偵測和互動設計參考

**和所有用到的第三方庫**：

- [CommunityToolkit](https://github.com/CommunityToolkit) — MVVM 框架 + WinUI 控制項（Segmented / Behaviors / Helpers）
- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — 流式解壓
- [Dapper](https://github.com/DapperLib/Dapper) — SQLite 輕量 ORM
- [H.NotifyIcon.WinUI](https://github.com/HavenDV/H.NotifyIcon) — 系統工作列
- [Vanara.PInvoke](https://github.com/dahall/Vanara) — Win32 API 封裝（DwmApi / Ole / Shell32）
- [ComputeSharp.D2D1](https://github.com/Sergio0694/ComputeSharp) — GPU 計算效果
- [Serilog](https://github.com/serilog/serilog) — 結構化日誌

## License

MIT
