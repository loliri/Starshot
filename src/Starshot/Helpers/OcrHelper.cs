using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Serilog;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Starshot.Helpers;

/// <summary>识别词：文本 + 原图坐标矩形</summary>
public sealed record OcrWord(string Text, Rect Rect);

/// <summary>识别行：整行文本 + 原图坐标矩形 + 词级明细</summary>
public sealed record OcrLine(string Text, Rect Rect, List<OcrWord> Words);

/// <summary>
/// OCR 双引擎封装：
/// 优先 oneocr.dll（Windows 照片应用同款引擎，随安装包分发，exe 旁的 oneocr.onemodel）；
/// 不可用（文件缺失 / 初始化失败）时降级 Windows.Media.Ocr（系统老引擎）。
/// 坐标全部为原图像素坐标系（老引擎超尺寸缩放识别的按比例还原），调用方再按显示尺寸换算 UI 坐标。
/// </summary>
public static class OcrHelper
{
    private static readonly object _oneOcrLock = new();
    private static int _oneOcrState; // 0=未探测 1=可用 2=不可用
    private static IntPtr _pipeline;
    private static IntPtr _processOptions;

    private static string DllPath => Path.Combine(AppContext.BaseDirectory, "oneocr.dll");

    private static string ModelPath => Path.Combine(AppContext.BaseDirectory, "oneocr.onemodel");

    /// <summary>oneocr 两个文件是否已落地 exe 旁（只查文件，不触发初始化）。</summary>
    public static bool IsOneOcrReady => File.Exists(DllPath) && File.Exists(ModelPath);

    /// <summary>
    /// 文件在但 init 失败（模型/dll 损坏类深层问题，区别于未配置）。
    /// EnsureOneOcr 对文件缺失不置状态，state==2 必然是「试过且失败」。
    /// </summary>
    public static bool OneOcrInitFailed => _oneOcrState == 2;

    /// <summary>
    /// 重新配置（本机获取 / CDN 下载 / 删除）后重置探测缓存，让下次识别重新尝试 init。
    /// native pipeline 已加载时不释放（进程生命周期内无法卸载）。
    /// </summary>
    public static void ResetEngineCache()
    {
        lock (_oneOcrLock)
        {
            _oneOcrState = 0;
            _pipeline = IntPtr.Zero;
            _processOptions = IntPtr.Zero;
        }
    }

    /// <summary>
    /// oneocr 引擎可用性（懒初始化一次）。失败缓存为不可用，后续调用直接走降级引擎。
    /// </summary>
    private static bool EnsureOneOcr()
    {
        if (!IsOneOcrReady)
        {
            return false;
        }
        if (_oneOcrState != 0)
        {
            return _oneOcrState == 1;
        }
        lock (_oneOcrLock)
        {
            if (_oneOcrState != 0)
            {
                return _oneOcrState == 1;
            }
            try
            {
                Check(
                    OneOcrNative.CreateOcrInitOptions(out IntPtr initOptions),
                    "CreateOcrInitOptions"
                );
                OneOcrNative.OcrInitOptionsSetUseModelDelayLoad(initOptions, 0);
                Check(
                    OneOcrNative.CreateOcrPipeline(
                        ModelPath,
                        OneOcrNative.ModelKey,
                        initOptions,
                        out _pipeline
                    ),
                    "CreateOcrPipeline"
                );
                Check(
                    OneOcrNative.CreateOcrProcessOptions(out _processOptions),
                    "CreateOcrProcessOptions"
                );
                // initOptions 交由 dll 侧持有（参照 oneocr-rs：进程生命周期不释放）
                _oneOcrState = 1;
                Log.Information("[OCR] OneOcr engine initialized");
            }
            catch (Exception ex)
            {
                _oneOcrState = 2;
                _pipeline = IntPtr.Zero;
                _processOptions = IntPtr.Zero;
                Log.Warning(ex, "[OCR] OneOcr init failed, will use legacy engine");
            }
        }
        return _oneOcrState == 1;
    }

    /// <summary>
    /// UI 线程调用：CanvasBitmap → BGRA8 像素（GPU 回读不能离开创建设备的线程）。
    /// 超旧引擎 MaxImageDimension 时 GPU 预缩，返回的 Scale 供识别坐标乘回原图。
    /// </summary>
    public static (byte[] Pixels, int Width, int Height, double Scale) PreparePixels(
        CanvasBitmap bitmap
    )
    {
        using var rgba8 = ToBgra8(bitmap);
        uint maxDim = OcrEngine.MaxImageDimension;
        double scale = Math.Min(
            1.0,
            (double)maxDim / Math.Max(rgba8.SizeInPixels.Width, rgba8.SizeInPixels.Height)
        );
        if (scale < 1.0)
        {
            using var scaled = DownscaleToBgra8(rgba8, scale);
            var size = scaled.SizeInPixels;
            return (scaled.GetPixelBytes(), (int)size.Width, (int)size.Height, scale);
        }
        var s = rgba8.SizeInPixels;
        return (rgba8.GetPixelBytes(), (int)s.Width, (int)s.Height, 1.0);
    }

