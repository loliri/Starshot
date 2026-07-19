using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using Starshot.Features.ViewHost;
using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;

namespace Starshot;

public partial class App : Application
{

    private readonly DispatcherQueue _uiDispatcherQueue;

    public static new App Current => (App)Application.Current;


    public App()
    {
        this.InitializeComponent();
        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += App_UnhandledException;
    }


    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        string logFile = AppConfig.LogFile;
        if (string.IsNullOrWhiteSpace(logFile))
        {
            string logFolder = Path.Combine(AppContext.BaseDirectory, "log");
            Directory.CreateDirectory(logFolder);
            logFile = Path.Combine(logFolder, $"Starshot_{DateTime.Now:yyMMdd}.log");
        }
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] App Crash:");
        sb.AppendLine(e.Exception.ToString());
        if (e.Exception.Data.Count > 0)
        {
            foreach (DictionaryEntry item in e.Exception.Data)
            {
                sb.AppendLine($"{item.Key}: {item.Value}");
            }
        }
        using var fs = File.Open(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var sw = new StreamWriter(fs);
        sw.Write(sb);
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

        bool startHidden = Environment.GetCommandLineArgs().Contains("--hide", StringComparer.OrdinalIgnoreCase)
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
