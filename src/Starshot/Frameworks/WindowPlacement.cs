using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Starshot.Frameworks;

/// <summary>
/// 窗口几何的序列化与校正。按物理像素记录——<see cref="AppWindow"/> 与
/// <see cref="DisplayArea"/> 用的都是物理像素，和 Win32 的 <c>SetWindowPlacement</c> 语义一致；
/// 不做 DIP 换算，否则多显示器缩放比不同时（如 150% + 100%）会用错缩放因子而错位。
/// </summary>
internal static partial class WindowPlacement
{
    /// <summary>
    /// 解析 “x,y,width,height,maximized”（物理像素），非法/缺字段返回 false（调用方保持系统默认）。
    /// </summary>
    public static bool TryParse(string? raw, out RectInt32 rect, out bool maximized)
    {
        rect = default;
        maximized = false;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        string[] parts = raw.Split(',');
        if (parts.Length != 5)
        {
            return false;
        }
        if (
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int y
            )
            || !int.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int width
            )
            || !int.TryParse(
                parts[3],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int height
            )
            || !int.TryParse(
                parts[4],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int flag
            )
        )
        {
            return false;
        }
        if (width <= 0 || height <= 0)
        {
            return false;
        }
        rect = new RectInt32(x, y, width, height);
        maximized = flag != 0;
        return true;
    }

    public static string Format(RectInt32 rect, bool maximized) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{rect.X},{rect.Y},{rect.Width},{rect.Height},{(maximized ? 1 : 0)}"
        );

    /// <summary>
    /// 居中到指定显示器：尺寸按该显示器的 DPI 换算成物理像素。跨缩放比显示器时
    /// 窗口自身的 DPI 还是源屏的，必须取目标显示器的 DPI 才算得准；
    /// 宽/高为 null 或非正数时保持当前物理尺寸。
    /// </summary>
    public static void CenterInScreen(
        AppWindow appWindow,
        DisplayArea displayArea,
        int? width = null,
        int? height = null
    )
    {
        nint monitor = Win32Interop.GetMonitorFromDisplayId(displayArea.DisplayId);
        GetDpiForMonitor(monitor, 0, out uint dpiX, out uint _);
        double scale = dpiX / 96.0;
        int w = width is > 0 ? (int)(width * scale) : appWindow.Size.Width;
        int h = height is > 0 ? (int)(height * scale) : appWindow.Size.Height;
        int x = displayArea.WorkArea.X + (displayArea.WorkArea.Width - w) / 2;
        int y = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - h) / 2;
        appWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    [LibraryImport("Shcore.dll")]
    private static partial int GetDpiForMonitor(
        nint hmonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY
    );

    /// <summary>
    /// 夹到最近显示器的工作区内：拔掉副屏、分辨率/缩放变小后，窗口不会整个留在屏幕外。
    /// 位置越界时贴边，尺寸超过工作区时收缩到工作区。
    /// </summary>
    public static RectInt32 EnsureVisible(RectInt32 rect)
    {
        DisplayArea area =
            DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
        RectInt32 work = area.WorkArea;
        int width = Math.Min(rect.Width, work.Width);
        int height = Math.Min(rect.Height, work.Height);
        int x = Math.Clamp(rect.X, work.X, work.X + work.Width - width);
        int y = Math.Clamp(rect.Y, work.Y, work.Y + work.Height - height);
        return new RectInt32(x, y, width, height);
    }
}