    /// <summary>
    /// 识别（任意线程；oneocr 是纯 native 调用，旧引擎 OcrEngine/SoftwareBitmap 为 agile WinRT）。
    /// 返回 null = 两个引擎都不可用；空列表 = 没识别到文字。坐标为原图像素。
    /// </summary>
    public static async Task<List<OcrLine>?> RecognizeAsync(
        byte[] bgra,
        int width,
        int height,
        double scale
    )
    {
        var sw = Stopwatch.StartNew();
        double restore = 1.0 / scale;

        // 引擎选择：用户配了系统引擎（OcrEngine=1）直接跳过 oneocr
        if (AppConfig.OcrEngine == 0 && EnsureOneOcr())
        {
            try
            {
                var oneLines = RunOneOcr(bgra, width, height, restore);
                Log.Information(
                    "[OCR] engine=OneOcr, {Count} lines, {Ms}ms",
                    oneLines.Count,
                    sw.ElapsedMilliseconds
                );
                return oneLines;
            }
            catch (Exception ex)
            {
                // 识别中途异常（模型损坏 / 内存不足）：落降级引擎
                Log.Warning(ex, "[OCR] OneOcr recognize failed, falling back to legacy");
            }
        }

        var engine = TryCreateLegacyEngine();
        if (engine is null)
        {
            Log.Warning("[OCR] no engine available");
            return null;
        }

        using var software = CreateSoftwareBitmap(bgra, width, height);
        var ocrResult = await engine.RecognizeAsync(software);
        var lines = new List<OcrLine>(ocrResult.Lines.Count);
        foreach (var line in ocrResult.Lines)
        {
            var words = new List<OcrWord>(line.Words.Count);
            foreach (var w in line.Words)
            {
                words.Add(new OcrWord(w.Text, ScaleRect(w.BoundingRect, restore)));
            }
            lines.Add(new OcrLine(line.Text, UnionRects(words.Select(w => w.Rect)), words));
        }
        Log.Information(
            "[OCR] engine=Legacy, {Count} lines, scale={Scale}, {Ms}ms",
            lines.Count,
            scale,
            sw.ElapsedMilliseconds
        );
        return lines;
    }

    private static unsafe List<OcrLine> RunOneOcr(
        byte[] bgra,
        int width,
        int height,
        double restore
    )
    {
        // Win2D 给的是 BGRA8，oneocr 要 RGBA：逐像素交换 R/B
        for (int i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        }
        var image = new OneOcrNative.RawImage
        {
            Type = 3,
            Width = width,
            Height = height,
            Reserved = 0,
            Step = (long)width * 4,
        };
        IntPtr result;
        fixed (byte* p = bgra)
        {
            image.DataPtr = (long)p;
            lock (_oneOcrLock)
            {
                Check(
                    OneOcrNative.RunOcrPipeline(_pipeline, ref image, _processOptions, out result),
                    "RunOcrPipeline"
                );
            }
        }
        try
        {
            return ReadOneOcrResult(result, restore);
        }
        finally
        {
            OneOcrNative.ReleaseOcrResult(result);
        }
    }

    private static List<OcrLine> ReadOneOcrResult(IntPtr result, double restore)
    {
        Check(OneOcrNative.GetOcrLineCount(result, out long lineCount), "GetOcrLineCount");
        var lines = new List<OcrLine>((int)lineCount);
        for (long i = 0; i < lineCount; i++)
        {
            Check(OneOcrNative.GetOcrLine(result, i, out IntPtr line), "GetOcrLine");
            Check(OneOcrNative.GetOcrLineContent(line, out IntPtr content), "GetOcrLineContent");
            string lineText = SquashCjkSpaces(Marshal.PtrToStringUTF8(content) ?? "");
            Check(
                OneOcrNative.GetOcrLineWordCount(line, out long wordCount),
                "GetOcrLineWordCount"
            );
            var words = new List<OcrWord>((int)wordCount);
            for (long j = 0; j < wordCount; j++)
            {
                Check(OneOcrNative.GetOcrWord(line, j, out IntPtr word), "GetOcrWord");
                Check(
                    OneOcrNative.GetOcrWordContent(word, out IntPtr wordContent),
                    "GetOcrWordContent"
                );
                string wordText = SquashCjkSpaces(Marshal.PtrToStringUTF8(wordContent) ?? "");
                Check(
                    OneOcrNative.GetOcrWordBoundingBox(word, out IntPtr bbox),
                    "GetOcrWordBoundingBox"
                );
                var box = Marshal.PtrToStructure<OneOcrNative.RawBBox>(bbox);
                words.Add(new OcrWord(wordText, ScaleRect(QuadToRect(box), restore)));
            }
            lines.Add(new OcrLine(lineText, UnionRects(words.Select(w => w.Rect)), words));
        }
        return lines;
    }

