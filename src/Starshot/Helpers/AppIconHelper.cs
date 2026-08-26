using System;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;
using Vanara.PInvoke;

namespace Starshot.Helpers;

/// <summary>
/// exe 内嵌图标资源（ApplicationIcon 嵌入）的运行时抽取与 XAML 转换。
/// 不随包分发 ico/png——图标单一来源就是 exe 资源。
/// </summary>
internal static class AppIconHelper
{
    /// <summary>
    /// 抽取应用自身图标：LoadIcon 取系统图标尺寸帧（默认 32×32）。
    /// 与托盘（H.NotifyIcon）、任务管理器（AppWindow.SetIcon）同源。
    /// </summary>
    public static System.Drawing.Icon? GetAppIcon()
    {
        try
        {
            nint hInstance = Kernel32.GetModuleHandle(null).DangerousGetHandle();
            nint hIcon = User32.LoadIcon(hInstance, "#32512").DangerousGetHandle();
            return hIcon == 0 ? null : System.Drawing.Icon.FromHandle(hIcon);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>System.Drawing.Icon → BitmapImage（PNG 内存流中转），失败返回 null。</summary>
    public static BitmapImage? ToBitmapImage(System.Drawing.Icon icon)
    {
        try
        {
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.SetSource(ms.AsRandomAccessStream());
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
