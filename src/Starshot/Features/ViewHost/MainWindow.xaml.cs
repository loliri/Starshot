using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Starshot.Features.About;
using Starshot.Features.Background;
using Starshot.Features.Screenshot;
using Starshot.Features.Setting;
using Starshot.Features.Update;
using Starshot.Frameworks;
using Starshot.Helpers;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.UI;

namespace Starshot.Features.ViewHost;

public sealed partial class MainWindow : WindowEx
{
    public bool ForceExit;

    /// <summary>
    /// 最近一次正常态（非最大化/最小化）的位置大小，物理像素
    /// </summary>
    private RectInt32 _placement;

    private bool _placementMaximized;

    private DispatcherQueueTimer? _placementTimer;

    /// <summary>
    /// 恢复几何期间为 true：WM_GETMINMAXINFO 不限制最小尺寸（见该方法内注释）
    /// </summary>
    private bool _restoringPlacement;

    /// <summary>
    /// 待首次激活后再摆一次的几何（跨缩放比显示器时修正尺寸），物理像素
    /// </summary>
    private RectInt32? _pendingPlacement;

    private SystemBackdropHelper? _backdropHelper;

    private Brush? _overlayAcrylicBrush; // 首次 ApplyBackdrop 时捕获 XAML 设的亚克力（用于亚克力开关还原）

    // 上一次 ApplyBackdrop 时壁纸层是否视同不存在（不透明度 0）；滑条只在跨过这条边界时才需要重算 backdrop
    private bool _wallpaperLayerHidden;

    private static readonly Brush _transparentBrush = new SolidColorBrush(
        Color.FromArgb(0, 0, 0, 0)
    );