    /// <summary>
    /// oneocr 的行文本是 token 空格拼接（中文 token = 单字，字间全带空格；照片应用展示前也做了同样处理）。
    /// 删除空格当且仅当两侧都不是拉丁字母/数字——汉字间删、汉字与标点间删、英文词间与中英之间保留。
    /// </summary>
    private static string SquashCjkSpaces(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (
                c == ' '
                && i > 0
                && i < text.Length - 1
                && !IsLatinWordChar(text[i - 1])
                && !IsLatinWordChar(text[i + 1])
            )
            {
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static bool IsLatinWordChar(char c)
    {
        return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');
    }

    /// <summary>
    /// 词序列拼接（拖选复制用）：相邻词接触侧都是拉丁字母/数字才补空格，
    /// 否则直接相连——中文单字词拼起来不带空格，英文单词间正常空格。
    /// </summary>
    internal static string JoinWords(IEnumerable<string> words)
    {
        string prev = "";
        var sb = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(word))
            {
                continue;
            }
            if (prev.Length > 0 && IsLatinWordChar(prev[^1]) && IsLatinWordChar(word[0]))
            {
                sb.Append(' ');
            }
            sb.Append(word);
            prev = word;
        }
        return sb.ToString();
    }

    private static void Check(int hr, string api)
    {
        if (hr != 0)
        {
            throw new InvalidOperationException($"{api} failed, hr=0x{hr:X8}");
        }
    }

    private static OcrEngine? TryCreateLegacyEngine()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages()
                ?? (
                    OcrEngine.AvailableRecognizerLanguages.Count > 0
                        ? OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0])
                        : null
                );
        }
        catch
        {
            return null;
        }
    }

    private static Rect UnionRects(IEnumerable<Rect> rects)
    {
        double left = double.MaxValue,
            top = double.MaxValue,
            right = double.MinValue,
            bottom = double.MinValue;
        bool any = false;
        foreach (var r in rects)
        {
            any = true;
            left = Math.Min(left, r.Left);
            top = Math.Min(top, r.Top);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }
        return any ? new Rect(left, top, right - left, bottom - top) : new Rect(0, 0, 0, 0);
    }

    private static Rect ScaleRect(Rect rect, double factor)
    {
        return new Rect(
            rect.X * factor,
            rect.Y * factor,
            rect.Width * factor,
            rect.Height * factor
        );
    }

    /// <summary>oneocr 的包围盒是四角点 quad（含倾斜文字），折算成轴对齐 Rect。</summary>
    private static Rect QuadToRect(in OneOcrNative.RawBBox box)
    {
        double left = Math.Min(Math.Min(box.X1, box.X2), Math.Min(box.X3, box.X4));
        double top = Math.Min(Math.Min(box.Y1, box.Y2), Math.Min(box.Y3, box.Y4));
        double right = Math.Max(Math.Max(box.X1, box.X2), Math.Max(box.X3, box.X4));
        double bottom = Math.Max(Math.Max(box.Y1, box.Y2), Math.Max(box.Y3, box.Y4));
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 任意像素格式 → B8G8R8A8：HDR float 源直转（clamp 到 [0,1] 级别的普通绘制，
    /// 不做 tonemap——识别只看结构，亮度映射不影响文字形状）。
    /// </summary>
    private static CanvasRenderTarget ToBgra8(CanvasBitmap bitmap)
    {
        var device = bitmap.Device;
        var target = new CanvasRenderTarget(
            device,
            bitmap.SizeInPixels.Width,
            bitmap.SizeInPixels.Height,
            96,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            CanvasAlphaMode.Premultiplied
        );
        using (var ds = target.CreateDrawingSession())
        {
            ds.Units = CanvasUnits.Pixels;
            ds.DrawImage(bitmap, 0, 0);
        }
        return target;
    }

    private static SoftwareBitmap CreateSoftwareBitmap(byte[] bgra, int width, int height)
    {
        return SoftwareBitmap.CreateCopyFromBuffer(
            bgra.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Premultiplied
        );
    }

    /// <summary>GPU 缩小绘制到新 target（SoftwareBitmap 自身无缩放转换 API）。</summary>
    private static CanvasRenderTarget DownscaleToBgra8(CanvasRenderTarget source, double scale)
    {
        uint w = Math.Max(1, (uint)Math.Round(source.SizeInPixels.Width * scale));
        uint h = Math.Max(1, (uint)Math.Round(source.SizeInPixels.Height * scale));
        var target = new CanvasRenderTarget(
            source.Device,
            w,
            h,
            96,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            CanvasAlphaMode.Premultiplied
        );
        using (var ds = target.CreateDrawingSession())
        {
            ds.Units = CanvasUnits.Pixels;
            ds.DrawImage(source, new Rect(0, 0, w, h));
        }
        return target;
    }
}
