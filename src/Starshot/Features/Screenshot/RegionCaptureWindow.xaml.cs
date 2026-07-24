using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Starshot.Features.Codec;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Starshot.Features.Screenshot;

public sealed partial class RegionCaptureWindow : WindowEx
{
    private const int MinimumRectangleSize = 5;
    private const int MagnifierPixelCount = 15;
    private const int MagnifierPixelSize = 10;

    public Rect SelectionRect { get; private set; }
    public bool IsConfirmed { get; private set; }
    // 确认时从 _displayBitmap（冻结帧，已 tonemap 的 SDR）裁出的选区，供剪贴板直接复用，不再二次 tonemap
    public CanvasRenderTarget SdrCrop { get; private set; }

    private readonly CanvasBitmap _canvasOriginal;   // 原始帧（裁剪用，可能 HDR）
    private readonly CanvasBitmap _displayBitmap;     // 显示用（SDR 色调映射后）
    private readonly float _scale;
    private readonly int _vx, _vy;  // 虚拟屏幕物理坐标原点（放大镜钳制到当前显示器用）

    private Point _positionOnClick;
    private bool _isMouseDown;
    private bool _pressedOnHover;  // 左键按下瞬间是否悬停在某个窗口上（单击截图用）
    private Point _currentMousePos;
    // 选区来源：true=鼠标框选（端点是光标像素索引，需 +1，对应 CreateRectangle）；
    // false=窗口矩形（本身就是正常尺寸，不 +1）
    private bool _selectionFromDrag;

    private List<Rect> _windowRects = new();
    private Rect _hoverRect;
    private bool _hasHover;

    private float _dashOffset;
    private System.Diagnostics.Stopwatch _timer;
    private bool _isClosed;

    // 锁定画布尺寸（首帧后固定，防止布局抖动导致冻结帧移动）
    private float _lockedW;
    private float _lockedH;
    private bool _sizeLocked;

    // HDR 时 _displayBitmap 是本窗新建的 SDR 副本，由本窗释放；
    // SDR 时它就是传入的 canvas（= composite），归调用方，不能动
    private readonly bool _ownsDisplayBitmap;
    private bool _cleanedUp;


