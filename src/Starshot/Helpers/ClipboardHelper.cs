using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Starshot.Helpers;

internal static class ClipboardHelper
{

    public static void SetText(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var data = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            data.SetText(value);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
    }


    public static void SetBitmap(IStorageFile file)
    {
        var value = RandomAccessStreamReference.CreateFromFile(file);
        var data = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        data.SetBitmap(value);
        Clipboard.SetContent(data);
    }


    public static void SetStorageItems(DataPackageOperation operation, params IStorageItem[] items)
    {
        var data = new DataPackage
        {
            RequestedOperation = operation,
        };
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

    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint CF_DIB = 8;
    private const uint CF_HDROP = 15;


    /// <summary>
    /// 把 BGRA top-down 像素以 CF_DIB 放进剪贴板（BITMAPINFOHEADER + 倒序行成 bottom-up）。
    /// 任意线程可调，剪贴板被占用时重试。
    /// </summary>
    public static void SetBitmapDib(int width, int height, byte[] bgraTopDown)
    {
        const int headerSize = 40;
        int rowBytes = width * 4;
        int pixelBytes = rowBytes * height;
        int total = headerSize + pixelBytes;

        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)total);
        if (hMem == IntPtr.Zero) return;
        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) { GlobalFree(hMem); return; }

        // BITMAPINFOHEADER
        Marshal.WriteInt32(ptr, 0, headerSize);       // biSize
        Marshal.WriteInt32(ptr, 4, width);            // biWidth
        Marshal.WriteInt32(ptr, 8, height);           // biHeight（正=bottom-up）
        Marshal.WriteInt16(ptr, 12, (short)1);        // biPlanes
        Marshal.WriteInt16(ptr, 14, (short)32);       // biBitCount
        Marshal.WriteInt32(ptr, 16, 0);               // biCompression = BI_RGB
        Marshal.WriteInt32(ptr, 20, pixelBytes);      // biSizeImage
        Marshal.WriteInt32(ptr, 24, 0);               // biXPelsPerMeter
        Marshal.WriteInt32(ptr, 28, 0);               // biYPelsPerMeter
        Marshal.WriteInt32(ptr, 32, 0);               // biClrUsed
        Marshal.WriteInt32(ptr, 36, 0);               // biClrImportant

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
                if (res != IntPtr.Zero) { success = true; break; } // 系统接管 hMem
            }
            Thread.Sleep(20);
        }
        if (!success)
        {
            GlobalFree(hMem); // 失败时自己释放
        }
    }


    /// <summary>
    /// 把文件复制到剪贴板（Win32 CF_HDROP，文件列表）。任意线程可调，被占用时重试。
    /// 粘贴目标需支持文件（资源管理器/聊天软件/支持拖入的编辑器）。
    /// </summary>
    public static void SetFiles(params string[] paths)
    {
        if (paths == null || paths.Length == 0) return;

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
        if (hMem == IntPtr.Zero) return;
        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) { GlobalFree(hMem); return; }

        // DROPFILES
        Marshal.WriteInt32(ptr, 0, headerSize); // pFiles = 偏移到文件列表
        Marshal.WriteInt32(ptr, 4, 0);          // pt.x
        Marshal.WriteInt32(ptr, 8, 0);          // pt.y
        Marshal.WriteInt32(ptr, 12, 0);         // fNC = FALSE
        Marshal.WriteInt32(ptr, 16, 1);         // fWide = TRUE（Unicode）
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
                if (res != IntPtr.Zero) { success = true; break; }
            }
            Thread.Sleep(20);
        }
        if (!success)
        {
            GlobalFree(hMem);
        }
    }


}
