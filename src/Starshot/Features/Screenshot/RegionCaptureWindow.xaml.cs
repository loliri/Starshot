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
    public CanvasRenderTarget? SdrCrop { get; private set; }

    private CanvasBitmap _canvasOriginal;    // 原始帧（裁剪用，可能 HDR），每次 SetCapture 更新
    private CanvasBitmap? _displayBitmap;    // 显示用（SDR 色调映射后），每次 SetCapture 重建；会话间为 null（CloseWindow 清引用）
    private float _scale;
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
    private readonly System.Diagnostics.Stopwatch _timer;
    private bool _isClosed;
    private CanvasSwapChain? _swapChain;
    private DispatcherTimer _renderTimer;

    // 锁定画布尺寸（首帧后固定，防止布局抖动导致冻结帧移动）
    private float _lockedW;
    private float _lockedH;
    private bool _sizeLocked;

    // HDR 时 _displayBitmap 是本窗新建的 SDR 副本，由本窗释放；
    // SDR 时它就是传入的 canvas（= composite），归调用方，不能动
    private bool _ownsDisplayBitmap;
    private bool _cleanedUp;
    // 关窗移屏外方案配套：截图前的前台窗口（关窗时还焦点）、待移回屏内标记与节拍计数
    private nint _prevForeground;
    private bool _pendingMoveIn;
    private int _moveInTick;

    // 单例：选区完成信号（替代 Closed），ScreenCaptureService await 它；窗口不 Close 只 Hide
    public TaskCompletionSource<bool> Completion { get; private set; }


    public RegionCaptureWindow()
    {
        InitializeComponent();
        _timer = System.Diagnostics.Stopwatch.StartNew();
        this.Closed += RegionCaptureWindow_Closed;

        // 窗口设置（单例，只一次）
        // 不写 WindowEx.MainWindowId：那是主窗口的静态锚（CenterInScreen 用），覆盖层窗口
        // 既不用居中，写它会歪曲 UpdateWindow/WelcomeWindow 的定位（且单例重建时会反复覆盖）
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
        // 任务栏硬保证：窗口永不 Hide（只挪屏外）后 IsWindowVisible 恒真，IsShownInSwitchers=false 只是提示，
        // 激活/样式手术等时机会失守让窗口冒进任务栏；TOOLWINDOW + 清 APPWINDOW 是 shell 层的硬规则
        var exStyle = (User32.WindowStylesEx)User32.GetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE);
        exStyle |= User32.WindowStylesEx.WS_EX_TOOLWINDOW;
        exStyle &= ~User32.WindowStylesEx.WS_EX_APPWINDOW;
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE, (nint)exStyle);
        User32.SetWindowPos(WindowHandle, IntPtr.Zero, vx, vy, vw, vh, (User32.SetWindowPosFlags)0x0020 | User32.SetWindowPosFlags.SWP_NOZORDER);

        PointerCursor.SetCursorShape(Canvas, InputSystemCursorShape.Cross);

        // _scale 按覆盖层窗口 DPI（d56df02）；swapChain 移到 SetCapture 创建（CloseWindow 释放本进程显存）
        float dpi = User32.GetDpiForWindow(WindowHandle);
        _scale = dpi / 96f;
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) => Redraw();
        // 不 Start：SetCapture 时启动（单例每次截图复用窗口）
    }


    /// <summary>
    /// 窗口被用户真关（任务栏/系统关闭）后置 true 且不再复位——
    /// service 据此丢弃单例重建窗口，SetCapture 也不会再碰已销毁的 HWND。
    /// </summary>
    public bool IsDestroyed { get; private set; }


    /// <summary>
    /// 每次截图调用：更新冻结帧 + 重置交互状态 + 显示。窗口单例且永不 Hide——
    /// 关窗时移到屏外保持 IsWindowVisible（合成管线不停摆），下次截图先把新帧 Present 上屏
    /// 再移回屏内，从根上避免 Show 瞬间 DWM 先合成保留的旧会话帧（启动闪上次截图界面）。
    /// </summary>
    public void SetCapture(CanvasBitmap canvas, float sdrWhiteLevel, int physW, int physH)
    {
        // swapChain 常驻（关窗只移屏外不销毁）；分辨率变了尺寸过期则重建
        float needW = physW / _scale, needH = physH / _scale;
        if (_swapChain is null
            || Math.Abs((float)_swapChain.Size.Width - needW) > 0.5f
            || Math.Abs((float)_swapChain.Size.Height - needH) > 0.5f)
        {
            try { Canvas.SwapChain = null; _swapChain?.Dispose(); } catch { }
            _swapChain = new CanvasSwapChain(CanvasDevice.GetSharedDevice(), needW, needH, _scale * 96f, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, CanvasAlphaMode.Premultiplied);
            Canvas.SwapChain = _swapChain;
        }

        // 更新帧（旧的已在 CloseWindow 释放并清引用）
        _canvasOriginal = canvas;
        _displayBitmap = CreateDisplayBitmap(canvas, physW, physH, sdrWhiteLevel);
        _ownsDisplayBitmap = !ReferenceEquals(_displayBitmap, canvas);

        // 重置交互状态（为本次截图清场）
        SelectionRect = default;
        IsConfirmed = false;
        SdrCrop = null;
        _positionOnClick = default;
        _isMouseDown = false;
        _pressedOnHover = false;
        if (User32.GetCursorPos(out var initCursor))
        {
            _currentMousePos = new Point((initCursor.x - _vx) / _scale, (initCursor.y - _vy) / _scale);
        }
        _selectionFromDrag = false;
        _windowRects = new List<Rect>();
        _hoverRect = default;
        _hasHover = false;
        _lockedW = 0;
        _lockedH = 0;
        _sizeLocked = false;  // 首帧重新锁尺寸 + 触发 DetectWindows
        _cleanedUp = false;
        Completion = new TaskCompletionSource<bool>();
        _prevForeground = (nint)User32.GetForegroundWindow();

        Show();          // 首次显示；后续会话窗口一直可见（在屏外），no-op
        // 会话激活放在 Show 之后：万一 Show 在已销毁窗口上抛（用户真关过窗、service 未能重建的兜底路径），
        // _isClosed 仍为 true、timer 未启动，Redraw 守卫生效，不会拿已释放的帧再画导致 FATAL
        _isClosed = false;
        _renderTimer.Start();
        Redraw();        // 屏外先把新冻结帧 Present 上屏（窗口可见，合成照常提交）
        _pendingMoveIn = true;   // 第 2 个 tick（新帧确定已合成）再移回屏内，移回瞬间不可能是旧内容
        _moveInTick = 0;
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


    private void Redraw()
    {
        if (_isClosed || _swapChain is null || _displayBitmap is null) return;

        _dashOffset = (float)_timer.Elapsed.TotalSeconds * -15;

        // 首帧锁定画布尺寸（_scale 构造时已按覆盖层窗口 DPI 设定）
        if (!_sizeLocked)
        {
            _lockedW = (float)_swapChain.Size.Width;
            _lockedH = (float)_swapChain.Size.Height;
            _sizeLocked = true;
            if (User32.GetCursorPos(out var initCursor))
            {
                _currentMousePos = new Point((initCursor.x - _vx) / _scale, (initCursor.y - _vy) / _scale);
            }
            _ = Task.Run(DetectWindows);
        }

        using (var ds = _swapChain.CreateDrawingSession(Colors.Transparent))
        {
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
            double cw = Math.Max(0, Math.Min(rect.X + rect.Width, _lockedW) - cx);
            double ch = Math.Max(0, Math.Min(rect.Y + rect.Height, _lockedH) - cy);
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
        }
        _swapChain.Present();

        // SetCapture 挂的移回任务：第 2 个 tick（首帧 Present 已过 16ms，新帧确定合成进 surface）移回屏内
        if (_pendingMoveIn)
        {
            _moveInTick++;
            if (_moveInTick >= 2)
            {
                _pendingMoveIn = false;
                MoveOnscreen();
            }
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
        if (_displayBitmap is null) return;
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
        try { _renderTimer?.Stop(); } catch { }
        if (IsConfirmed)
        {
            // _displayBitmap 是冻结帧的 SDR 版（覆盖层已 tonemap），隐藏前（它还活着）裁出选区给剪贴板
            try { SdrCrop = CropDisplayToBgra(); } catch { }
        }
        _isClosed = true;
        // 不 Hide：移到屏外保持 IsWindowVisible，合成管线不停摆，
        // 否则下次 Show 瞬间 DWM 先合成保留的旧会话帧（启动闪上次截图的完整界面）
        User32.SetWindowPos(WindowHandle, IntPtr.Zero, -32000, -32000, 0, 0,
            User32.SetWindowPosFlags.SWP_NOSIZE | User32.SetWindowPosFlags.SWP_NOZORDER | User32.SetWindowPosFlags.SWP_NOACTIVATE);
        // 交还焦点（屏外窗口不 Hide 仍持有键盘焦点，不还的话用户打字被吞）
        if (_prevForeground != 0 && _prevForeground != (nint)WindowHandle)
        {
            try { User32.SetForegroundWindow(new HWND(_prevForeground)); } catch { }
        }
        if (_ownsDisplayBitmap) { try { _displayBitmap?.Dispose(); } catch { } }
        // _displayBitmap 引用清掉（自有的已 dispose）；_canvasOriginal 不清：
        // service 在 Completion 后还要 GetPhysicalSourceRect 读它（底层是 service 的 composite，由 service dispose）；
        // swapChain 常驻不销毁（屏外窗口还靠它承接下次会话的 Present）
        _displayBitmap = null;
        _ownsDisplayBitmap = false;
        Completion?.TrySetResult(IsConfirmed);
    }


    /// <summary>移回虚拟屏幕原位（SetCapture 后第 2 个 tick 调：新帧已合成，移回瞬间不闪旧内容）。</summary>
    private void MoveOnscreen()
    {
        int vx = User32.GetSystemMetrics((User32.SystemMetric)76);
        int vy = User32.GetSystemMetrics((User32.SystemMetric)77);
        int vw = User32.GetSystemMetrics((User32.SystemMetric)78);
        int vh = User32.GetSystemMetrics((User32.SystemMetric)79);
        User32.SetWindowPos(WindowHandle, IntPtr.Zero, vx, vy, vw, vh, User32.SetWindowPosFlags.SWP_NOZORDER);
        Activate();
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
        // 用户从任务栏/系统真关了窗口（正常运行期我们只移屏外不 Close）：
        // 标记销毁让 service 下次重建，放行 pending 的 Completion 防 service 悬等
        IsDestroyed = true;
        Completion?.TrySetResult(false);
        Cleanup();
    }

    /// <summary>
    /// 释放覆盖层资源。窗口被用户真关（任务栏/系统关闭）时调用：运行期单例只移屏外不 Close，真关才真销毁；
    /// 进程退出的资源回收靠进程终止本身。
    /// </summary>
    public void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _isClosed = true;
        try { _renderTimer?.Stop(); } catch { }
        try { Canvas.SwapChain = null; } catch { }
        try { Canvas.RemoveFromVisualTree(); } catch { }
        try { _swapChain?.Dispose(); _swapChain = null; } catch { }
        if (_ownsDisplayBitmap) { try { _displayBitmap?.Dispose(); } catch { } }
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
