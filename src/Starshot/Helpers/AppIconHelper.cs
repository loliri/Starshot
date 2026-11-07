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

    /// <summary>
    /// 按系统通知图标尺寸从 exe 内嵌图标取一帧（大=SM_CXICON，小=SM_CXSMICON）。
    /// H.NotifyIcon 会校验句柄尺寸必须等于同一个 GetSystemMetrics 值，否则抛异常，所以这里必须按它取。
    /// LR_SHARED 的句柄由系统托管，不能 DestroyIcon。
    /// </summary>
    public static nint LoadNotificationIcon(bool large)
    {
        try
        {
            int size = User32.GetSystemMetrics(
                large ? User32.SystemMetric.SM_CXICON : User32.SystemMetric.SM_CXSMICON
            );
            nint hIcon = (nint)
                User32.LoadImage(
                    Kernel32.GetModuleHandle(null),
                    "#32512",
                    User32.LoadImageType.IMAGE_ICON,
                    size,
                    size,
                    User32.LoadImageOptions.LR_SHARED
                );
            return hIcon;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// System.Drawing.Icon → BitmapImage（PNG 内存流中转），失败返回 null。
    /// </summary>
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
