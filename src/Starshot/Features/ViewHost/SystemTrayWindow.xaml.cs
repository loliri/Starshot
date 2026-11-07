using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Starshot.Features.Screenshot;
using Starshot.Features.Setting;
using Starshot.Frameworks;
using Starshot.Helpers;
using Vanara.PInvoke;
using Windows.Foundation;

namespace Starshot.Features.ViewHost;

[INotifyPropertyChanged]
public sealed partial class SystemTrayWindow : WindowEx
{
    // 与 HotkeyManager 的 id 对齐：--hide 启动时 MainWindow 不创建，热键由本窗口接管
    private const int HOTKEY_CAPTURE = 44445;

    private const int HOTKEY_REGION = 44446;

    private const int HOTKEY_REGION_COPY = 44447;

    private const int HOTKEY_REGION_OCR = 44448;

    public SystemTrayWindow()
    {
        this.InitializeComponent();
        InitializeWindow();
        SetTrayIcon();
        // 气泡的点击回调只能走 MessageWindow：TaskbarIcon 上的 TrayBalloonTipClicked 在 WinUI 下不生成公开事件
        trayIcon.TrayIcon.MessageWindow.MouseEventReceived += TrayIcon_MouseEventReceived;
        // 托盘窗口生命周期 = App 生命周期；--hide 启动时它是唯一窗口，必须由它注册热键。
        // 延迟到 DispatcherQueue 下一轮：非隐藏启动时等 MainWindow Loaded 设好 InAppToast.MainWindow，
        // 这样注册失败（被占用）时 toast 才能弹出来；MainWindow 已先注册时 IsRegistered 守卫会跳过。
        DispatcherQueue
            .GetForCurrentThread()
            .TryEnqueue(() =>
            {
                HotkeyManager.InitializeHotkey(WindowHandle);
                // --hide 自启没有主窗口，注册失败只能走托盘气泡（有主窗口时留给 MainWindow Loaded 的 toast）
                if (App.Current.MainWindow is null)
                {
                    HotkeyManager.ShowRegistrationErrors(
                        (title, message) => ShowNotification(title, message)
                    );
                }
            });
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(
            this,
            (_, _) => this.Bindings.Update()
        );
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
            switch ((int)wParam)
            {
                case HOTKEY_CAPTURE:
                    ScreenCaptureService.Capture();
                    break;
                case HOTKEY_REGION:
                    ScreenCaptureService.CaptureRegion();
                    break;
                case HOTKEY_REGION_COPY:
                    ScreenCaptureService.CaptureRegionCopyOnly();
                    break;
                case HOTKEY_REGION_OCR:
                    ScreenCaptureService.CaptureRegionOcrCopy();
                    break;
            }
        }
        return base.WindowSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
    }

    private unsafe void InitializeWindow()
    {
        new SystemBackdropHelper(
            this,
            SystemBackdropProperty.AcrylicDefault with
            {
                TintColorLight = 0xFFE7E7E7,
                TintColorDark = 0xFF404040,
            }
        ).TrySetAcrylic(true);

        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (s, e) => e.Cancel = true;
        this.Activated += SystemTrayWindow_Activated;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var flag = User32.GetWindowLongPtr(WindowHandle, User32.WindowLongFlags.GWL_STYLE);
        flag &= ~(nint)User32.WindowStyles.WS_CAPTION;
        flag &= ~(nint)User32.WindowStyles.WS_BORDER;
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE, flag);
        var p = DwmApi.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        DwmApi.DwmSetWindowAttribute(
            WindowHandle,
            DwmApi.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            (nint)(&p),
            sizeof(DwmApi.DWM_WINDOW_CORNER_PREFERENCE)
        );

        // 托盘窗口必须 Show 一次以初始化托盘图标和 XAML 树（否则图标不出现、菜单打不开）。
        // WS_EX_LAYERED + alpha=0 让窗口透明不可见地完成初始化，base.Show 绕过 override 的光标定位。
        var exStyle = User32.GetWindowLongPtr(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE);
        User32.SetWindowLong(
            WindowHandle,
            User32.WindowLongFlags.GWL_EXSTYLE,
            exStyle | (nint)User32.WindowStylesEx.WS_EX_LAYERED
        );
        User32.SetLayeredWindowAttributes(
            WindowHandle,
            0,
            0,
            User32.LayeredWindowAttributes.LWA_ALPHA
        );
        base.Show();
        Hide();
        // 摘掉 LAYERED，恢复正常渲染（否则以后每次 Show 都是透明的）
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE, exStyle);
    }

    private void SetTrayIcon()
    {
        try
        {
            trayIcon.Icon = AppIconHelper.GetAppIcon();
        }
        catch { }
    }

    /// <summary>
    /// 托盘气泡通知：后台产生、主窗口不可见时没处展示的消息走这里（更新提醒等）。
    /// onClick 在用户点击通知时执行（Win11 横幅只在屏上停留几秒，错过就收不到）。
    /// 图标与主路径的应用通知不一致：这里用 exe 内嵌的 logo.ico（Win32 HICON 接口只吃句柄），
    /// 应用通知用 Assets/update.png（只吃文件路径）。
    /// </summary>
    public void ShowNotification(string title, string message, Action? onClick = null)
    {
        try
        {
            _notificationClick = onClick;
            nint icon = AppIconHelper.LoadNotificationIcon(large: true);
            trayIcon.ShowNotification(
                title,
                message,
                customIconHandle: icon != 0 ? icon : null,
                largeIcon: true
            );
        }
        catch { }
    }

    /// <summary>
    /// 当前通知的点击动作（气泡本身不带载荷，靠这里记住点开该做什么）
    /// </summary>
    private Action? _notificationClick;

    private void TrayIcon_MouseEventReceived(
        object? sender,
        H.NotifyIcon.Core.MessageWindow.MouseEventReceivedEventArgs e
    )
    {
        if (e.MouseEvent is not H.NotifyIcon.Core.MouseEvent.BalloonToolTipClicked)
        {
            return;
        }
        var action = _notificationClick;
        _notificationClick = null;
        if (action is null)
        {
            return;
        }
        // 点击动作在 UI 线程执行，异常若逃出去会直接崩进程（气泡来源没有别的兜底）
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppConfig
                    .GetLogger<SystemTrayWindow>()
                    .LogError(ex, "Notification click action failed");
            }
        });
    }

    private void SystemTrayWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState is WindowActivationState.Deactivated)
        {
            Hide();
        }
    }

    [RelayCommand]
    public override void Show()
    {
        RootGrid.RequestedTheme = ShouldSystemUseDarkMode()
            ? ElementTheme.Dark
            : ElementTheme.Light;
        RootGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SIZE windowSize = new()
        {
            Width = (int)(RootGrid.DesiredSize.Width * UIScale),
            Height = (int)(RootGrid.DesiredSize.Height * UIScale),
        };
        User32.GetCursorPos(out POINT point);
        User32.CalculatePopupWindowPosition(
            point,
            windowSize,
            User32.TrackPopupMenuFlags.TPM_RIGHTALIGN
                | User32.TrackPopupMenuFlags.TPM_BOTTOMALIGN
                | User32.TrackPopupMenuFlags.TPM_WORKAREA,
            null,
            out RECT windowPos
        );
        User32.MoveWindow(
            WindowHandle,
            windowPos.X,
            windowPos.Y,
            windowPos.Width,
            windowPos.Height,
            true
        );
        base.Show();
    }

    [RelayCommand]
    public override void Hide()
    {
        base.Hide();
    }

    [RelayCommand]
    public void ShowMainWindow()
    {
        App.Current.EnsureMainWindow();
    }

    [RelayCommand]
    private void Exit()
    {
        App.Current.Exit();
    }

    [RelayCommand]
    private void Capture()
    {
        Hide();
        ScreenCaptureService.Capture();
    }

    [RelayCommand]
    private void CaptureRegion()
    {
        Hide();
        ScreenCaptureService.CaptureRegion();
    }

    [RelayCommand]
    private void CaptureRegionCopy()
    {
        Hide();
        ScreenCaptureService.CaptureRegionCopyOnly();
    }

    [RelayCommand]
    private void CaptureRegionOcr()
    {
        Hide();
        ScreenCaptureService.CaptureRegionOcrCopy();
    }

    private void WindowEx_Closed(object sender, WindowEventArgs args)
    {
        trayIcon?.Dispose();
    }
}