    public MainWindow()
    {
        InitializeComponent();
        RestorePlacement();
        WindowEx.MainWindowId = AppWindow.Id;
        Title = "Starshot";
        // 任务管理器图标：AppWindow.SetIcon 走系统图标管线（LoadIcon 直挂 WM_SETICON 会被拉伸畸形）
        SetIcon();
        LoadPaneLogo();
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AdaptTitleBarButtonColorToActuallTheme();
        SetDragRectangles(new RectInt32(0, 0, 100000, (int)(48 * UIScale)));
        ApplyBackdrop();
        ApplyTheme();
        // 热键由 SystemTrayWindow 注册（托盘恒开，它是常驻宿主；--hide 场景唯一常驻窗口）
        WeakReferenceMessenger.Default.Register<AccentColorChangedMessage>(
            this,
            (_, _) => OnAccentChanged()
        );
        WeakReferenceMessenger.Default.Register<BackgroundChangedMessage>(
            this,
            (_, _) => ApplyBackdrop()
        );
        WeakReferenceMessenger.Default.Register<WallpaperOpacityChangedMessage>(
            this,
            (_, _) =>
            {
                // 中间值只改图片自身透明度，backdrop 不用动；只有「壁纸层视同不存在」这条边界翻转时才重算，
                // 免得拖动滑条时每一帧都重建一次 Mica
                if (IsWallpaperLayerHidden() != _wallpaperLayerHidden)
                {
                    ApplyBackdrop();
                }
            }
        );
        Activated += MainWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
        AppWindow.Changed += AppWindow_Changed;
        ((FrameworkElement)Content).Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// 导航栏 logo：exe 内嵌图标资源抽取（AppIconHelper，与托盘同源），不随包带文件。
    /// </summary>
    private void LoadPaneLogo()
    {
        using var icon = AppIconHelper.GetAppIcon();
        if (icon is null)
            return;
        Image_PaneLogo.Source = AppIconHelper.ToBitmapImage(icon);
    }

    /// <summary>
    /// 启动 splash：Loaded 后固定延迟 700ms，淡出 400ms，露出就绪 UI。每个窗口实例只触发一次（托盘恢复不重播）。
    /// </summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)sender).Loaded -= MainWindow_Loaded;
        // 锁屏/解锁暂停视频壁纸（WM_WTSSESSION_CHANGE 需先注册接收窗口）
        WTSRegisterSessionNotification(WindowHandle, 0);
        await Task.Delay(700);
        var fade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
        };
        Storyboard.SetTarget(fade, SplashOverlay);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Completed += (_, _) =>
        {
            SplashOverlay.Visibility = Visibility.Collapsed;
            InAppToast.FlushPending();
            HotkeyManager.ShowRegistrationErrors();
            if (AppConfig.AutoStartInvalid)
            {
                AppConfig.AutoStartInvalid = false;
                InAppToast.MainWindow?.Warning(null, Lang.Starshot_AutoStartInvalidCleared, 5000);
            }
            if (AppConfig.TaskInvalid)
            {
                AppConfig.TaskInvalid = false;
                InAppToast.MainWindow?.Warning(null, Lang.Starshot_TaskInvalid, 5000);
            }
            StartupUpdateCheck.FlushPending();
        };
        sb.Begin();
    }

    /// <summary>
    /// 壁纸开：关 Mica + overlay 隔层显（亚克力开=磨砂，关=透明让壁纸直接透出）；壁纸关：Mica + overlay 隐。
    /// 壁纸渲染本身由 AppBackground 据 EnableWallpaper 自管。
    /// 不透明度 0 是另一种状态：图片看不见了，此时整扇窗走系统亚克力磨砂，后面的桌面与窗口被糊成一片。
    /// </summary>
    public void ApplyBackdrop()
    {
        _wallpaperLayerHidden = IsWallpaperLayerHidden();
        if (!_wallpaperLayerHidden)
        {
            SystemBackdrop = null;
            _backdropHelper?.ResetBackdrop();
            _overlayAcrylicBrush ??= Border_OverlayMask.Background; // 首次捕获 XAML 亚克力
            Border_OverlayMask.Background = AppConfig.EnableAcrylic
                ? _overlayAcrylicBrush!
                : _transparentBrush;
            Border_OverlayMask.Opacity = 1;
        }
        else if (AppConfig.EnableWallpaper)
        {
            // 壁纸拖着但被打到 0：图片完全看不见了，此时整扇窗换成系统亚克力（磨砂玻璃）。
            // 后面有什么会被糊成一片、只剩轮廓，比纯透底（后面的文字直接压在界面上）好读。
            SystemBackdrop = null;
            _backdropHelper ??= new SystemBackdropHelper(this);
            _backdropHelper.TrySetAcrylic();
            Border_OverlayMask.Opacity = 0;
        }
        else
        {
            SystemBackdrop = null;
            _backdropHelper ??= new SystemBackdropHelper(this);
            _backdropHelper.TrySetMica();
            Border_OverlayMask.Opacity = 0;
        }
    }

    /// <summary>
    /// 壁纸图层是否视同不存在：没开壁纸，或壁纸不透明度拉到 0（图片完全透明，什么都看不见）。
    /// 两种情况 backdrop 处理不同——没开壁纸走 Mica，开着但为 0 走亚克力磨砂。
    /// </summary>
    private static bool IsWallpaperLayerHidden() =>
        !AppConfig.EnableWallpaper || AppConfig.WallpaperOpacity <= 0;

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= MainWindow_Activated;
        _restoringPlacement = false;
        // 首次激活时窗口已经在目标显示器上（DPI 已切换），此时再摆一次，
        // 跨不同缩放比的显示器才能还原到精确的物理尺寸（移动瞬间会按 DIP 比例缩放一次）
        if (_pendingPlacement is RectInt32 pending)
        {
            _pendingPlacement = null;
            ApplyPlacement(pending);
        }
        if (_placementMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
        NavView.SelectedItem = NavView.MenuItems[Math.Clamp(AppConfig.StartPage, 0, 1)];
        // 主窗口从托盘重新打开时补弹后台检查挂起的新版本（首次启动由 splash 结束处的 FlushPending 负责）
        StartupUpdateCheck.FlushPending();
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args
    )
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            var pageType = tag switch
            {
                "Gallery" => typeof(ScreenshotPage),
                "Clipboard" => typeof(ClipboardPage),
                "Appearance" => typeof(AppearanceSetting),
                "Hotkey" => typeof(HotkeySetting),
                "Screenshot" => typeof(ScreenshotSetting),
                "Storage" => typeof(StorageSetting),
                "Settings" => typeof(GeneralSetting),
                "About" => typeof(AboutPage),
                _ => typeof(ScreenshotPage),
            };
            ContentFrame.Navigate(pageType);
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _placementTimer?.Stop();
        SavePlacement();
        if (ForceExit)
        {
            return;
        }
        // 托盘恒开：关窗即最小化到托盘
        args.Cancel = true;
        Hide();
    }

    /// <summary>
    /// 恢复上次关窗时的位置/大小。没有记录（首次启动）或记录损坏则保持系统默认。
    /// 记录是物理像素，直接使用（不做 DIP 换算，多屏缩放比不同时才不会错位），
    /// 再夹到最近显示器的工作区内。
    /// </summary>
    private void RestorePlacement()
    {
        if (
            !WindowPlacement.TryParse(
                AppConfig.MainWindowPlacement,
                out RectInt32 rect,
                out bool maximized
            )
        )
        {
            _placement = new RectInt32(
                AppWindow.Position.X,
                AppWindow.Position.Y,
                AppWindow.Size.Width,
                AppWindow.Size.Height
            );
            return;
        }
        _placement = rect;
        _placementMaximized = maximized;
        // 保持到首次激活：窗口先建在默认屏上，移动到目标屏时才触发 DPI 变化
        _restoringPlacement = true;
        _pendingPlacement = rect;
        ApplyPlacement(rect);
    }

    /// <summary>
    /// 摆到目标几何：先按当前 DPI 补足最小尺寸（避免 SetWindowPos 被 WM_GETMINMAXINFO 顶大后溢出屏幕），
    /// 再夹到最近显示器的工作区内。
    /// </summary>
    private void ApplyPlacement(RectInt32 rect)
    {
        int minWidth = (int)(1200 * UIScale);
        int minHeight = (int)(676 * UIScale);
        RectInt32 target = new(
            rect.X,
            rect.Y,
            Math.Max(rect.Width, minWidth),
            Math.Max(rect.Height, minHeight)
        );
        AppWindow.MoveAndResize(WindowPlacement.EnsureVisible(target));
    }

    /// <summary>
    /// 记录当前几何：最大化时保留进入最大化前的位置（此时 AppWindow.Position/Size 是最大化后的矩形），
    /// 最小化时不写（尺寸不可信）。只在值变化时落盘，SetValue 内部会跳过重复值。
    /// </summary>
    private void SavePlacement()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }
        switch (presenter.State)
        {
            case OverlappedPresenterState.Minimized:
                return;
            case OverlappedPresenterState.Maximized:
                _placementMaximized = true;
                break;
            default:
                _placement = new RectInt32(
                    AppWindow.Position.X,
                    AppWindow.Position.Y,
                    AppWindow.Size.Width,
                    AppWindow.Size.Height
                );
                _placementMaximized = false;
                break;
        }
        AppConfig.MainWindowPlacement = WindowPlacement.Format(_placement, _placementMaximized);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange)
        {
            return;
        }
        // 拖动/缩放途中 Changed 触发很密，防抖 500ms，避免每帧写一次 config
        _placementTimer ??= CreatePlacementTimer();
        _placementTimer.Stop();
        _placementTimer.Start();
    }

    private DispatcherQueueTimer CreatePlacementTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => SavePlacement();
        return timer;
    }

    [DllImport("Wtsapi32.dll")]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    protected override nint WindowSubclassProc(
        HWND hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData
    )
    {
        if (uMsg == (uint)User32.WindowMessage.WM_WTSSESSION_CHANGE)
        {
            // WTS_SESSION_LOCK / WTS_SESSION_UNLOCK：锁屏暂停视频壁纸（省电），
            // 解锁只恢复因锁屏暂停的
            if (wParam == 0x7)
            {
                AppBackground.PauseVideo(sessionLock: true);
            }
            else if (wParam == 0x8)
            {
                AppBackground.PlayVideo(sessionUnlock: true);
            }
        }
        else if (uMsg == (uint)User32.WindowMessage.WM_ACTIVATE)
        {
            // LOWORD = 激活状态：1=WA_ACTIVE, 2=WA_CLICKACTIVE（0=WA_INACTIVE 不发）
            int lo = (int)((long)wParam & 0xFFFF);
            if (lo is 1 or 2)
            {
                WeakReferenceMessenger.Default.Send(
                    new MainWindowStateChangedMessage { Activate = true }
                );
            }
        }
        else if (uMsg == (uint)User32.WindowMessage.WM_GETMINMAXINFO)
        {
            // 恢复期间不限制最小尺寸：窗口在创建时可能还挂在另一块缩放不同的屏上，
            // 此时的最小尺寸按那块屏算，会把跨屏恢复到的小尺寸窗口强行撑大。
            if (_restoringPlacement)
            {
                return 0;
            }
            // 最小尺寸与 Starward 主窗口一致（1200×676 逻辑像素，按 DPI 缩放到物理像素）
            unsafe
            {
                var mmi = (User32.MINMAXINFO*)lParam;
                mmi->minTrackSize = new SIZE((int)(1200 * UIScale), (int)(676 * UIScale));
            }
            // 已处理：不再传 DefWindowProc，默认处理会用系统值覆盖掉刚填的最小尺寸
            return 0;
        }
        return base.WindowSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
    }

    /// <summary>
    /// 重写 Hide：隐藏后通知 AppBackground 暂停视频壁纸（避免不可见时占 GPU）。
    /// </summary>
    public override void Hide()
    {
        // 托盘化时立即落盘，不等防抖定时器（用户可能拖完窗口立刻关窗/退出）
        _placementTimer?.Stop();
        SavePlacement();
        base.Hide();
        WeakReferenceMessenger.Default.Send(new MainWindowStateChangedMessage { Hide = true });
    }

    /// <summary>
    /// 应用主题（0跟随系统 / 1浅色 / 2深色）。先 toggle 再设回，强制 WinUI 重新解析主题资源
    /// ——运行时改 accent/字体后，已存在的 UI 立即跟着变。
    /// </summary>
    public void ApplyTheme()
    {
        if (Content is FrameworkElement root)
        {
            var t = root.ActualTheme;
            root.RequestedTheme = t is ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            root.RequestedTheme = (ElementTheme)AppConfig.Theme;
            AdaptTitleBarButtonColorToActuallTheme();
        }
    }

    /// <summary>
    /// 强调色变更后切换主题强制 WinUI 重解析主题资源
    /// </summary>
    private void OnAccentChanged()
    {
        ApplyTheme();
    }
}
