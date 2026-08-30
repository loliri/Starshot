using System;
using System.Runtime.InteropServices;

namespace Starshot.Helpers;

internal static class OneOcrNative
{
    /// <summary>oneocr.onemodel 的解密密钥</summary>
    internal const string ModelKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";

    /// <summary>输入图像（packed，字段布局须与 dll 期望逐字节一致）。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct RawImage
    {
        public int Type; // 3 = RGBA8
        public int Width;
        public int Height;
        public int Reserved; // 未知字段，oneocr-rs 传 0
        public long Step; // 行步长（字节）= width * 4
        public long DataPtr; // RGBA 数据指针
    }

    /// <summary>文字包围盒四角点：(x1,y1) 左上 → (x2,y2) 右上 → (x3,y3) 右下 → (x4,y4) 左下。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RawBBox
    {
        public float X1,
            Y1,
            X2,
            Y2,
            X3,
            Y3,
            X4,
            Y4;
    }

    private const string Dll = "oneocr.dll";

    [DllImport(Dll)]
    internal static extern int CreateOcrInitOptions(out IntPtr initOptions);

    [DllImport(Dll)]
    internal static extern int OcrInitOptionsSetUseModelDelayLoad(
        IntPtr initOptions,
        byte delayLoad
    );

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    internal static extern int CreateOcrPipeline(
        string modelPath,
        string key,
        IntPtr initOptions,
        out IntPtr pipeline
    );

    [DllImport(Dll)]
    internal static extern int CreateOcrProcessOptions(out IntPtr processOptions);

    [DllImport(Dll)]
    internal static extern int RunOcrPipeline(
        IntPtr pipeline,
        ref RawImage image,
        IntPtr processOptions,
        out IntPtr result
    );

    [DllImport(Dll)]
    internal static extern int GetOcrLineCount(IntPtr result, out long count);

    [DllImport(Dll)]
    internal static extern int GetOcrLine(IntPtr result, long index, out IntPtr line);

    [DllImport(Dll)]
    internal static extern int GetOcrLineContent(IntPtr line, out IntPtr content);

    [DllImport(Dll)]
    internal static extern int GetOcrLineWordCount(IntPtr line, out long count);

    [DllImport(Dll)]
    internal static extern int GetOcrWord(IntPtr line, long index, out IntPtr word);

    [DllImport(Dll)]
    internal static extern int GetOcrWordContent(IntPtr word, out IntPtr content);

    [DllImport(Dll)]
    internal static extern int GetOcrWordBoundingBox(IntPtr word, out IntPtr bbox);

    [DllImport(Dll)]
    internal static extern void ReleaseOcrResult(IntPtr result);
}
