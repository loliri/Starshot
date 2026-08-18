global using Starshot.Language;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.TaskScheduler;
using Serilog;

namespace Starshot;

#if DISABLE_XAML_GENERATED_MAIN

/// <summary>
/// Program class
/// </summary>
public static class Program
{

    // 未打包应用没注册 AppUserModelID，任务管理器用隐式 AUMID 解析应用图标常失败 → 空白图标。
    // 显式设置后按此 ID 分组解析（配合 MainWindow 的 AppWindow.SetIcon）
    [DllImport("shell32.dll")]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);


    // Windows 11 效率模式（EcoQoS）会把后台/窗口隐藏的进程自动压低执行频率——托盘常驻的 Starshot 是施加对象。
    // 该声明把 EXECUTION_SPEED 节流显式置为关闭（始终 High Performance），系统不再自动降级；
    // 用户在任务管理器手动设效率模式仍可覆盖（用户意图，无 API 可禁也不该禁）
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }


    [DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(nint hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, int size);


    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2411")]
    [global::System.STAThreadAttribute]
    static int Main(string[] args)
    {
        SetCurrentProcessExplicitAppUserModelID("loliri.Starshot");
        try
        {
            var throttling = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,   // PROCESS_POWER_THROTTLING_CURRENT_VERSION
                ControlMask = 0x1,  // PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                StateMask = 0,      // 0 = 不节流
            };
            SetProcessInformation((nint)(-1), 2 /* ProcessPowerThrottling */, ref throttling, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch { }
        // 提权子进程：--manage-task create/delete，以管理员权限调 TaskScheduler API 创建/删除任务后退出
        if (args.Length > 0 && args[0] == "--manage-task")
        {
            try
            {
                using var ts = new TaskService();
                if (args.Length > 1 && args[1] == "create")
                {
                    string launcherPath = args.Length > 2 ? args[2] : "";
                    string taskArgs = args.Length > 3 ? args[3] : "";
                    var td = ts.NewTask();
                    td.Triggers.Add(new LogonTrigger());
                    td.Actions.Add(new ExecAction(launcherPath, taskArgs));
                    // 启动时机的优先权（Task Scheduler 替代注册表 Run 抢先启动）+ 进程优先级一并给足：
                    // launcher CreateProcess 继承任务优先级，app 不再自钉 Normal，链路全程 High
                    td.Settings.Priority = ProcessPriorityClass.High;
                    td.Settings.DisallowStartIfOnBatteries = false;
                    td.Settings.StopIfGoingOnBatteries = false;
                    try { ts.RootFolder.DeleteTask("Starshot", false); } catch { }
                    ts.RootFolder.RegisterTaskDefinition("Starshot", td,
                        TaskCreation.CreateOrUpdate,
                        $"{Environment.UserDomainName}\\{Environment.UserName}",
                        null,
                        TaskLogonType.InteractiveToken);
                    LogManageTask($"Task created: {launcherPath} {taskArgs}");
                }
                else
                {
                    ts.RootFolder.DeleteTask("Starshot", false);
                    LogManageTask("Task deleted");
                }
            }
            catch (Exception ex)
            {
                LogManageTask($"Task operation failed: {ex}");
                return 1;
            }
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }


    private static void LogManageTask(string message)
    {
        try
        {
            string logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starshot", "log", "TaskScheduler.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] [manage-task] {message}\n");
        }
        catch { }
    }


    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("Program Crash", e.ExceptionObject as Exception);
    }


    /// <summary>
    /// 崩溃直写文件，不依赖任何初始化：Serilog 要到首个页面构造才配置（Log.Logger 唯一赋值点在 BuildServiceProvider），
    /// 早期崩溃走 Log.Fatal 会落进 SilentLogger 零输出。LogFile 已设则并入同一天的应用日志，否则落到程序目录 log/。
    /// </summary>
    internal static void WriteCrashLog(string kind, Exception? ex)
    {
        try
        {
            string file = string.IsNullOrWhiteSpace(AppConfig.LogFile)
                ? Path.Combine(AppContext.BaseDirectory, "log", AppConfig.BuildLogFileName())
                : AppConfig.LogFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{kind}]\r\n{ex}\r\n\r\n");
        }
        catch { }
    }
}

#endif
