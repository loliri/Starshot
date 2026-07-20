<div align="center">

<img src="src/logo.png" width="120" alt="Starshot Logo">

# Starshot

**新一代 Windows 原生 HDR 截图工具**

**Next-generation Windows-native HDR Screenshot Tool**

16bit 全链路捕获 · 区域截图 · AVIF / JPEG XL 编码 · 色彩管理

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../releases)

[下载](../../releases) · [快速上手](#快速上手) · [功能详解](#功能详解) · [从源码构建](#从源码构建)

**简体中文** | **[English](docs/README.en.md)** | **[繁體中文](docs/README.zh-TW.md)** | **[日本語](docs/README.ja.md)** | **[Français](docs/README.fr.md)** | **[Русский](docs/README.ru.md)** | **[Español](docs/README.es.md)**

</div>

---

## 为什么需要 Starshot

Windows 自带的截图工具（Snipping Tool、Win+Shift+S）在 HDR 显示器上依然只能截出 8bit SDR 图像——系统合成器把 16bit HDR 帧压缩输出，高光被截断，色域被收窄。市面上常见的截图工具（ShareX 等）同样受限于传统 GDI/BitBlt 截图管线，无法感知 HDR 数据。

Starshot 直接从 DXGI 层获取显示器输出的原始 `R16G16B16A16Float` scRGB 帧缓冲，完整保留 HDR 亮度信息（可达数千 nit），编码为 16bit HDR AVIF 或 JPEG XL，色彩空间写入 BT2020 + PQ 传输函数元数据。同时提供 SDR 显示器自动降级、区域截图、多格式批量转换等通用截图工具应有的功能。

**核心特点**

- 🎯 **HDR 全链路无损**——捕获、编码、色彩管理全程 16bit，不做有损色调映射
- 🧠 **智能 HDR/SDR 判定**——maxCLL 直方图区分真实 HDR 内容与 HDR 格式包裹的 SDR 内容
- ✂️ **区域截图**——冻结帧多显示器覆盖层，窗口检测 + 放大镜精确选点
- 📋 **原生剪贴板**——Win32 原生 API 直写剪贴板，避免 WinRT 延迟渲染导致粘贴失败
- 🗂️ **多格式支持**——AVIF / JPEG XL / UHDR JPEG / PNG，含批量转换工具
- 📦 **免安装**——解压即用，无需管理员权限

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
<sub>画面来自《明日方舟：终末地》</sub>
</div>
</br>

> [!NOTE]
> 由于 GitHub 平台不支持 AVIF 渲染，因此展示的是 Ultra HDR JPEG。AVIF 原图可以点击 [这里查看](https://r2.cialo.site/endfield/3840x2160.dlaa.avif)。

SDR 显示器上，Starshot 自动走标准 SDR 截图路径，是一款通用截图工具；HDR 显示器上，它是目前少数能够完整保留 HDR 数据的桌面截图方案。

## 系统要求

- 首选 Windows 11 以获得最佳体验
- x64 架构
- **HDR 截图功能需要 HDR 显示器**（SDR 显示器上自动走 SDR 路径）

## 下载

从 [Releases](../../releases) 下载压缩包，解压后运行根目录的 `Starshot.exe`（启动器，自动启动 `app/` 主程序）。无需安装，解压即用。

## 快速上手

| 操作                                           | 默认快捷键 |
| ---------------------------------------------- | ---------- |
| 全屏截图                                       | Alt+W      |
| 区域截图（选区后保存文件 + 复制到剪贴板）      | Alt+Q      |
| 区域仅复制（选区后只复制到剪贴板，不保存文件） | Alt+A      |

所有快捷键均可在设置中自定义。

## 功能详解

### HDR 截图管线

大多数截图工具在 HDR 显示器上也只能截 8bit SDR——系统合成器输出的 16bit 浮点 scRGB 帧被压成 SDR，高光截断、色域收窄。Starshot 截取**原始 HDR 帧缓冲**：

1. **HDR 捕获**：显示器报告 HDR 时，请求 `R16G16B16A16Float` 像素格式，获取完整 scRGB 浮点数据（亮度可达数千 nit）
2. **HDR 保存**：16bit AVIF / JPEG XL，BT2020 色域 + PQ 传输函数。高光不截断，色域不收窄
3. **maxCLL 计算**：Win2D 直方图效果计算最大内容亮度，用于区分真正 HDR 内容与 HDR 格式的 SDR 内容
4. **色彩管理**：读取显示器 ICC profile 解析真实色域基色，写入文件的 cICP/ICC chunk。HDR 强制 BT2020；SDR 可开关（开 = 读 ICC 真实色域，关 = BT709）

#### SDR 内容处理

HDR 显示器上，桌面和 SDR 应用也以 HDR 格式（R16G16B16A16Float）捕获，但内容亮度实际是 SDR 级别。对此 Starshot 的处理：

- **默认**：仍以 HDR 格式保存（16bit），**不做 8bit 色调映射**，避免降级偏色
- **SDR 内容删 HDR 开关**（可选）：启用后检测 maxCLL 阈值，内容不达标则自动转 SDR（遵循用户设置的 SDR 存储格式）并删除 HDR 文件，节省空间

#### UHDR JPEG 回退

HDR 截图可同时保存一份 Ultra HDR JPEG（SDR 基图 + HDR gain map），在不支持 HDR 的软件中也能正常显示。通过 `Starward.Codec` 的 `UhdrEncoder` 编码。

#### 区域截图 HDR 权衡

区域截图覆盖层**故意**将 HDR 帧色调映射成 SDR 显示——因为 WinUI 的 `CanvasControl` 走 SDR 交换链，scRGB 浮点直出会偏色/发黑。**保存的文件是完整 HDR**，没动；选区时高光被压只影响预览，不影响输出。

### 三种截图模式

| 模式       | 目标                                      | 剪贴板格式          | 文件   |
| ---------- | ----------------------------------------- | ------------------- | ------ |
| 全屏截图   | 整块显示器（前台窗口/光标所在屏，可切换） | CF_HDROP（文件）    | 保存   |
| 区域截图   | 框选 / 单击窗口                           | CF_DIB（BGRA 位图） | 保存   |
| 区域仅复制 | 框选 / 单击窗口                           | CF_DIB（BGRA 位图） | 不保存 |

三种模式共享 HDR 检测、色彩管理、文件名模板、保存管线、信息浮窗。

### 区域截图覆盖层

- **冻结帧**：先截所有显示器合成一张位图，覆盖层显示冻结帧——选区时画面不动，覆盖层不在截图里
- **多显示器**：覆盖整个虚拟屏幕，放大镜和坐标框钳制到光标所在显示器（不跨屏）
- **窗口检测**：EnumWindows + DWM cloaked/toolwindow 过滤 + DWM 扩展边界去阴影 + 客户区双候选 + Z 序选择，单击窗口直接截（QuickCrop）
- **放大镜**：NearestNeighbor 整数对齐 + 像素网格（15×15 像素，每个 10px），像素清晰可辨
- **动画蚂蚁线 + 实时坐标**：选区 X/Y/W/H + 光标物理坐标
- **像素精度**：拖拽框选 +1px，窗口矩形 +0
- ESC / 右键取消，Enter 确认窗口悬停

### 剪贴板

非打包 WinUI 应用的 WinRT `Clipboard.SetContent` 不可靠（延迟渲染 + Flush 问题，内容经常到不了其它应用）。Starshot 直接用 Win32 原生 API（`OpenClipboard` / `SetClipboardData`）：

- **全屏截图**：CF_HDROP（文件拖放格式），粘贴进资源管理器/聊天软件直接得到文件
- **区域截图**：CF_DIB（BGRA 位图），从覆盖层裁好的 SDR 位图直接放剪贴板，不读文件、不重编码、不二次色调映射
- 任意线程可调，10×20ms 重试应对剪贴板被占用

### 保存

- **扁平结构**（无子文件夹），默认 `我的图片\Starshot`，可自定义
- **SDR 格式**（PNG / AVIF / JPEG XL，默认 PNG）和 **HDR 格式**（AVIF / JPEG XL，默认 AVIF）分开设置
- 质量：中 / 高 / 无损
- XMP 元数据（CreatorTool = Starshot）
- 编码串行化（SemaphoreSlim），避免并发编码冲突
- **存储统计**：设置页显示截图 / 缩略图缓存 / 壁纸 / 日志 各自占用空间，支持刷新与一键清理缓存（顺带清理孤儿壁纸文件）

#### 支持的格式

| 格式      | 色深                 | HDR 支持               | 用途               |
| --------- | -------------------- | ---------------------- | ------------------ |
| PNG       | 8bit / 16bit         | HDR 格式可存但兼容性差 | SDR 默认，无损     |
| AVIF      | 8bit / 10bit / 12bit | 完整 HDR               | HDR 默认，高压缩比 |
| JPEG XL   | 8bit / 16bit         | 完整 HDR               | HDR 备选，可逆压缩 |
| UHDR JPEG | 8bit + gain map      | SDR 兼容 HDR 回退      | HDR 额外产出       |

### 文件名模板

全屏截图和区域截图使用**独立模板**。

| 占位符                                                    | 含义                            | 示例                |
| --------------------------------------------------------- | ------------------------------- | ------------------- |
| `{process}`                                               | 进程名（不带扩展名）            | `explorer`          |
| `{processPath}`                                           | exe 文件名（带扩展名）          | `explorer.exe`      |
| `{title}`                                                 | 窗口标题（trim + 可设截断长度） | `Genshin Impact`    |
| `{timestamp}`                                             | Unix 时间戳                     | `1721234567`        |
| `{time}`                                                  | yyyyMMdd_HHmmssff               | `20260718_14302512` |
| `{date}`                                                  | yyyyMMdd                        | `20260718`          |
| `{width}` `{height}`                                      | 图像尺寸（px）                  | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}` | 时间各分量                      |                     |

非法文件名字符统一替换为 `_`。

### 信息浮窗

截图后弹出缩略图 + 状态浮窗（不影响截图——设了 `WDA_EXCLUDEFROMCAPTURE`，其它截图工具捕获不到此窗口）：

- **处理中**（旋转动画）/ **已保存**（带打开按钮）/ **已复制**（绿色勾）/ **失败**
- 多次连拍计数器（如 2/3）
- Composition 动画滑入/滑出

### 截图库

- 多文件夹浏览（默认截图目录 + 用户自加文件夹）
- `FileSystemWatcher` 实时感知新增/删除
- 按日期分组、缩略图懒加载
- 右键菜单：打开 / 复制文件 / 复制为 JPG / 在资源管理器中打开 / 打开方式 / 删除
- 多选 + 拖出 + 批量转换入口

### 图片查看器

- 缩放（滑块 / 按钮 / 鼠标滚轮 / 双击适配）、全屏模式（F11）
- 上一张 / 下一张（方向键、鼠标滚轮、底部缩略图条）
- 拖入文件直接打开
- 右键菜单：复制 文件 / 路径 / 图像、删除、在资源管理器中打开、打开方式
- **编辑面板**：HDR / SDR / Auto 显示模式切换、SDR 亮度滑块（100–500 nit）、图像与显示器信息
- **格式互转**：导出为 PNG / AVIF / JPEG XL（SDR 显示器）或 UHDR JPEG / AVIF / JPEG XL（HDR 显示器）
- **色彩管理**：读取显示器 ICC profile 与 AdvancedColorInfo

### 批量格式转换

| 转换方向                          | 引擎                                 |
| --------------------------------- | ------------------------------------ |
| JPG / PNG → AVIF / JXL            | avifenc.exe / cjxl.exe（CLI）        |
| AVIF / JXL → JPG / PNG            | avifdec.exe / djxl.exe（CLI）        |
| JXR / WEBP / HEIC 等 → AVIF / JXL | 进程内 ImageSaver（avifEncoderLite） |

### 个性化外观

- **自定义壁纸**：三种模式
  - **指定图片**：选一张图，固定显示
  - **指定视频**：循环静音播放，主窗口隐藏时自动暂停
  - **文件夹随机**：每次启动从文件夹随机抽一张（图片或视频混选）
  - 壁纸源丢失自动检测，清理配置并回退到无壁纸 + toast 提示
- **强调色**：
  - **从壁纸自动取色**（默认开）：采样壁纸主色作为应用强调色（HSV 饱和度提升）；视频仅采样首帧，避免颜色闪烁
  - **自定义颜色**：手动取色器覆盖自动取色
- **主题**：跟随系统 / 浅色 / 深色
- **亚克力效果**：壁纸模式下可选磨砂玻璃隔层或壁纸直接透出

### 启动画面

启动时显示 Logo + 标语，延迟 700ms 后 400ms 淡出。仅首次打开窗口触发，从托盘恢复时不重放。

### 系统托盘

- 左键显示主窗口，右键弹出菜单（显示 / 退出）
- 关闭主窗口最小化到托盘（可开关）
- `ForceExit` 机制确保托盘"退出"能真正退出

### 开机自启

- 注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，指向启动器（根目录 `Starshot.exe`）
- 可选 `--hide` 最小化到托盘启动（需托盘已开启）

## 架构

### 目录结构

```
根目录/
  Starshot.exe            ← C++ 启动器（~400KB，启动 app/ 主程序）
  StarshotDatabase.db     ← SQLite 设置数据库
  app/
    Starshot.exe          ← 主程序（WinUI 3 / .NET 10）
    *.dll                 ← 依赖库
    avifenc.exe 等        ← 编解码工具（来自 Starward.Codec NuGet）
%LOCALAPPDATA%/Starshot/ （默认，可配置）
  log/                    ← 日志
  cache/                  ← 缩略图缓存
```

### 启动器

C++ 原生程序（~400KB），目前写死 `app/Starshot.exe`。未来可加 `version.ini` 支持版本化目录 + 自动清理旧版本。

### 托盘与后台启动

- `--hide` 自启时不创建 MainWindow，全局热键注册到 SystemTrayWindow 的 hwnd（托盘窗口作为常驻宿主）
- H.NotifyIcon.WinUI 的 TaskbarIcon 依赖 Window 的一次 Show 触发 `Loaded` 才注册图标；初始化时套 `WS_EX_LAYERED + alpha=0` 让窗口透明地完成这次 Show，避免 `--hide` 自启时可见闪烁
- C++ 启动器用 `argv[1..]` 重新拼接透传命令行，不依赖 `GetCommandLine` 的引号格式（裸 cmd 调用不会吃字符）

### 技术栈

| 层             | 技术                                                              |
| -------------- | ----------------------------------------------------------------- |
| UI 框架        | WinUI 3（Windows App SDK 1.8）                                    |
| 运行时         | .NET 10                                                           |
| 图形           | Win2D 1.3（D3D11 互操作、HDR 色调映射、直方图效果）               |
| 编解码         | Starward.Codec NuGet（libavif / libjxl / UltraHDR P/Invoke 封装） |
| 数据存储       | SQLite + Dapper                                                   |
| 日志           | Serilog                                                           |
| 托盘           | H.NotifyIcon.WinUI                                                |
| 缩略图         | Scighost.WinUI ImageEx + 自定义 CachedImage                       |
| 区域截图覆盖层 | Win2D CanvasControl（冻结帧渲染 + 选区绘制）                      |
| 剪贴板         | Win32 原生 API（OpenClipboard / SetClipboardData）                |
| 启动器         | C++ 原生（v145 工具集，静态 CRT）                                 |

### 重入保护

`Interlocked.CompareExchange` 全局守卫，全屏/区域/仅复制共用一个 `_isCapturing` 标志——键盘连触或快速连续按热键不会触发多次截图。

### 构建配置

|              | Debug                                       | Release                                                                                         |
| ------------ | ------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| .NET Runtime | Framework-dependent（不打包）               | 自包含                                                                                          |
| 原生库       | 仅 win-x64（RuntimeIdentifier，铺到输出根） | 同 Debug                                                                                        |
| Trim         | 否                                          | partial                                                                                         |
| ReadyToRun   | 否                                          | 是                                                                                              |
| 额外清理     | —                                           | 删除 DirectML.dll / onnxruntime.dll / NpuDetect（Windows App SDK 的 WinML/AI 组件，本应用不用） |
| 输出路径     | `build/app/`                                | `build/release/app/` + 启动器拷到 `build/release/`                                              |
| 大小         | ~80MB                                       | 更小（Trim + 删 AI 库）                                                                         |

## 从源码构建

### 环境要求

- Visual Studio 2022 / 2026（含 C++ 桌面开发、.NET 桌面开发）
- .NET 10 SDK
- Windows SDK 10.0.26100

### 步骤

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# 构建主程序（输出到 build/app/）
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# 构建启动器（输出到 build/Starshot.exe，需要 VS 的 MSBuild）
"C:\Program Files\Microsoft Visual Studio\<版本>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 运行：build/Starshot.exe（启动器）或 build/app/Starshot.exe（主程序）

# === Release 发布 ===
# 1. 先构建启动器（输出到 build/Starshot.exe）
"C:\Program Files\Microsoft Visual Studio\<版本>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. 发布主程序（输出到 build/release/app/，自动拷启动器到 build/release/Starshot.exe + 删 AI 库）
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# 完成后的目录结构：
# build/release/
#   Starshot.exe        ← 启动器（自动拷贝）
#   app/
#     Starshot.exe      ← 主程序（自包含 + trim + R2R）
#     *.dll / avifenc.exe 等
```

## 已知限制

- 区域截图覆盖层 HDR 帧显示为 SDR（WinUI CanvasControl 走 SDR 交换链）；保存的文件不受影响
- 自定义壁纸按 `UniformToFill` 铺满，但 WinUI 的裁剪不居中，目前是**左上**对齐，比如窄（竖向）壁纸在宽窗口里会只显示上半部分（从顶部裁剪而非居中）
- 区域截图覆盖层打开瞬间，光标仍是系统默认形状，**需移动一次鼠标后十字光标才出现**（WinUI `ProtectedCursor` 对已在元素上的静止指针不立即生效，移动一次触发 pointer 事件后即正常）
- 暂无版本管理 / 自动更新

## 致谢

- [Starward](https://github.com/Scighost/Starward) — 截图核心、编解码引擎、窗口框架均源自 Starward，由 [@Scighost](https://github.com/Scighost) 开发
- [ShareX](https://github.com/ShareX/ShareX) — 区域截图覆盖层的窗口检测和交互设计参考

## License

MIT
