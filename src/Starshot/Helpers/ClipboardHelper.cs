using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Starshot.Helpers;

internal static class ClipboardHelper
{
    public static void SetText(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            data.SetText(value);
            Clipboard.SetContent(data);
            try
            {
                Clipboard.Flush();
            }
            catch (Exception ex)
            {
                // CLIPBRD_E_CANT_OPEN（剪贴板被他进程短暂占用）：SetContent 已成功、内容可用，
                // Flush 只是「应用退出后仍可读」的增强，失败不构成失败
                Serilog.Log.Warning(ex, "[Clipboard] Flush after SetText failed (content is still valid)");
            }
        }
    }

    public static void SetBitmap(IStorageFile file)
    {
        var value = RandomAccessStreamReference.CreateFromFile(file);
        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetBitmap(value);
        Clipboard.SetContent(data);
    }

    public static void SetStorageItems(DataPackageOperation operation, params IStorageItem[] items)
    {
        var data = new DataPackage { RequestedOperation = operation };
        data.SetStorageItems(items);
        Clipboard.SetContent(data);
    }

    // ===== Win32 剪贴板（CF_DIB）。绕过 WinRT DataPackage，任意线程可调，最可靠。 =====

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint CF_DIB = 8;
    private const uint CF_HDROP = 15;

    /// <summary>
    /// 把 BGRA top-down 像素以 CF_DIB 放进剪贴板（BITMAPINFOHEADER + 倒序行成 bottom-up）。
    /// 任意线程可调，剪贴板被占用时重试。返回是否成功（失败时调用方不应报成功）。
    /// </summary>
    public static bool SetBitmapDib(int width, int height, byte[] bgraTopDown)
    {
        const int headerSize = 40;
        int rowBytes = width * 4;
        int pixelBytes = rowBytes * height;
        int total = headerSize + pixelBytes;

        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)total);
        if (hMem == IntPtr.Zero)
            return false;
        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(hMem);
            return false;
        }

        // BITMAPINFOHEADER
        Marshal.WriteInt32(ptr, 0, headerSize); // biSize
        Marshal.WriteInt32(ptr, 4, width); // biWidth
        Marshal.WriteInt32(ptr, 8, height); // biHeight（正=bottom-up）
        Marshal.WriteInt16(ptr, 12, (short)1); // biPlanes
        Marshal.WriteInt16(ptr, 14, (short)32); // biBitCount
        Marshal.WriteInt32(ptr, 16, 0); // biCompression = BI_RGB
        Marshal.WriteInt32(ptr, 20, pixelBytes); // biSizeImage
        Marshal.WriteInt32(ptr, 24, 0); // biXPelsPerMeter
        Marshal.WriteInt32(ptr, 28, 0); // biYPelsPerMeter
        Marshal.WriteInt32(ptr, 32, 0); // biClrUsed
        Marshal.WriteInt32(ptr, 36, 0); // biClrImportant

        // top-down 像素 → bottom-up：从最后一行往前拷
        IntPtr rowPtr = ptr + headerSize;
        for (int y = height - 1; y >= 0; y--)
        {
            Marshal.Copy(bgraTopDown, y * rowBytes, rowPtr, rowBytes);
            rowPtr += rowBytes;
        }
        GlobalUnlock(hMem);

        bool success = false;
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                EmptyClipboard();
                IntPtr res = SetClipboardData(CF_DIB, hMem);
                CloseClipboard();
                if (res != IntPtr.Zero)
                {
                    success = true;
                    break;
                } // 系统接管 hMem
            }
            Thread.Sleep(20);
        }
        if (!success)
        {
            GlobalFree(hMem); // 失败时自己释放
        }
        return success;
    }

    /// <summary>
    /// 读当前剪贴板里的图像，返回可重复打开的流引用；非图像/空/失败一律返回 null，绝不抛
    /// （ContentChanged 可能在本 app 写入剪贴板的中途触发，读取方要求零异常）。
    /// 优先 WinRT 位图格式（其他 app 的复制，系统对 CF_DIB 也会合成声明），
    /// 拿不到再直接读 CF_DIB——本 app 自己的写入路径，大图必须走这条。
    /// </summary>
    public static async Task<RandomAccessStreamReference?> GetClipboardImageAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content is not null && content.Contains(StandardDataFormats.Bitmap))
            {
                var streamRef = await content.GetBitmapAsync();
                if (streamRef is not null)
                {
                    return streamRef;
                }
            }
        }
        catch { }
        try
        {
            byte[]? dib = TryGetCfDibBytes();
            if (dib is not null)
            {
                var png = await DibToPngStreamAsync(dib);
                if (png is not null)
                {
                    return RandomAccessStreamReference.CreateFromStream(png);
                }
            }
        }
        catch { }
        return null;
    }

    private static byte[]? TryGetCfDibBytes()
    {
        for (int i = 0; i < 3; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    IntPtr h = GetClipboardData(CF_DIB);
                    if (h == IntPtr.Zero)
                        return null;
                    IntPtr ptr = GlobalLock(h);
                    if (ptr == IntPtr.Zero)
                        return null;
                    try
                    {
                        int size = checked((int)GlobalSize(h));
                        byte[] buf = new byte[size];
                        Marshal.Copy(ptr, buf, 0, size);
                        return buf;
                    }
                    finally
                    {
                        GlobalUnlock(h);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }
        return null;
    }

    /// <summary>CF_DIB（32bpp BI_RGB，本 app 的写法）→ PNG 内存流；其他位深/压缩一律放弃返回 null</summary>
    private static async Task<InMemoryRandomAccessStream?> DibToPngStreamAsync(byte[] dib)
    {
        if (dib.Length < 40)
            return null;
        int headerSize = BitConverter.ToInt32(dib, 0);
        int width = BitConverter.ToInt32(dib, 4);
        int heightRaw = BitConverter.ToInt32(dib, 8);
        short bitCount = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        if (width <= 0 || heightRaw == 0 || bitCount != 32 || compression != 0 || headerSize < 40)
            return null;
        bool bottomUp = heightRaw > 0;
        int height = Math.Abs(heightRaw);
        int rowBytes = width * 4;
        int pixelLen = rowBytes * height;
        if (dib.Length < headerSize + pixelLen)
            return null;

        // bottom-up 行序翻成 top-down
        byte[] bgra = new byte[pixelLen];
        if (bottomUp)
        {
            for (int y = 0; y < height; y++)
            {
                System.Buffer.BlockCopy(
                    dib,
                    headerSize + (height - 1 - y) * rowBytes,
                    bgra,
                    y * rowBytes,
                    rowBytes
                );
            }
        }
        else
        {
            System.Buffer.BlockCopy(dib, headerSize, bgra, 0, pixelLen);
        }

        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)width,
            (uint)height,
            96,
            96,
            bgra
        );
        await encoder.FlushAsync();
        return stream;
    }

    /// <summary>
    /// 把文件复制到剪贴板（Win32 CF_HDROP，文件列表）。任意线程可调，被占用时重试。
    /// 粘贴目标需支持文件（资源管理器/聊天软件/支持拖入的编辑器）。
    /// </summary>
    public static void SetFiles(params string[] paths)
    {
        if (paths == null || paths.Length == 0)
            return;

        // DROPFILES(20B) + 各路径(逐个 \0 结束) + 末尾额外 \0，Unicode
        var sb = new System.Text.StringBuilder();
        foreach (var p in paths)
        {
            sb.Append(p);
            sb.Append('\0');
        }
        sb.Append('\0');
        string blob = sb.ToString();
        int headerSize = 20;
        int total = headerSize + blob.Length * 2;

        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)total);
        if (hMem == IntPtr.Zero)
            return;
        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(hMem);
            return;
        }

        // DROPFILES
        Marshal.WriteInt32(ptr, 0, headerSize); // pFiles = 偏移到文件列表
        Marshal.WriteInt32(ptr, 4, 0); // pt.x
        Marshal.WriteInt32(ptr, 8, 0); // pt.y
        Marshal.WriteInt32(ptr, 12, 0); // fNC = FALSE
        Marshal.WriteInt32(ptr, 16, 1); // fWide = TRUE（Unicode）
        char[] chars = blob.ToCharArray();
        Marshal.Copy(chars, 0, ptr + headerSize, chars.Length);
        GlobalUnlock(hMem);

        bool success = false;
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                EmptyClipboard();
                IntPtr res = SetClipboardData(CF_HDROP, hMem);
                CloseClipboard();
                if (res != IntPtr.Zero)
                {
                    success = true;
                    break;
                }
            }
            Thread.Sleep(20);
        }
        if (!success)
        {
            GlobalFree(hMem);
        }
    }
}
