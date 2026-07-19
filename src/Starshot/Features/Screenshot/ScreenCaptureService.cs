using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Display;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Starward.Codec.ICC;
using Starshot.Features.Codec;
using Starshot.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage;

namespace Starshot.Features.Screenshot;

internal class ScreenCaptureService
{


    private static ScreenCaptureService? _instance;
    private static ScreenCaptureService Instance => _instance ??= AppConfig.GetService<ScreenCaptureService>();

    private readonly ILogger<ScreenCaptureService> _logger;

    private ScreenCaptureInfoWindow? _infoWindow;

    private static SemaphoreSlim _encodeSlim = new(1);

    private static int _isCapturing;


    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }



    public static void Capture()
    {
        Instance.CaptureInternal();
    }



    /// <summary>
    /// 获取窗口所在桌面的高级色彩信息
    /// </summary>
    public static DisplayInformation GetDisplayInformationFromWindowHandle(nint hwnd)
    {
        HMONITOR monitor = User32.MonitorFromWindow(hwnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
        return DisplayInformation.CreateForDisplayId(new((ulong)monitor.DangerousGetHandle()));
    }


    // 逻辑直接取自 ScreenshotSetting.TestCaptureAsync（设置页测试截图按钮），
    // 仅替换：窗口句柄来源（前台窗口）、配置项来源（AppConfig）、输出目录（进程名子目录），并去掉 UI 部分。
    private async void CaptureInternal()
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        bool guardReleased = false;
        nint hwnd = (nint)User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            Interlocked.Exchange(ref _isCapturing, 0);
            return;
        }
        string processName = GetProcessNameFromWindowHandle(hwnd);
        string processExeName = GetProcessExeNameFromWindowHandle(hwnd);
        bool captureStarted = false;
        try
        {
            HMONITOR monitor;
            if ((CaptureMonitorSource)AppConfig.ScreenshotCaptureMonitorSource is CaptureMonitorSource.Cursor)
            {
                User32.GetCursorPos(out var pt);
                monitor = User32.MonitorFromPoint(pt, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            }
            else
            {
                monitor = User32.MonitorFromWindow(hwnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            }
            Microsoft.UI.DisplayId displayId = new((ulong)monitor.DangerousGetHandle());
            using DisplayInformation displayInfo = DisplayInformation.CreateForDisplayId(displayId);
            DisplayAdvancedColorInfo colorInfo = displayInfo.GetAdvancedColorInfo();
            DirectXPixelFormat pixelFormat = colorInfo.CurrentAdvancedColorKind is DisplayAdvancedColorKind.HighDynamicRange ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.R8G8B8A8UIntNormalized;
            using Direct3D11CaptureFrame frame = await ScreenCaptureHelper.CaptureMonitorAsync(monitor.DangerousGetHandle(), pixelFormat);
            DateTimeOffset frameTime = DateTimeOffset.Now;
            using CanvasBitmap canvasBitmap = CanvasBitmap.CreateFromDirect3D11Surface(CanvasDevice.GetSharedDevice(), frame.Surface, 96);

            float maxCLL = -1;
            float sdrWhiteLevel = 80;
            bool hdr = canvasBitmap.Format is DirectXPixelFormat.R16G16B16A16Float;
            if (hdr)
            {
                maxCLL = GetMaxCLL(canvasBitmap);
                sdrWhiteLevel = (float)colorInfo.SdrWhiteLevelInNits;
            }

            _infoWindow ??= new ScreenCaptureInfoWindow();
            _infoWindow.CaptureStart(displayId, canvasBitmap, maxCLL);
            captureStarted = true;

            string windowTitle = "";
            try
            {
                int titleLen = User32.GetWindowTextLength(hwnd);
                if (titleLen > 0)
                {
                    var titleSb = new StringBuilder(titleLen + 1);
                    User32.GetWindowText(hwnd, titleSb, titleLen + 1);
                    windowTitle = titleSb.ToString();
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to get window title for hwnd {Hwnd}", hwnd); }

            // 守卫只挡"抓帧"；编码慢且已由 _encodeSlim 单独串行，这里放守卫让下一次按下能立刻进
            Interlocked.Exchange(ref _isCapturing, 0);
            guardReleased = true;

            await SaveCaptureAsync(canvasBitmap, processName, processExeName, windowTitle, frameTime, displayInfo, maxCLL, sdrWhiteLevel, displayId);
            _logger.LogInformation("Screenshot saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while capturing the screen.");
            _infoWindow ??= new ScreenCaptureInfoWindow();
            _infoWindow.CaptureError(hwnd, captureStarted);
        }
        finally
        {
            if (!guardReleased)
            {
                Interlocked.Exchange(ref _isCapturing, 0);
            }
        }
    }


    // ==================== 区域截图 ====================

    public static void CaptureRegion()
    {
        Instance.CaptureRegionInternal();
    }


    /// <summary>
    /// 仅复制：区域选区 → 只进剪贴板，不存文件、不弹信息窗、无视"自动复制"开关
    /// </summary>
    public static void CaptureRegionCopyOnly()
    {
        Instance.CaptureRegionInternal(copyOnly: true);
    }


    private async void CaptureRegionInternal(bool copyOnly = false)
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0) return;
        bool guardReleased = false;
        _logger.LogInformation(copyOnly ? "Region copy-only triggered" : "Region capture triggered");
        CanvasRenderTarget composite = null;
        CanvasRenderTarget cropped = null;
        CanvasRenderTarget sdrCrop = null;  // 覆盖层裁出的 SDR 选区（剪贴板用）
        try
        {
            // 虚拟屏幕边界（物理像素）
            int vx = User32.GetSystemMetrics((User32.SystemMetric)76);
            int vy = User32.GetSystemMetrics((User32.SystemMetric)77);
            int vw = User32.GetSystemMetrics((User32.SystemMetric)78);
            int vh = User32.GetSystemMetrics((User32.SystemMetric)79);
            if (vw <= 0 || vh <= 0) return;

            // 枚举所有显示器（for 循环，不用 foreach 避免 CsWinRT 枚举器 bug）
            var displays = DisplayArea.FindAll();
            _logger.LogInformation("Region capture: {count} displays found", displays.Count);
            if (displays.Count == 0) return;

            // 判断是否有 HDR
            bool anyHDR = false;
            for (int i = 0; i < displays.Count; i++)
            {
                using var di = DisplayInformation.CreateForDisplayId(displays[i].DisplayId);
                if (di.GetAdvancedColorInfo().CurrentAdvancedColorKind is DisplayAdvancedColorKind.HighDynamicRange)
                {
                    anyHDR = true;
                    break;
                }
            }
            var pixelFormat = anyHDR ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.R8G8B8A8UIntNormalized;

            // 并行捕获所有显示器（同时启动所有 GraphicsCaptureSession，等全部帧到达）
            var device = CanvasDevice.GetSharedDevice();
            var captureTasks = new Task<(int ox, int oy, CanvasBitmap bmp)>[displays.Count];
            for (int i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                var bounds = d.OuterBounds;
                int ox = bounds.X - vx;
                int oy = bounds.Y - vy;
                captureTasks[i] = Task.Run(async () =>
                {
                    using var frame = await ScreenCaptureHelper.CaptureMonitorAsync((nint)d.DisplayId.Value, pixelFormat);
                    var bmp = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface, 96);
                    return (ox, oy, bmp);
                });
            }
            var results = await Task.WhenAll(captureTasks);

            // 合成到虚拟屏幕大小的 CanvasRenderTarget
            composite = new CanvasRenderTarget(device, vw, vh, 96, pixelFormat, CanvasAlphaMode.Premultiplied);
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                using (var ds = composite.CreateDrawingSession())
                {
                    ds.DrawImage(r.bmp, r.ox, r.oy);
                }
                r.bmp.Dispose();
            }

            // DPI scale
            nint fgHwnd = (nint)User32.GetForegroundWindow();
            float dpi = User32.GetDpiForWindow(fgHwnd);
            float scale = dpi / 96f;

            // SDR 白电平（用于覆盖层 HDR→SDR 色调映射）
            float sdrWhiteLevel = anyHDR ? GetSdrWhiteLevelFromDisplays(displays) : 80;

            // 弹覆盖层，等用户选区
            _logger.LogInformation("Region capture: showing overlay window");
            var tcs = new TaskCompletionSource<bool>();
            var window = new RegionCaptureWindow(composite, scale, sdrWhiteLevel, vw, vh);
            window.Closed += (s, e) => tcs.TrySetResult(window.IsConfirmed);
            window.Show();

            bool confirmed = await tcs.Task;

            if (!confirmed || window.SelectionRect.Width < 2 || window.SelectionRect.Height < 2)
            {
                return; // 取消
            }

            // 覆盖层关窗时已从 _displayBitmap（tonemap 好的 SDR）裁出选区
            sdrCrop = window.SdrCrop;

            if (copyOnly)
            {
                // 仅复制：不存文件、无视"自动复制"开关
                await CopyCaptureToClipboardAsync(sdrCrop, force: true);
                var mon = User32.MonitorFromWindow(fgHwnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
                var dispId = new Microsoft.UI.DisplayId((ulong)mon.DangerousGetHandle());
                _infoWindow ??= new ScreenCaptureInfoWindow();
                _infoWindow.CaptureCopySuccess(dispId, sdrCrop);
                _logger.LogInformation("Region copy-only done");
                return;
            }

            // 裁剪 HDR 选区用于保存（用窗口提供的物理像素坐标）
            var srcRect = window.GetPhysicalSourceRect();
            int cx = (int)srcRect.X;
            int cy = (int)srcRect.Y;
            int cw = (int)srcRect.Width;
            int ch = (int)srcRect.Height;
            cropped = new CanvasRenderTarget(device, cw, ch, 96, pixelFormat, CanvasAlphaMode.Premultiplied);
            using (var ds = cropped.CreateDrawingSession())
            {
                ds.DrawImage(composite, 0, 0, new Windows.Foundation.Rect(cx, cy, cw, ch));
            }

            // ===== 走保存管线 =====
            DateTimeOffset frameTime = DateTimeOffset.Now;
            bool hdr = cropped.Format is DirectXPixelFormat.R16G16B16A16Float;
            float maxCLL = hdr ? GetMaxCLL(cropped) : -1;

            string processName = GetProcessNameFromWindowHandle(fgHwnd);
            string processExeName = GetProcessExeNameFromWindowHandle(fgHwnd);
            string windowTitle = "";
            try
            {
                int len = User32.GetWindowTextLength(fgHwnd);
                if (len > 0)
                {
                    var sb = new System.Text.StringBuilder(len + 1);
                    User32.GetWindowText(fgHwnd, sb, len + 1);
                    windowTitle = sb.ToString();
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to get foreground window title"); }
            var fgMonitor = User32.MonitorFromWindow(fgHwnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            Microsoft.UI.DisplayId regionDisplayId = new((ulong)fgMonitor.DangerousGetHandle());
            using DisplayInformation regionDisplayInfo = DisplayInformation.CreateForDisplayId(regionDisplayId);

            _infoWindow ??= new ScreenCaptureInfoWindow();
            _infoWindow.CaptureStart(regionDisplayId, cropped, maxCLL);

            // 守卫只挡"抓帧+选区"；编码慢且已由 _encodeSlim 单独串行，这里放守卫让下一次按下能立刻进
            Interlocked.Exchange(ref _isCapturing, 0);
            guardReleased = true;

            // 选好后并行：保存（完整 HDR，不内嵌剪贴板）+ 直接从覆盖层裁好的 SDR 选区复制
            await Task.WhenAll(
                SaveCaptureAsync(cropped, processName, processExeName, windowTitle, frameTime, regionDisplayInfo, maxCLL, sdrWhiteLevel, regionDisplayId, true, copyToClipboard: false),
                CopyCaptureToClipboardAsync(sdrCrop));
            _logger.LogInformation("Region screenshot saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Region capture failed");
        }
        finally
        {
            cropped?.Dispose();
            sdrCrop?.Dispose();
            composite?.Dispose();
            if (!guardReleased)
            {
                Interlocked.Exchange(ref _isCapturing, 0);
            }
        }
    }



    // ==================== 共用保存管线 ====================

    private async Task SaveCaptureAsync(CanvasBitmap bitmap, string processName, string processExeName,
        string windowTitle, DateTimeOffset frameTime, DisplayInformation displayInfo,
        float maxCLL, float sdrWhiteLevel, Microsoft.UI.DisplayId displayId, bool isRegion = false, bool copyToClipboard = true)
    {
        bool hdr = bitmap.Format is DirectXPixelFormat.R16G16B16A16Float;
        bool writeColorProfile = AppConfig.EnableScreenshotColorManagement || hdr;
        int quality = AppConfig.ScreenCaptureEncodeQuality switch { 0 => 80, 1 => 90, 2 => 100, _ => 90 };
        float distance = AppConfig.ScreenCaptureEncodeQuality switch { 0 => 2, 1 => 1, 2 => 0, _ => 1 };

        byte[] xmpData = BuildXMPMetadata(frameTime);
        ColorPrimaries colorPrimaries;
        if (hdr)
        {
            colorPrimaries = ColorPrimaries.BT2020;
        }
        else if (!AppConfig.EnableScreenshotColorManagement)
        {
            colorPrimaries = ColorPrimaries.BT709;
        }
        else
        {
            colorPrimaries = await GetColorPrimariesFromDisplayInformationAsync(displayInfo);
        }

        string? targetFolder = AppConfig.ScreenshotFolder;
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            targetFolder = Path.Join(AppConfig.UserDataFolder, "Screenshots");
        }
        string screenshotFolder = Path.GetFullPath(targetFolder);
        Directory.CreateDirectory(screenshotFolder);

        string extension = hdr
            ? (AppConfig.ScreenCaptureHDRFormat switch { 1 => "jxl", _ => "avif" })
            : (AppConfig.ScreenCaptureSDRFormat switch { 1 => "avif", 2 => "jxl", _ => "png" });

        string filePath = Path.Combine(screenshotFolder,
            $"{BuildFileName(processName, processExeName, windowTitle, frameTime, bitmap.SizeInPixels.Width, bitmap.SizeInPixels.Height, isRegion ? AppConfig.RegionScreenshotFileNamePattern : null)}.{extension}");

        using MemoryStream ms = new();
        await _encodeSlim.WaitAsync();
        try
        {
            if (extension is "png")
            {
                ColorPrimaries cp = (!hdr && !AppConfig.EnableScreenshotColorManagement) ? ColorPrimaries.BT709 : colorPrimaries;
                await ImageSaver.SaveAsPngAsync(bitmap, ms, cp, xmpData, writeColorProfile);
            }
            else if (extension is "avif")
            {
                ColorPrimaries cp = (!hdr && !AppConfig.EnableScreenshotColorManagement) ? ColorPrimaries.BT709 : colorPrimaries;
                await ImageSaver.SaveAsAvifAsync(bitmap, ms, cp, quality, xmpData, writeColorProfile);
            }
            else if (extension is "jxl")
            {
                ColorPrimaries cp = (!hdr && !AppConfig.EnableScreenshotColorManagement) ? ColorPrimaries.BT709 : colorPrimaries;
                await ImageSaver.SaveAsJxlAsync(bitmap, ms, cp, distance, xmpData, writeColorProfile);
            }
            else
            {
                throw new NotSupportedException($"Unsupported image format: {extension}");
            }
        }
        finally
        {
            _encodeSlim.Release();
        }

        using (var fs = File.Create(filePath))
        {
            ms.Seek(0, SeekOrigin.Begin);
            await ms.CopyToAsync(fs);
        }

        bool contentIsHDR = hdr && maxCLL > sdrWhiteLevel + 5;
        bool deleteHDR = hdr && AppConfig.DeleteHDRIfSDRContent && !contentIsHDR;
        bool autoConvertSDR = hdr && AppConfig.AutoConvertScreenshotToSDR && !deleteHDR;
        bool produceSDR = deleteHDR || autoConvertSDR;

        string? sdrPath = null;
        if (deleteHDR)
        {
            // 内容实为 SDR：tonemap 到 R8G8B8A8，按用户 SDR 格式存（BT709），稍后删 HDR 文件
            using CanvasRenderTarget sdrBitmap = TonemapToSdr(bitmap, sdrWhiteLevel);
            string sdrExt = AppConfig.ScreenCaptureSDRFormat switch { 1 => "avif", 2 => "jxl", _ => "png" };
            sdrPath = Path.ChangeExtension(filePath, sdrExt);
            using var ms2 = new MemoryStream();
            await _encodeSlim.WaitAsync();
            try
            {
                if (sdrExt is "png")
                    await ImageSaver.SaveAsPngAsync(sdrBitmap, ms2, ColorPrimaries.BT709, xmpData, false);
                else if (sdrExt is "avif")
                    await ImageSaver.SaveAsAvifAsync(sdrBitmap, ms2, ColorPrimaries.BT709, quality, xmpData, false);
                else
                    await ImageSaver.SaveAsJxlAsync(sdrBitmap, ms2, ColorPrimaries.BT709, distance, xmpData, false);
            }
            finally
            {
                _encodeSlim.Release();
            }
            ms2.Position = 0;
            using var fs2 = File.Create(sdrPath);
            await ms2.CopyToAsync(fs2);
        }
        else if (autoConvertSDR)
        {
            // 内容真为 HDR：额外存一份 UHDR JPEG（SDR 基图 + HDR gain map）
            using var ms2 = new MemoryStream();
            await ImageSaver.SaveAsUhdrAsync(bitmap, ms2, maxCLL, sdrWhiteLevel);
            ms2.Position = 0;
            sdrPath = Path.ChangeExtension(filePath, ".jpg");
            using var fs2 = File.Create(sdrPath);
            await ms2.CopyToAsync(fs2);
        }

        string finalFile = filePath;
        if (deleteHDR)
        {
            File.Delete(filePath);
            finalFile = sdrPath!;
        }

        if (copyToClipboard && AppConfig.AutoCopyScreenshotToClipboard)
        {
            // 全屏截图：直接把文件放剪贴板（CF_HDROP）。开了转换复制 uhdr jpg，否则主文件
            string clipFile = produceSDR ? sdrPath! : finalFile;
            ClipboardHelper.SetFiles(clipFile);
        }

        _infoWindow?.CaptureSuccess(displayId, bitmap, finalFile, maxCLL);
    }


    /// <summary>
    /// HDR（R16G16B16A16Float scRGB）→ SDR（R8G8B8A8）色调映射。
    /// WhiteLevelAdjustment(80→sdrWhiteLevel) + SrgbGamma(OETF)，与覆盖层显示用 tonemap 同逻辑。
    /// </summary>
    private static CanvasRenderTarget TonemapToSdr(CanvasBitmap hdrBitmap, float sdrWhiteLevel)
    {
        var device = CanvasDevice.GetSharedDevice();
        int w = (int)hdrBitmap.SizeInPixels.Width;
        int h = (int)hdrBitmap.SizeInPixels.Height;
        var sdr = new CanvasRenderTarget(device, w, h, 96, DirectXPixelFormat.R8G8B8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var ds = sdr.CreateDrawingSession())
        {
            var wle = new WhiteLevelAdjustmentEffect
            {
                Source = hdrBitmap,
                InputWhiteLevel = 80,
                OutputWhiteLevel = sdrWhiteLevel,
                BufferPrecision = CanvasBufferPrecision.Precision16Float,
            };
            var gamma = new SrgbGammaEffect
            {
                Source = wle,
                GammaMode = SrgbGammaMode.OETF,
                BufferPrecision = CanvasBufferPrecision.Precision16Float,
            };
            ds.DrawImage(gamma);
        }
        return sdr;
    }


    /// <summary>
    /// 直接从内存 SDR 位图复制到剪贴板（Win32 CF_DIB，不读文件、不经 WinRT DataPackage）。
    /// 输入需已是 SDR（区域截图用覆盖层裁好的 SdrCrop）。与保存流程平级、独立，可复用。
    /// </summary>
    public static async Task CopyCaptureToClipboardAsync(CanvasBitmap sdrBitmap, bool force = false)
    {
        var log = AppConfig.GetLogger<ScreenCaptureService>();
        if (!force && !AppConfig.AutoCopyScreenshotToClipboard)
        {
            log.LogInformation("CopyCaptureToClipboardAsync: skipped (disabled)");
            return;
        }
        await Task.Run(() =>
        {
            try
            {
                // CF_DIB 要 BGRA；非 B8G8R8A8 的 SDR（如 R8G8B8A8）拷贝转换，不做 tonemap
                CanvasBitmap src = sdrBitmap;
                CanvasRenderTarget? converted = null;
                if (src.Format != DirectXPixelFormat.B8G8R8A8UIntNormalized)
                {
                    converted = ConvertToBgra(src);
                    src = converted;
                }
                try
                {
                    int w = (int)src.SizeInPixels.Width;
                    int h = (int)src.SizeInPixels.Height;
                    byte[] pixels = src.GetPixelBytes(); // BGRA top-down
                    log.LogInformation("CopyCaptureToClipboardAsync: DIB {W}x{H}, {Len} bytes", w, h, pixels.Length);
                    ClipboardHelper.SetBitmapDib(w, h, pixels);
                    log.LogInformation("CopyCaptureToClipboardAsync: SetClipboardData done");
                }
                finally
                {
                    converted?.Dispose();
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Copy capture to clipboard failed");
            }
        });
    }


    /// <summary>SDR 位图 → B8G8R8A8（仅通道顺序转换，无 tonemap）</summary>
    private static CanvasRenderTarget ConvertToBgra(CanvasBitmap src)
    {
        uint w = src.SizeInPixels.Width;
        uint h = src.SizeInPixels.Height;
        var device = CanvasDevice.GetSharedDevice();
        var rt = new CanvasRenderTarget(device, w, h, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var ds = rt.CreateDrawingSession())
        {
            ds.DrawImage(src);
        }
        return rt;
    }


    private static float GetSdrWhiteLevelFromDisplays(IReadOnlyList<DisplayArea> displays)
    {
        for (int i = 0; i < displays.Count; i++)
        {
            using var di = DisplayInformation.CreateForDisplayId(displays[i].DisplayId);
            var info = di.GetAdvancedColorInfo();
            if (info.CurrentAdvancedColorKind is DisplayAdvancedColorKind.HighDynamicRange)
            {
                return (float)info.SdrWhiteLevelInNits;
            }
        }
        return 80;
    }


    private static string GetProcessNameFromWindowHandle(nint hwnd)
    {
        try
        {
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0)
            {
                return Process.GetProcessById((int)pid).ProcessName;
            }
        }
        catch { }
        return "capture";
    }


    /// <summary>
    /// 进程可执行文件名（带扩展名，如 Game.exe）；取不到则回退到不含扩展名的进程名
    /// </summary>
    private static string GetProcessExeNameFromWindowHandle(nint hwnd)
    {
        try
        {
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                string name = proc.ProcessName;
                try
                {
                    var fileName = proc.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        name = Path.GetFileName(fileName);
                    }
                }
                catch { }
                return name;
            }
        }
        catch { }
        return "capture";
    }



    public static string BuildFileName(string processName, string processPath, string title, DateTimeOffset time, uint width, uint height, string? pattern = null)
    {
        pattern ??= AppConfig.ScreenshotFileNamePattern;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = "{process}_{time}";
        }
        // 窗口标题：去首尾空白 + 按设置截断（非法字符由后续 sanitize 统一替换为 _）
        title = title.Trim();
        int maxLen = AppConfig.ScreenshotFileNameTitleMaxLength;
        if (maxLen > 0 && title.Length > maxLen)
        {
            title = title[..maxLen];
        }
        var name = pattern
            .Replace("{process}", processName)
            .Replace("{processPath}", processPath)
            .Replace("{title}", title)
            .Replace("{timestamp}", time.ToUnixTimeSeconds().ToString())
            .Replace("{width}", width.ToString())
            .Replace("{height}", height.ToString())
            .Replace("{time}", time.ToString("yyyyMMdd_HHmmssff"))
            .Replace("{date}", time.ToString("yyyyMMdd"))
            .Replace("{year}", time.ToString("yyyy"))
            .Replace("{month}", time.ToString("MM"))
            .Replace("{day}", time.ToString("dd"))
            .Replace("{hour}", time.ToString("HH"))
            .Replace("{minute}", time.ToString("mm"))
            .Replace("{second}", time.ToString("ss"));
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }



    public static async Task<ColorPrimaries> GetColorPrimariesFromDisplayInformationAsync(DisplayInformation displayInfo)
    {
        try
        {
            DisplayAdvancedColorInfo colorInfo = displayInfo.GetAdvancedColorInfo();
            if (colorInfo.CurrentAdvancedColorKind is DisplayAdvancedColorKind.HighDynamicRange or DisplayAdvancedColorKind.WideColorGamut)
            {
                return ColorPrimaries.BT709;
            }
            var iccStream = await displayInfo.GetColorProfileAsync();
            if (iccStream is null)
            {
                return new ColorPrimaries
                {
                    Red = new Vector2((float)colorInfo.RedPrimary.X, (float)colorInfo.RedPrimary.Y),
                    Green = new Vector2((float)colorInfo.GreenPrimary.X, (float)colorInfo.GreenPrimary.Y),
                    Blue = new Vector2((float)colorInfo.BluePrimary.X, (float)colorInfo.BluePrimary.Y),
                    White = new Vector2((float)colorInfo.WhitePoint.X, (float)colorInfo.WhitePoint.Y),
                };
            }
            else
            {
                byte[] iccData = new byte[iccStream.Size];
                await iccStream.AsStream().ReadExactlyAsync(iccData).ConfigureAwait(false);
                return ICCHelper.GetColorPrimariesFromIccData(iccData);
            }
        }
        catch
        {
            return ColorPrimaries.BT709;
        }
    }


    public static byte[] BuildXMPMetadata(DateTimeOffset time)
    {
        string value = $"""
            <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:xmp="http://ns.adobe.com/xap/1.0/"><xmp:CreatorTool>Starshot</xmp:CreatorTool><xmp:CreateDate>{time:yyyy-MM-ddTHH:mm:sszzz}</xmp:CreateDate></rdf:Description></rdf:RDF></x:xmpmeta>
            """;
        return Encoding.UTF8.GetBytes(value);
    }







    /// <summary>
    /// 图片最大亮度
    /// </summary>
    public static float GetMaxCLL(CanvasBitmap canvasBitmap)
    {
        float pixelScale = MathF.Min(0.5f, 2048f / MathF.Max(canvasBitmap.SizeInPixels.Width, canvasBitmap.SizeInPixels.Height));
        using var scaleEfect = new ScaleEffect
        {
            Source = canvasBitmap,
            Scale = new Vector2(pixelScale, pixelScale),
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        using var colorEffect = new ColorMatrixEffect
        {
            Source = scaleEfect,
            ColorMatrix = new Matrix5x4(
                0.2126f / 125, 0, 0, 0,
                0.7152f / 125, 0, 0, 0,
                0.0722f / 125, 0, 0, 0,
                0, 0, 0, 1,
                0, 0, 0, 0),
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        using var gammaEffect = new GammaTransferEffect
        {
            Source = colorEffect,
            RedExponent = 0.5f,
            GreenDisable = true,
            BlueDisable = true,
            AlphaDisable = true,
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        using var histogramEffect = new HistogramEffect
        {
            Source = gammaEffect,
            NumBins = 500,
            ChannelSelect = HistogramEffectChannelSelector.R,
            BufferPrecision = CanvasBufferPrecision.Precision16Float,
        };
        using CanvasRenderTarget renderTarget = new(CanvasDevice.GetSharedDevice(), 1, 1, 96);
        using var ds = renderTarget.CreateDrawingSession();
        ds.DrawImage(histogramEffect);
        ds.Dispose();
        float[] histogram = new float[500];
        histogramEffect.GetHistogramOutput(histogram);
        int maxBinIndex = 0;
        float cumulative = 0;
        for (int i = histogram.Length - 1; i >= 0; i--)
        {
            cumulative += histogram[i];
            if (cumulative >= 0.0001f)
            {
                maxBinIndex = i;
                break;
            }
        }
        return MathF.Pow((maxBinIndex + 0.5f) / histogram.Length, 2f) * 10000;
    }


}