    public RegionCaptureWindow(CanvasBitmap canvas, float scale, float sdrWhiteLevel, int physW, int physH)
    {
        InitializeComponent();

        _canvasOriginal = canvas;
        _scale = scale;
        _timer = System.Diagnostics.Stopwatch.StartNew();

        _displayBitmap = CreateDisplayBitmap(canvas, physW, physH, sdrWhiteLevel);
        _ownsDisplayBitmap = !ReferenceEquals(_displayBitmap, canvas);
        this.Closed += RegionCaptureWindow_Closed;

        // 窗口设置
        WindowEx.MainWindowId = AppWindow.Id;
        Title = "Starshot";
        AppWindow.IsShownInSwitchers = false;
        SystemBackdrop = new TransparentBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        int vx = User32.GetSystemMetrics((User32.SystemMetric)76);
        int vy = User32.GetSystemMetrics((User32.SystemMetric)77);
        int vw = User32.GetSystemMetrics((User32.SystemMetric)78);
        int vh = User32.GetSystemMetrics((User32.SystemMetric)79);
        _vx = vx;
        _vy = vy;
        AppWindow.MoveAndResize(new RectInt32(vx, vy, vw, vh));

        // 清除残留窗口边框样式（WinUI 的 SetBorderAndTitleBar 仍留 ~2px resize frame）
        var style = (User32.WindowStyles)User32.GetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE);
        style &= ~(User32.WindowStyles.WS_THICKFRAME | User32.WindowStyles.WS_BORDER | User32.WindowStyles.WS_CAPTION | User32.WindowStyles.WS_DLGFRAME);
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE, (nint)style);
        User32.SetWindowPos(WindowHandle, IntPtr.Zero, vx, vy, vw, vh, (User32.SetWindowPosFlags)0x0020 | User32.SetWindowPosFlags.SWP_NOZORDER);

        // 首帧前用当前光标位置初始化 _currentMousePos，否则放大镜/坐标框会画在 (0,0) 直到第一次 PointerMoved
        if (User32.GetCursorPos(out var initCursor))
        {
            _currentMousePos = new Point((initCursor.x - _vx) / _scale, (initCursor.y - _vy) / _scale);
        }

        PointerCursor.SetCursorShape(Canvas, InputSystemCursorShape.Cross);

        _ = Task.Run(DetectWindows);
    }


    private static CanvasBitmap CreateDisplayBitmap(CanvasBitmap source, int w, int h, float sdrWhiteLevel)
    {
        if (source.Format is DirectXPixelFormat.R8G8B8A8UIntNormalized or DirectXPixelFormat.B8G8R8A8UIntNormalized)
        {
            return source;
        }

        var device = CanvasDevice.GetSharedDevice();
        var sdr = new CanvasRenderTarget(device, w, h, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var ds = sdr.CreateDrawingSession())
        {
            var wle = new WhiteLevelAdjustmentEffect
            {
                Source = source,
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


    // 直接 P/Invoke DwmGetWindowAttribute，避免 Vanara 泛型重载在 DWMWA_CLOAKED 上 marshal 不可靠
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetCloaked(IntPtr hwnd, int attr, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetExtendedFrameBounds(IntPtr hwnd, int attr, ref RECT pvAttribute, int cbAttribute);


    // 移植自 WindowsRectangleList：跳过 cloaked / TOOLWINDOW&NOACTIVATE 等垃圾窗口，
    // DWM 扩展边界去阴影，额外加入 client rect（可吸到内容区），最后去重。
    private void DetectWindows()
    {
        var raw = new List<(Rect rect, bool isWindow)>();

        User32.EnumWindows((hWnd, _) =>
        {
            try
            {
                if (!User32.IsWindowVisible(hWnd)) return true;
                if (User32.IsIconic(hWnd)) return true;
                if (hWnd == WindowHandle) return true;

                // cloaked（隐藏的 UWP / 最小化到任务栏 / 其它虚拟桌面等，真正不可见）
                try
                {
                    if (DwmGetCloaked(hWnd.DangerousGetHandle(), 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                        return true;
                }
                catch { }

                // 跳过 non-activatable tool windows：任务栏/托盘/平铺管理器 overlay/各种小工具
                var exStyle = (User32.WindowStylesEx)User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
                const User32.WindowStylesEx junk = User32.WindowStylesEx.WS_EX_TOOLWINDOW | User32.WindowStylesEx.WS_EX_NOACTIVATE;
                if ((exStyle & junk) == junk) return true;

                // 窗口矩形：DWM 扩展边界（去阴影），失败回退 GetWindowRect
                RECT wr = default;
                bool hasWr = false;
                try
                {
                    if (DwmGetExtendedFrameBounds(hWnd.DangerousGetHandle(), 9, ref wr, Marshal.SizeOf<RECT>()) == 0)
                        hasWr = wr.Width > 0 && wr.Height > 0;
                }
                catch { }
                if (!hasWr)
                {
                    if (!User32.GetWindowRect(hWnd, out wr)) return true;
                }
                if (wr.Width <= 5 || wr.Height <= 5) return true;

                var winRect = new Rect(wr.left / _scale, wr.top / _scale, wr.Width / _scale, wr.Height / _scale);

                // 客户区（若与窗口矩形明显不同）：放在窗口矩形之前入列，使悬停优先命中内容区
                Rect? clientRect = null;
                try
                {
                    if (User32.GetClientRect(hWnd, out RECT cr) && cr.Width > 5 && cr.Height > 5)
                    {
                        POINT tl = new POINT { x = 0, y = 0 };
                        if (User32.ClientToScreen(hWnd, ref tl))
                        {
                            var c = new Rect((tl.x + cr.left) / _scale, (tl.y + cr.top) / _scale,
                                cr.Width / _scale, cr.Height / _scale);
                            if (Math.Abs(c.X - winRect.X) > 2 || Math.Abs(c.Y - winRect.Y) > 2 ||
                                Math.Abs(c.Width - winRect.Width) > 2 || Math.Abs(c.Height - winRect.Height) > 2)
                            {
                                clientRect = c;
                            }
                        }
                    }
                }
                catch { }

                if (clientRect.HasValue) raw.Add((clientRect.Value, false));
                raw.Add((winRect, true));
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        // 去重：仅对非顶级窗口（client rect）做包含剔除，顶级窗口始终保留
        var result = new List<Rect>();
        foreach (var (rect, isWindow) in raw)
        {
            bool keep = true;
            if (!isWindow)
            {
                foreach (var r in result)
                {
                    // Windows.Foundation.Rect 没有 Contains(Rect)，手动判断 outer 是否包含 inner
                    if (r.X <= rect.X && r.Y <= rect.Y &&
                        r.X + r.Width >= rect.X + rect.Width &&
                        r.Y + r.Height >= rect.Y + rect.Height)
                    { keep = false; break; }
                }
            }
            if (keep) result.Add(rect);
        }
        _windowRects = result;

        // 窗口列表就绪后，立即对初始光标位置做悬停命中——不必等第一次 PointerMoved
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed)
            {
                UpdateHover(_currentMousePos);
            }
        });
    }


    private void Canvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
    }


    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_isClosed) return;

        _dashOffset = (float)_timer.Elapsed.TotalSeconds * -15;
        var ds = args.DrawingSession;

        // 首帧锁定画布尺寸
        if (!_sizeLocked)
        {
            _lockedW = (float)sender.Size.Width;
            _lockedH = (float)sender.Size.Height;
            _sizeLocked = true;
        }

        float physW = (float)_displayBitmap.SizeInPixels.Width;
        float physH = (float)_displayBitmap.SizeInPixels.Height;

        // 1. 画冻结帧（铺满，尺寸锁定，不动）
        ds.DrawImage(_displayBitmap,
            new Rect(0, 0, _lockedW, _lockedH),
            new Rect(0, 0, physW, physH),
            1f, CanvasImageInterpolation.Linear);

        // 1b. 整帧压黑 alpha 51（BackgroundDimStrength=20 → 255*0.2）
        ds.FillRectangle(new Rect(0, 0, _lockedW, _lockedH), Color.FromArgb(51, 0, 0, 0));

        // 2. 选区或悬停边框（纯绘图，不碰冻结帧）
        Rect rect = default;
        bool hasRect = false;

        if (_isMouseDown && SelectionRect.Width > MinimumRectangleSize && SelectionRect.Height > MinimumRectangleSize)
        {
            rect = SelectionRect;
            hasRect = true;
        }
        else if (_hasHover && _hoverRect.Width > 2 && _hoverRect.Height > 2)
        {
            rect = _hoverRect;
            hasRect = true;
        }

        if (hasRect)
        {
            // 选区/hover 位置挖洞：重画干净原图抵消压黑（backgroundHighlight）
            // hover rect 可能含标题栏/阴影（位置负，超画布），只挖与画布的交集，避免 sourceRect 越出 bitmap 边界被拉伸
            double cx = Math.Max(rect.X, 0);
            double cy = Math.Max(rect.Y, 0);
            double cw = Math.Min(rect.X + rect.Width, _lockedW) - cx;
            double ch = Math.Min(rect.Y + rect.Height, _lockedH) - cy;
            var clip = new Rect(cx, cy, cw, ch);
            if (clip.Width > 0 && clip.Height > 0)
            {
                ds.DrawImage(_displayBitmap,
                    clip,
                    new Rect(clip.X / _lockedW * physW, clip.Y / _lockedH * physH,
                             clip.Width / _lockedW * physW, clip.Height / _lockedH * physH),
                    1f, CanvasImageInterpolation.Linear);
            }

            ds.DrawRectangle(rect, Colors.Black, 1);
            using var anim = new CanvasStrokeStyle { CustomDashStyle = new float[] { 5, 5 }, DashOffset = _dashOffset };
            ds.DrawRectangle(rect, Colors.White, 1, anim);

            // 与 GetPhysicalSourceRect 一致：拖拽中 +1，悬停窗口不 +1
            var phys = ComputePhysicalRect(rect, _isMouseDown);
            DrawInfoBox(ds, $"X: {(int)phys.X}, Y: {(int)phys.Y}, W: {(int)phys.Width}, H: {(int)phys.Height}",
                new Vector2((float)rect.X + 3, (float)rect.Y + 3));
        }

        // 3+4. 放大镜与鼠标坐标框都钳制到光标所在显示器（不跨屏）
        float mx = (float)_currentMousePos.X, my = (float)_currentMousePos.Y;
        GetActiveMonitorDip(mx, my, out float ml, out float mt, out float mr, out float mb);
        DrawMagnifier(ds, mx, my, ml, mt, mr, mb);

        // 鼠标坐标框：同样钳制到当前显示器
        const float cbW = 160, cbH = 22, cbOff = 12;
        float cbX = mx + cbOff, cbY = my + cbOff;
        if (cbX + cbW > mr) cbX = mx - cbOff - cbW;
        if (cbY + cbH > mb) cbY = my - cbOff - cbH;
        if (cbX < ml) cbX = ml;
        if (cbY < mt) cbY = mt;
        DrawInfoBox(ds, $"X: {(int)(mx * _scale)} Y: {(int)(my * _scale)}", new Vector2(cbX, cbY));

        if (!_isClosed)
        {
            Canvas.Invalidate();
        }
    }


    // 光标所在显示器在 canvas DIP 坐标下的边界（放大镜、坐标框共用，不跨屏）
    private void GetActiveMonitorDip(float mx, float my, out float l, out float t, out float r, out float b)
    {
        l = 0; t = 0; r = _lockedW; b = _lockedH;
        try
        {
            POINT phys = new POINT { x = (int)(mx * _scale + _vx), y = (int)(my * _scale + _vy) };
            var mon = User32.MonitorFromPoint(phys, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            var mi = new User32.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<User32.MONITORINFOEX>() };
            if (User32.GetMonitorInfo(mon, ref mi))
            {
                l = (mi.rcMonitor.left - _vx) / _scale;
                t = (mi.rcMonitor.top - _vy) / _scale;
                r = (mi.rcMonitor.right - _vx) / _scale;
                b = (mi.rcMonitor.bottom - _vy) / _scale;
            }
        }
        catch { }
    }

    private void DrawMagnifier(CanvasDrawingSession ds, float mx, float my, float monLeft, float monTop, float monRight, float monBottom)
    {
        int halfCount = MagnifierPixelCount / 2;
        int magSize = MagnifierPixelCount * MagnifierPixelSize;
        const int offset = 10;

        float destX = mx + offset;
        float destY = my + offset;
        if (destX + magSize > monRight) destX = mx - offset - magSize;
        if (destY + magSize > monBottom) destY = my - offset - magSize;
        if (destX < monLeft) destX = monLeft;
        if (destY < monTop) destY = monTop;

        // 源矩形整数对齐，让 NearestNeighbor 真正锐利（不再糊）
        int srcX = (int)Math.Floor(mx * _scale) - halfCount;
        int srcY = (int)Math.Floor(my * _scale) - halfCount;
        // 钳制到 bitmap bounds 内：鼠标在屏幕边缘时 srcX/srcY 可能负或越界，
        // DrawImage sourceRect 越出 bitmap → E_BOUNDS → stowed exception → fail-fast
        srcX = Math.Clamp(srcX, 0, (int)_displayBitmap.SizeInPixels.Width - MagnifierPixelCount);
        srcY = Math.Clamp(srcY, 0, (int)_displayBitmap.SizeInPixels.Height - MagnifierPixelCount);

        ds.DrawImage(_displayBitmap,
            new Rect(destX, destY, magSize, magSize),
            new Rect(srcX, srcY, MagnifierPixelCount, MagnifierPixelCount),
            1f, CanvasImageInterpolation.NearestNeighbor);

        // 像素网格：让放大的每个像素清晰可辨
        var grid = Color.FromArgb(45, 0, 0, 0);
        for (int i = 1; i < MagnifierPixelCount; i++)
        {
            float gx = destX + i * MagnifierPixelSize;
            float gy = destY + i * MagnifierPixelSize;
            ds.DrawLine(new Vector2(gx, destY), new Vector2(gx, destY + magSize), grid, 1);
            ds.DrawLine(new Vector2(destX, gy), new Vector2(destX + magSize, gy), grid, 1);
        }

        ds.DrawRectangle(new Rect(destX - 1, destY - 1, magSize + 2, magSize + 2), Colors.White, 1);
        ds.DrawRectangle(new Rect(destX, destY, magSize, magSize), Colors.Black, 1);

        float cx = destX + magSize / 2f;
        float cy = destY + magSize / 2f;
        float ps = MagnifierPixelSize / 2f;
        var cc = Color.FromArgb(125, 173, 216, 230);
        ds.FillRectangle(new Rect(destX, cy - ps / 2, cx - ps / 2 - destX, ps), cc);
        ds.FillRectangle(new Rect(cx + ps / 2, cy - ps / 2, destX + magSize - cx - ps / 2, ps), cc);
        ds.FillRectangle(new Rect(cx - ps / 2, destY, ps, cy - ps / 2 - destY), cc);
        ds.FillRectangle(new Rect(cx - ps / 2, cy + ps / 2, ps, destY + magSize - cy - ps / 2), cc);
    }


    private void DrawInfoBox(CanvasDrawingSession ds, string text, Vector2 pos)
    {
        try
        {
            using var fmt = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat { FontSize = 13, FontFamily = "Consolas" };
            using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(ds, text, fmt, 400, 30);
            float w = (float)layout.LayoutBounds.Width;
            float h = (float)layout.LayoutBounds.Height;
            var bgRect = new Rect(pos.X - 3, pos.Y - 2, w + 6, h + 4);
            ds.FillRoundedRectangle(bgRect, 3, 3, Color.FromArgb(200, 0, 0, 0));
            ds.DrawRoundedRectangle(bgRect, 3, 3, Color.FromArgb(200, 128, 128, 128), 1);
            ds.DrawTextLayout(layout, pos, Colors.White);
        }
        catch { }
    }


    // ===== 鼠标事件 =====

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Canvas);
        _currentMousePos = pt.Position;
        if (pt.Properties.IsLeftButtonPressed)
        {
            _positionOnClick = pt.Position;
            _isMouseDown = true;
            _selectionFromDrag = true;
            _pressedOnHover = _hasHover;  // 记下：是否在悬停窗口上按下（单击截图用）
            SelectionRect = new Rect(pt.Position.X, pt.Position.Y, 0, 0);
            e.Handled = true;
        }
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(Canvas).Position;
        _currentMousePos = pos;

        if (_isMouseDown)
        {
            double x = Math.Min(_positionOnClick.X, pos.X);
            double y = Math.Min(_positionOnClick.Y, pos.Y);
            double w = Math.Abs(pos.X - _positionOnClick.X);
            double h = Math.Abs(pos.Y - _positionOnClick.Y);
            SelectionRect = new Rect(x, y, w, h);
        }
        else
        {
            UpdateHover(pos);
        }
        e.Handled = true;
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Canvas);

        if (pt.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased)
        {
            if (_isMouseDown)
            {
                _isMouseDown = false;
                SelectionRect = default;
            }
            else
            {
                CloseWindow();
            }
            e.Handled = true;
            return;
        }

        if (_isMouseDown)
        {
            _isMouseDown = false;
            if (SelectionRect.Width > MinimumRectangleSize && SelectionRect.Height > MinimumRectangleSize)
            {
                // 拖拽选区
                IsConfirmed = true;
                CloseWindow();
            }
            else if (_pressedOnHover && _hoverRect.Width > 2 && _hoverRect.Height > 2)
            {
                // 单击（未拖动）落在悬停窗口上 → 直接截该窗口（QuickCrop）
                SelectionRect = _hoverRect;
                _selectionFromDrag = false;
                IsConfirmed = true;
                CloseWindow();
            }
            e.Handled = true;
        }
    }

    private void UpdateHover(Point pos)
    {
        // _windowRects 为 EnumWindows 的 Z 序（顶层在前），首个命中即最上层窗口。
        // 不能选"最小矩形"，否则会高亮被遮挡的后台小窗口（同 FindSelectedWindow 语义）。
        _hasHover = false;
        foreach (var rect in _windowRects)
        {
            if (rect.Contains(pos))
            {
                _hoverRect = rect;
                _hasHover = true;
                return;
            }
        }
    }

    private void CloseWindow()
    {
        if (IsConfirmed)
        {
            // _displayBitmap 是冻结帧的 SDR 版（覆盖层已 tonemap），关窗前（它还活着）裁出选区给剪贴板
            SdrCrop = CropDisplayToBgra();
        }
        _isClosed = true;
        this.Hide();   // 立即从屏幕消失，不等 Close() 的异步收尾（避免最后一帧遮罩残留）
        this.Close();
    }

    // 从 _displayBitmap 裁出选区为 B8G8R8A8 SDR（CF_DIB 要 BGRA）
    private CanvasRenderTarget CropDisplayToBgra()
    {
        var srcRect = GetPhysicalSourceRect();
        int w = (int)srcRect.Width;
        int h = (int)srcRect.Height;
        var device = CanvasDevice.GetSharedDevice();
        var rt = new CanvasRenderTarget(device, w, h, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var ds = rt.CreateDrawingSession())
        {
            ds.DrawImage(_displayBitmap, new Windows.Foundation.Rect(0, 0, w, h), srcRect, 1f, CanvasImageInterpolation.Linear);
        }
        return rt;
    }

    private void RegionCaptureWindow_Closed(object sender, WindowEventArgs e)
    {
        Cleanup();
    }

    // 释放覆盖层资源：移除 CanvasControl（Win2D 已知泄漏点）、释放自有的显示位图。
    // _canvasOriginal（= composite）归调用方，不在此释放。
    public void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _isClosed = true;
        try { Canvas.RemoveFromVisualTree(); } catch { }
        if (_ownsDisplayBitmap)
        {
            _displayBitmap?.Dispose();
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseWindow();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter && _hasHover && !_isMouseDown)
        {
            SelectionRect = _hoverRect;
            _selectionFromDrag = false;
            IsConfirmed = true;
            CloseWindow();
            e.Handled = true;
        }
    }

    protected override nint WindowSubclassProc(HWND hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (uMsg == (uint)User32.WindowMessage.WM_RBUTTONUP)
        {
            if (_isMouseDown)
            {
                _isMouseDown = false;
                SelectionRect = default;
            }
            else
            {
                CloseWindow();
            }
            return 0;
        }
        return base.WindowSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
    }

    public Rect GetPhysicalSourceRect()
    {
        return ComputePhysicalRect(SelectionRect, _selectionFromDrag);
    }

    // 鼠标框选时两端是光标像素索引（含端点），宽 = |x2-x1| + 1（CreateRectangle）；
    // 窗口矩形本身就是正常尺寸，不 +1。WinUI 指针是 DIP，先 round 成物理像素索引。
    private Rect ComputePhysicalRect(Rect dipRect, bool fromDrag)
    {
        double ratioX = _canvasOriginal.SizeInPixels.Width / _lockedW;
        double ratioY = _canvasOriginal.SizeInPixels.Height / _lockedH;

        int x1 = (int)Math.Round(dipRect.X * ratioX);
        int y1 = (int)Math.Round(dipRect.Y * ratioY);
        int x2 = (int)Math.Round((dipRect.X + dipRect.Width) * ratioX);
        int y2 = (int)Math.Round((dipRect.Y + dipRect.Height) * ratioY);

        int x = Math.Min(x1, x2);
        int y = Math.Min(y1, y2);
        int physW = (int)_canvasOriginal.SizeInPixels.Width;
        int physH = (int)_canvasOriginal.SizeInPixels.Height;
        int w = Math.Abs(x2 - x1) + (fromDrag ? 1 : 0);
        int h = Math.Abs(y2 - y1) + (fromDrag ? 1 : 0);
        // 选区/hover 经 ratio 缩放 + round 后可能落在画布物理边界外（边缘 round 把 x2/y2 顶到 physW/physH+1），
        // physW-x / physH-y 此时会为负，必须 clamp 到 0，否则 new Rect 负宽高抛 ArgumentOutOfRangeException
        w = Math.Max(0, Math.Min(w, physW - x));
        h = Math.Max(0, Math.Min(h, physH - y));
        return new Rect(x, y, w, h);
    }

}
