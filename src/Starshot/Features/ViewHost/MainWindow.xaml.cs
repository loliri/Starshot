using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
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
    private const int HOTKEY_CAPTURE = 44445;

    private const int HOTKEY_REGION = 44446;

    private const int HOTKEY_REGION_COPY = 44447;

    private const int HOTKEY_REGION_OCR = 44448;

    public bool ForceExit;

    private SystemBackdropHelper? _backdropHelper;

    private Brush? _overlayAcrylicBrush; // 首次 ApplyBackdrop 时捕获 XAML 设的亚克力（用于亚克力开关还原）

    private static readonly Brush _transparentBrush = new SolidColorBrush(
        Color.FromArgb(0, 0, 0, 0)
    );

    public MainWindow()
    {
        InitializeComponent();
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
        Activated += MainWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
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
            _ = TryCheckUpdateOnStartupAsync();
        };
        sb.Begin();
    }

    private static async Task TryCheckUpdateOnStartupAsync()
    {
#if !DEBUG
        try
        {
            if (!AppConfig.EnableAutoUpdateCheck)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - AppConfig.LastUpdateCheckTime < 86400)
                return;
            var (release, _) = await UpdateService.CheckUpdateAsync();
            // 成功查询后才更新时间戳：网络失败抛异常会跳过本行（catch 兜底），下次启动即重试
            AppConfig.LastUpdateCheckTime = now;
            if (release is null)
                return;
            var window = new UpdateWindow();
            window.SetRelease(release);
        }
        catch { }
#endif
    }

    /// <summary>
    /// 壁纸开：关 Mica + overlay 隔层显（亚克力开=磨砂，关=透明让壁纸直接透出）；壁纸关：Mica + overlay 隐。
    /// 壁纸渲染本身由 AppBackground 据 EnableWallpaper 自管。
    /// </summary>
    public void ApplyBackdrop()
    {
        if (AppConfig.EnableWallpaper)
        {
            _backdropHelper?.ResetBackdrop();
            _overlayAcrylicBrush ??= Border_OverlayMask.Background; // 首次捕获 XAML 亚克力
            Border_OverlayMask.Background = AppConfig.EnableAcrylic
                ? _overlayAcrylicBrush!
                : _transparentBrush;
            Border_OverlayMask.Opacity = 1;
        }
        else
        {
            _backdropHelper ??= new SystemBackdropHelper(this);
            _backdropHelper.TrySetMica();
            Border_OverlayMask.Opacity = 0;
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= MainWindow_Activated;
        NavView.SelectedItem = NavView.MenuItems[Math.Clamp(AppConfig.StartPage, 0, 1)];
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
        if (ForceExit)
        {
            return;
        }
        // 托盘恒开：关窗即最小化到托盘
        args.Cancel = true;
        Hide();
    }

    protected override nint WindowSubclassProc(
        HWND hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData
    )
    {
        if (uMsg == (uint)User32.WindowMessage.WM_HOTKEY)
        {
            if (wParam == HOTKEY_CAPTURE)
            {
                ScreenCaptureService.Capture();
            }
            else if (wParam == HOTKEY_REGION)
            {
                ScreenCaptureService.CaptureRegion();
            }
            else if (wParam == HOTKEY_REGION_COPY)
            {
                ScreenCaptureService.CaptureRegionCopyOnly();
            }
            else if (wParam == HOTKEY_REGION_OCR)
            {
                ScreenCaptureService.CaptureRegionOcrCopy();
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
