using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starshot.Features.About;
using Starshot.Features.Background;
using Starshot.Features.Screenshot;
using Starshot.Features.Setting;
using Starshot.Frameworks;
using Starshot.Helpers;
using Vanara.PInvoke;
using Windows.Graphics;

namespace Starshot.Features.ViewHost;

public sealed partial class MainWindow : WindowEx
{

    private const int HOTKEY_CAPTURE = 44445;

    private const int HOTKEY_REGION = 44446;

    private const int HOTKEY_REGION_COPY = 44447;

    public bool ForceExit;

    private SystemBackdropHelper? _backdropHelper;


    public MainWindow()
    {
        InitializeComponent();
        WindowEx.MainWindowId = AppWindow.Id;
        Title = "Starshot";
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AdaptTitleBarButtonColorToActuallTheme();
        SetDragRectangles(new RectInt32(0, 0, 100000, (int)(48 * UIScale)));
        ApplyBackdrop();
        ApplyTheme();
        HotkeyManager.InitializeHotkey(WindowHandle);
        WeakReferenceMessenger.Default.Register<AccentColorChangedMessage>(this, (_, _) => OnAccentChanged());
        WeakReferenceMessenger.Default.Register<BackgroundChangedMessage>(this, (_, _) => ApplyBackdrop());
        Activated += MainWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
    }


    /// <summary>
    /// 壁纸开：关 Mica + overlay 隔层显；壁纸关：Mica + overlay 隐。
    /// 壁纸渲染本身由 AppBackground 据 EnableWallpaper 自管。
    /// </summary>
    private void ApplyBackdrop()
    {
        if (AppConfig.EnableWallpaper)
        {
            _backdropHelper?.ResetBackdrop();
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
        NavView.SelectedItem = NavView.MenuItems[0];
    }


    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            var pageType = tag switch
            {
                "Gallery" => typeof(ScreenshotPage),
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
        if (AppConfig.EnableSystemTrayIcon)
        {
            args.Cancel = true;
            Hide();
        }
    }


    protected override nint WindowSubclassProc(HWND hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
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
        }
        return base.WindowSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
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
