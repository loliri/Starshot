using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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



    public SystemTrayWindow()
    {
        this.InitializeComponent();
        InitializeWindow();
        SetTrayIcon();
        // 托盘窗口生命周期 = App 生命周期；--hide 启动时它是唯一窗口，必须由它注册热键。
        // 非隐藏启动时 MainWindow 已先注册，IsRegistered 守卫会让这里跳过，不会重复注册。
        HotkeyManager.InitializeHotkey(WindowHandle);
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => this.Bindings.Update());
    }


    protected override nint WindowSubclassProc(HWND hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
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
            }
        }
        return base.WindowSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
    }




    private unsafe void InitializeWindow()
    {
        new SystemBackdropHelper(this, SystemBackdropProperty.AcrylicDefault with
        {
            TintColorLight = 0xFFE7E7E7,
            TintColorDark = 0xFF404040
        }).TrySetAcrylic(true);

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
        DwmApi.DwmSetWindowAttribute(WindowHandle, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (nint)(&p), sizeof(DwmApi.DWM_WINDOW_CORNER_PREFERENCE));

        // 托盘窗口必须 Show 一次以初始化托盘图标和 XAML 树（否则图标不出现、菜单打不开）。
        // WS_EX_LAYERED + alpha=0 让窗口透明不可见地完成初始化，base.Show 绕过 override 的光标定位。
        var exStyle = User32.GetWindowLongPtr(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE);
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (nint)User32.WindowStylesEx.WS_EX_LAYERED);
        User32.SetLayeredWindowAttributes(WindowHandle, 0, 0, User32.LayeredWindowAttributes.LWA_ALPHA);
        base.Show();
        Hide();
        // 摘掉 LAYERED，恢复正常渲染（否则以后每次 Show 都是透明的）
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE, exStyle);
    }



    private void SetTrayIcon()
    {
        try
        {
            nint hInstance = Kernel32.GetModuleHandle(null).DangerousGetHandle();
            nint hIcon = User32.LoadIcon(hInstance, "#32512").DangerousGetHandle();
            trayIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
        }
        catch { }
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
        RootGrid.RequestedTheme = ShouldSystemUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
        RootGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SIZE windowSize = new()
        {
            Width = (int)(RootGrid.DesiredSize.Width * UIScale),
            Height = (int)(RootGrid.DesiredSize.Height * UIScale)
        };
        User32.GetCursorPos(out POINT point);
        User32.CalculatePopupWindowPosition(point, windowSize, User32.TrackPopupMenuFlags.TPM_RIGHTALIGN | User32.TrackPopupMenuFlags.TPM_BOTTOMALIGN | User32.TrackPopupMenuFlags.TPM_WORKAREA, null, out RECT windowPos);
        User32.MoveWindow(WindowHandle, windowPos.X, windowPos.Y, windowPos.Width, windowPos.Height, true);
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


    private void WindowEx_Closed(object sender, WindowEventArgs args)
    {
        trayIcon?.Dispose();
    }


}
