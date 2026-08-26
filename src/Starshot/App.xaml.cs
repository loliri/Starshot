using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using Starshot.Features.ViewHost;
using Windows.UI;

namespace Starshot;

public partial class App : Application
{
    private readonly DispatcherQueue _uiDispatcherQueue;

    private readonly Timer _gcTimer = new(TimeSpan.FromSeconds(60));

    public static new App Current => (App)Application.Current;

    public App()
    {
        this.InitializeComponent();
        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += App_UnhandledException;
        // 后台定时 GC：截图（尤其区域截图覆盖层）残留的 RCW/未引用资源靠 GC 回收，
        // GC 看托管堆不看显存，不主动 Collect 会累积占显存。每 60s 回收一次（参考 Starward，且补上它漏掉的 Start）。
        _gcTimer.Elapsed += (_, _) => GC.Collect();
        _gcTimer.Start();
    }

    private void App_UnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e
    )
    {
        Program.WriteCrashLog("App Crash", e.Exception);
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs _)
    {
        await AppConfig.CheckEnviromentAsync();

        instance = AppInstance.GetCurrent();
        instance.Activated += AppInstance_Activated;

        var main = AppInstance.FindOrRegisterForKey("main");
        if (!main.IsCurrent)
        {
            await main.RedirectActivationToAsync(instance.GetActivatedEventArgs());
            Environment.Exit(0);
        }

        // 主实例：检测自启项指向的 exe 是否存在，不存在则清除
        AppConfig.CheckAutoStartValidity();
        AppConfig.CheckTaskValidity();

        bool startHidden =
            Environment.GetCommandLineArgs().Contains("--hide", StringComparer.OrdinalIgnoreCase)
            && AppConfig.EnableSystemTrayIcon;

        if (!startHidden)
        {
            m_MainWindow = new MainWindow();
            m_MainWindow.Activate();
        }
        EnsureSystemTray();
    }

    private AppInstance instance;

    private MainWindow m_MainWindow;

    /// <summary>
    /// 主窗口引用（供设置页等调用 ApplyTheme）
    /// </summary>
    public MainWindow? MainWindow => m_MainWindow;

    private SystemTrayWindow? m_SystemTrayWindow;

    public void EnsureSystemTray()
    {
        if (AppConfig.EnableSystemTrayIcon && m_SystemTrayWindow is null)
        {
            m_SystemTrayWindow = new SystemTrayWindow();
        }
    }

    public void EnsureMainWindow()
    {
        m_MainWindow ??= new MainWindow();
        m_MainWindow.Activate();
        m_MainWindow.Show();
    }

    private void AppInstance_Activated(object? sender, AppActivationArguments e)
    {
        _uiDispatcherQueue.TryEnqueue(EnsureMainWindow);
    }

    public new void Exit()
    {
        if (m_MainWindow is not null)
        {
            m_MainWindow.ForceExit = true;
        }
        m_SystemTrayWindow?.Close();
        m_MainWindow?.Close();
        Application.Current.Exit();
    }
}
