<div align="center">

<img src="../src/logo.png" width="120" alt="Starshot Logo">

# Starshot

**Next-generation Windows-native HDR Screenshot Tool**

16bit Full-Pipeline Capture · Region Screenshot · AVIF / JPEG XL Encoding · Color Management

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../releases)

[Download](../../releases) · [Quick Start](#quick-start) · [Features](#features) · [Build from Source](#build-from-source)

**[简体中文](../README.md)** | **English** | **[繁體中文](README.zh-TW.md)** | **[日本語](README.ja.md)** | **[Français](README.fr.md)** | **[Русский](README.ru.md)** | **[Español](README.es.md)**

</div>

---

## Why Starshot

Windows built-in screenshot tools (Snipping Tool, Win+Shift+S) can only capture 8bit SDR images even on HDR displays — the system compositor compresses the 16bit HDR framebuffer, clipping highlights and narrowing the color gamut. Common third-party screenshot tools (ShareX, etc.) are likewise limited by the traditional GDI/BitBlt capture pipeline and cannot access HDR data.

Starshot directly acquires the display's raw `R16G16B16A16Float` scRGB framebuffer from the DXGI layer, fully preserving HDR luminance information (up to thousands of nits), and encodes it as 16bit HDR AVIF or JPEG XL with BT.2020 + PQ transfer function metadata. It also provides all the features you'd expect from a general-purpose screenshot tool: automatic SDR display downgrade, region capture, multi-format batch conversion, and more.

**Key Highlights**

- 🎯 **Lossless Full-Pipeline HDR** — 16bit throughout capture, encoding, and color management; no lossy tone mapping
- 🧠 **Smart HDR/SDR Detection** — maxCLL histogram distinguishes true HDR content from SDR content wrapped in HDR format
- ✂️ **Region Screenshot** — freeze-frame multi-monitor overlay with window detection + magnifier for precise selection
- 📋 **Native Clipboard** — Win32 native API writes directly to the clipboard, avoiding WinRT deferred rendering paste failures
- 🗂️ **Multi-Format Support** — AVIF / JPEG XL / UHDR JPEG / PNG, with batch conversion tool
- 📦 **Portable** — extract and run, no admin privileges required

<div align="center">
<table>
<tr>
<td align="center" width="50%">

**Other Tools**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="SDR screenshot showing clipped highlights and washed out colors">
</td>
<td align="center" width="50%">

**Starshot (Ultra HDR JPEG)**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Starshot Ultra HDR JPEG preserving full highlight detail via gain map">
</td>
</tr>
</table>
<sub>Footage from *Arknights: Endfield*</sub>
</div>
</br>

> [!NOTE]
> Ultra HDR JPEG is shown because GitHub does not support AVIF rendering. The original AVIF can be viewed [here](https://r2.cialo.site/endfield/3840x2160.dlaa.avif).

On SDR displays, Starshot automatically uses the standard SDR capture path as a general-purpose screenshot tool; on HDR displays, it is one of the few desktop capture solutions that fully preserves HDR data.

## System Requirements

- Windows 11 preferred for the best experience
- x64 architecture
- **HDR capture requires an HDR display** (automatically falls back to SDR path on SDR displays)

## Download

Download the archive from [Releases](../../releases), extract it, and run `Starshot.exe` in the root directory (the launcher automatically starts the main program under `app/`). No installation required — just extract and run.

## Quick Start

| Action                                                     | Default Shortcut |
| ---------------------------------------------------------- | ---------------- |
| Full-screen screenshot                                     | Alt+W            |
| Region screenshot (save file + copy to clipboard)          | Alt+Q            |
| Region copy only (copy to clipboard, no file saved)        | Alt+A            |

All shortcuts can be customized in Settings.

## Features

### HDR Capture Pipeline

Most screenshot tools can only capture 8bit SDR even on HDR displays — the system compositor's 16bit floating-point scRGB framebuffer is crushed to SDR, clipping highlights and narrowing the color gamut. Starshot captures the **raw HDR framebuffer**:

1. **HDR Capture**: When the display reports HDR, requests `R16G16B16A16Float` pixel format to obtain full scRGB floating-point data (luminance up to thousands of nits)
2. **HDR Save**: 16bit AVIF / JPEG XL with BT.2020 gamut + PQ transfer function. No highlight clipping, no gamut reduction
3. **maxCLL Calculation**: Win2D histogram effect computes maximum content light level to distinguish true HDR content from HDR-formatted SDR content
4. **Color Management**: Reads the display ICC profile to parse real gamut primaries and embeds cICP/ICC chunks in output files. HDR is forced to BT.2020; SDR can be toggled (on = read ICC real gamut, off = BT.709)

#### SDR Content Handling

On HDR displays, the desktop and SDR applications are also captured in HDR format (R16G16B16A16Float), but the actual content luminance is at SDR levels. Starshot handles this as follows:

- **Default**: Still saved in HDR format (16bit), with **no 8bit tone mapping**, avoiding degradation and color shifts
- **Delete HDR for SDR Content** toggle (optional): When enabled, detects maxCLL threshold — if content falls below it, automatically converts to SDR (using the user's configured SDR save format) and deletes the HDR file, saving space

#### UHDR JPEG Fallback

HDR screenshots can also produce an Ultra HDR JPEG (SDR base image + HDR gain map), which displays correctly even in software without HDR support. Encoded via the `UhdrEncoder` in `Starward.Codec`.

#### Region Screenshot HDR Trade-off

The region screenshot overlay **intentionally** tone-maps HDR frames to SDR for display — because WinUI `CanvasControl` uses an SDR swap chain, and scRGB floating-point output directly would appear discolored or black. **Saved files are full HDR**, untouched; highlight clamping during selection only affects the preview, not the output.

### Three Capture Modes

| Mode            | Target                                        | Clipboard Format         | File Saved |
| --------------- | --------------------------------------------- | ------------------------ | ---------- |
| Full-screen     | Entire display (foreground window / cursor display, switchable) | CF_HDROP (file)          | Yes        |
| Region          | Drag-select / click window                    | CF_DIB (BGRA bitmap)     | Yes        |
| Region Copy Only| Drag-select / click window                    | CF_DIB (BGRA bitmap)     | No         |

All three modes share HDR detection, color management, filename templates, save pipeline, and info toast.

### Region Screenshot Overlay

- **Freeze Frame**: Captures all displays into a single bitmap first; the overlay displays a frozen frame — the screen doesn't move while selecting, and the overlay itself is not in the screenshot
- **Multi-Monitor**: Covers the entire virtual screen; magnifier and coordinate box are clamped to the cursor's display (no cross-monitor bleed)
- **Window Detection**: EnumWindows + DWM cloaked/toolwindow filtering + DWM extended frame bounds (remove shadow) + client area dual-candidate + Z-order selection; click a window to capture it directly (QuickCrop)
- **Magnifier**: NearestNeighbor integer-aligned + pixel grid (15×15 pixels, 10px each), pixels clearly distinguishable
- **Animated Marching Ants + Live Coordinates**: Selection X/Y/W/H + cursor physical coordinates
- **Pixel Precision**: Drag-select +1px, window rectangle +0
- ESC / Right-click to cancel, Enter to confirm window hover

### Clipboard

The WinRT `Clipboard.SetContent` call from unpackaged WinUI apps is unreliable (deferred rendering + flush issues, content often fails to reach other apps). Starshot uses Win32 native APIs (`OpenClipboard` / `SetClipboardData`) directly:

- **Full-screen**: CF_HDROP (file drag-drop format) — paste into Explorer/chat apps and get a file directly
- **Region**: CF_DIB (BGRA bitmap) — the SDR bitmap cropped from the overlay goes straight to the clipboard, no file read, no re-encode, no secondary tone mapping
- Callable from any thread, 10×20ms retries when clipboard is locked

### Save

- **Flat structure** (no subfolders), default `Pictures\Starshot`, customizable
- **SDR format** (PNG / AVIF / JPEG XL, default PNG) and **HDR format** (AVIF / JPEG XL, default AVIF) set independently
- Quality: Medium / High / Lossless
- XMP metadata (CreatorTool = Starshot)
- Serialized encoding (SemaphoreSlim) to prevent concurrent encoding conflicts
- **Storage Stats**: Settings page shows disk usage for screenshots / thumbnail cache / wallpapers / logs, with refresh and one-click cache cleanup (also cleans orphaned wallpaper files)

#### Supported Formats

| Format    | Bit Depth             | HDR Support                   | Use Case                    |
| --------- | --------------------- | ----------------------------- | --------------------------- |
| PNG       | 8bit / 16bit          | Can save HDR but poor compat  | SDR default, lossless       |
| AVIF      | 8bit / 10bit / 12bit  | Full HDR                      | HDR default, high compression|
| JPEG XL   | 8bit / 16bit          | Full HDR                      | HDR alternative, reversible |
| UHDR JPEG | 8bit + gain map       | SDR-compatible HDR fallback   | HDR bonus output            |

### Filename Templates

Full-screen and region screenshots use **independent templates**.

| Placeholder                                               | Meaning                              | Example             |
| --------------------------------------------------------- | ------------------------------------ | ------------------- |
| `{process}`                                               | Process name (without extension)     | `explorer`          |
| `{processPath}`                                           | EXE filename (with extension)        | `explorer.exe`      |
| `{title}`                                                 | Window title (trimmed, truncatable)  | `Genshin Impact`    |
| `{timestamp}`                                             | Unix timestamp                       | `1721234567`        |
| `{time}`                                                  | yyyyMMdd_HHmmssff                    | `20260718_14302512` |
| `{date}`                                                  | yyyyMMdd                             | `20260718`          |
| `{width}` `{height}`                                      | Image dimensions (px)                | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}` | Individual time components           |                     |

Illegal filename characters are uniformly replaced with `_`.

### Info Toast

After a screenshot, a thumbnail + status toast pops up (does not affect screenshots — set with `WDA_EXCLUDEFROMCAPTURE` so other capture tools cannot see this window):

- **Processing** (spinner) / **Saved** (with open button) / **Copied** (green checkmark) / **Failed**
- Multi-capture counter (e.g. 2/3)
- Composition animation slide-in / slide-out

### Screenshot Library

- Multi-folder browsing (default screenshot directory + user-added folders)
- `FileSystemWatcher` detects additions/deletions in real time
- Grouped by date, lazy-loaded thumbnails
- Right-click menu: Open / Copy File / Copy as JPG / Open in Explorer / Open With / Delete
- Multi-select + drag-out + batch conversion entry

### Image Viewer

- Zoom (slider / buttons / mouse wheel / double-click fit), fullscreen mode (F11)
- Previous / Next (arrow keys, mouse wheel, bottom thumbnail strip)
- Drag and drop files to open
- Right-click menu: Copy File / Path / Image, Delete, Open in Explorer, Open With
- **Edit Panel**: HDR / SDR / Auto display mode toggle, SDR brightness slider (100–500 nits), image and display information
- **Format Conversion**: Export to PNG / AVIF / JPEG XL (SDR display) or UHDR JPEG / AVIF / JPEG XL (HDR display)
- **Color Management**: Reads display ICC profile and AdvancedColorInfo

### Batch Format Conversion

| Conversion Direction                  | Engine                                 |
| ------------------------------------- | -------------------------------------- |
| JPG / PNG → AVIF / JXL                | avifenc.exe / cjxl.exe (CLI)           |
| AVIF / JXL → JPG / PNG                | avifdec.exe / djxl.exe (CLI)           |
| JXR / WEBP / HEIC etc. → AVIF / JXL   | In-process ImageSaver (avifEncoderLite)|

### Personalization

- **Custom Wallpaper**: Three modes
  - **Fixed Image**: Pick an image, displayed fixed
  - **Fixed Video**: Looped muted playback, auto-pauses when main window is hidden
  - **Folder Random**: Randomly pick one (image or video) from a folder on each launch
  - Automatic detection of missing wallpaper sources, config cleanup, and fallback to no wallpaper + toast notification
- **Accent Color**:
  - **Auto-pick from wallpaper** (on by default): Samples wallpaper dominant color as app accent (HSV saturation boost); videos sample first frame only to avoid color flickering
  - **Custom color**: Manual color picker overrides auto-pick
- **Theme**: Follow system / Light / Dark
- **Acrylic Effect**: In wallpaper mode, choose frosted-glass layer or direct wallpaper transparency

### Splash Screen

Displays logo + tagline at startup, with a 700ms delay followed by a 400ms fade-out. Triggers only on first window open; does not replay when restored from tray.

### System Tray

- Left-click shows main window, right-click context menu (Show / Exit)
- Closing the main window minimizes to tray (toggleable)
- `ForceExit` mechanism ensures tray "Exit" truly quits

### Auto-start on Boot

- Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, pointing to the launcher (root `Starshot.exe`)
- Optional `--hide` flag to start minimized to tray (requires tray enabled)

## Architecture

### Directory Structure

```
Root/
  Starshot.exe            ← C++ launcher (~400KB, launches app/ main program)
  StarshotDatabase.db     ← SQLite settings database
  app/
    Starshot.exe          ← Main program (WinUI 3 / .NET 10)
    *.dll                 ← Dependencies
    avifenc.exe etc.       ← Codec tools (from Starward.Codec NuGet)
%LOCALAPPDATA%/Starshot/  (default, configurable)
  log/                    ← Logs
  cache/                  ← Thumbnail cache
```

### Launcher

Native C++ program (~400KB), currently hardcoded to `app/Starshot.exe`. Future plans include `version.ini` support for versioned directories + automatic old version cleanup.

### Tech Stack

| Layer              | Technology                                                            |
| ------------------ | --------------------------------------------------------------------- |
| UI Framework       | WinUI 3 (Windows App SDK 1.8)                                         |
| Runtime            | .NET 10                                                               |
| Graphics           | Win2D 1.3 (D3D11 interop, HDR tone mapping, histogram effect)         |
| Codec              | Starward.Codec NuGet (libavif / libjxl / UltraHDR P/Invoke wrapper)   |
| Data Storage       | SQLite + Dapper                                                       |
| Logging            | Serilog                                                               |
| System Tray        | H.NotifyIcon.WinUI                                                    |
| Thumbnails         | Scighost.WinUI ImageEx + custom CachedImage                           |
| Region Overlay     | Win2D CanvasControl (freeze-frame rendering + selection drawing)      |
| Clipboard          | Win32 native API (OpenClipboard / SetClipboardData)                   |
| Launcher           | Native C++ (v145 toolset, static CRT)                                 |

### Re-entrancy Protection

`Interlocked.CompareExchange` global guard — full-screen, region, and copy-only modes share a single `_isCapturing` flag, preventing multiple captures from rapid keystrokes or consecutive hotkey presses.

### Build Configuration

|              | Debug                                       | Release                                                                                          |
| ------------ | ------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| .NET Runtime | Framework-dependent (not bundled)           | Self-contained                                                                                   |
| Native Libs  | win-x64 only (RuntimeIdentifier, flat to output root) | Same as Debug                                                                           |
| Trim         | No                                          | Partial                                                                                          |
| ReadyToRun   | No                                          | Yes                                                                                              |
| Extra Cleanup| —                                           | Delete DirectML.dll / onnxruntime.dll / NpuDetect (Windows App SDK WinML/AI components, not used)|
| Output Path  | `build/app/`                                | `build/release/app/` + launcher copied to `build/release/`                                       |
| Size         | ~80MB                                       | Smaller (Trim + AI libs removed)                                                                 |

## Build from Source

### Prerequisites

- Visual Studio 2022 / 2026 (with C++ Desktop Development, .NET Desktop Development)
- .NET 10 SDK
- Windows SDK 10.0.26100

### Steps

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# Build main program (output to build/app/)
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# Build launcher (output to build/Starshot.exe, requires VS MSBuild)
"C:\Program Files\Microsoft Visual Studio\<version>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# Run: build/Starshot.exe (launcher) or build/app/Starshot.exe (main program)

# === Release Publish ===
# 1. Build launcher first (output to build/Starshot.exe)
"C:\Program Files\Microsoft Visual Studio\<version>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. Publish main program (output to build/release/app/, auto-copies launcher to build/release/Starshot.exe + removes AI libs)
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# Resulting directory structure:
# build/release/
#   Starshot.exe        ← Launcher (auto-copied)
#   app/
#     Starshot.exe      ← Main program (self-contained + trimmed + R2R)
#     *.dll / avifenc.exe etc.
```

## Known Limitations

- Region screenshot overlay displays HDR frames as SDR (WinUI CanvasControl uses SDR swap chain); saved files are unaffected
- Custom wallpaper uses `UniformToFill` fill, but WinUI's crop is not centered — currently **top-left** aligned, e.g. a narrow (portrait) wallpaper on a wide window will only show the upper portion (cropped from top, not center)
- When the region screenshot overlay opens, the cursor remains the system default shape; **move the mouse once** to make the crosshair cursor appear (WinUI `ProtectedCursor` does not immediately apply to a stationary pointer already over the element — moving once triggers a pointer event, after which it works normally)
- No version management / auto-update yet

## Acknowledgements

- [Starward](https://github.com/Scighost/Starward) — Screenshot core, codec engine, and window framework are all derived from Starward, developed by [@Scighost](https://github.com/Scighost)
- [ShareX](https://github.com/ShareX/ShareX) — Reference for region screenshot overlay window detection and interaction design

## License

MIT
