using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Win32;
using Starshot.Features.Codec;
using Starshot.Features.Update;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;
using Windows.System;
using ExecAction = Microsoft.Win32.TaskScheduler.ExecAction;
using LogonTrigger = Microsoft.Win32.TaskScheduler.LogonTrigger;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;

namespace Starshot.Features.Setting;

public sealed partial class GeneralSetting : PageBase
{
    private readonly ILogger<GeneralSetting> _logger = AppConfig.GetLogger<GeneralSetting>();

    public string? DebugDest
    {
        get => AppConfig.GetValue<string?>();
        set
        {
            AppConfig.SetValue(value);
            OnPropertyChanged(nameof(DebugDest));
            OnPropertyChanged(nameof(DebugDestDisplay));
        }
    }

    public string DebugDestDisplay
    {
        get
        {
            string? dest = DebugDest;
            return string.IsNullOrWhiteSpace(dest) ? Lang.Starshot_PathNone : dest!;
        }
    }

    // 禁用证书校验：调试流式解压用（自签测试服务器），进程级不写 DB
    private static bool s_disableCert;

    public bool DisableCert
    {
        get => s_disableCert;
        set
        {
            s_disableCert = value;
            OnPropertyChanged(nameof(DisableCert));
        }
    }

    // 解压状态进程级：切走设置页再回来恢复（后台任务继续跑）
    private enum ExtractState
    {
        Idle,
        Running,
        Completed,
        Failed,
    }

    private static ExtractState s_extractState = ExtractState.Idle;
    private static int s_extractPercent;
    private static string s_extractStatus = "";

    // 当前活动实例：progress handler 通过它刷新当前页面（切 tab 回来新实例能收到更新）
    private static GeneralSetting? s_activeInstance;

    public GeneralSetting()
    {
        InitializeComponent();
        // 安装版线：更新源在 kachina config 写死，只藏源选择；
        // GitHub 直连开关保留——release notes 仍走 GitHub API 拉取
        if (AppConfig.Installer)
        {
            Segmented_UpdateSource.Visibility = Visibility.Collapsed;
        }
        s_activeInstance = this;
        LoadShieldIcon();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        s_activeInstance = this;
        RefreshExtractState();
    }

    private void RefreshExtractState()
    {
        DebugProgressBar.Value = s_extractPercent;
        DebugProgressBar.Visibility =
            s_extractState == ExtractState.Running ? Visibility.Visible : Visibility.Collapsed;
        DebugCompletedIcon.Visibility =
            s_extractState == ExtractState.Completed ? Visibility.Visible : Visibility.Collapsed;
        DebugStatus.Text = s_extractStatus;
    }

    public bool EnableAutoStart
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                _ = ApplyEnableAutoStartAsync(value);
        }
    } = IsAutoStartActive();

    private async Task ApplyEnableAutoStartAsync(bool value)
    {
        if (value)
        {
            if (PriorityStart)
                await UpdateAutoStartTaskAsync(true);
            else
                UpdateAutoStartRegistry(true);
        }
        else
        {
            UpdateAutoStartRegistry(false);
            if (_priorityStart)
            {
                if (await UpdateAutoStartTaskAsync(false))
                    _priorityStart = false;
            }
        }
        OnPropertyChanged(nameof(AutoStartMinimizedVisibility));
        OnPropertyChanged(nameof(PriorityStartVisibility));
        OnPropertyChanged(nameof(PriorityStart));
    }

    private static bool IsAutoStartActive()
    {
        if (AppConfig.EnableAutoStart)
            return true;
        try
        {
            using var ts = new TaskService();
            return ts.GetTask("Starshot") is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool AutoStartMinimized
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                _ = ApplyAutoStartMinimizedAsync(value);
        }
    } = AppConfig.AutoStartMinimized;

    private async Task ApplyAutoStartMinimizedAsync(bool value)
    {
        AppConfig.AutoStartMinimized = value;
        if (EnableAutoStart)
        {
            if (PriorityStart)
                await UpdateAutoStartTaskAsync(true);
            else
                UpdateAutoStartRegistry(true);
        }
    }

    public Visibility AutoStartMinimizedVisibility =>
        EnableAutoStart ? Visibility.Visible : Visibility.Collapsed;

    public Microsoft.UI.Xaml.Media.ImageSource? ShieldSource { get; set; }

    public Visibility PriorityStartVisibility =>
        EnableAutoStart ? Visibility.Visible : Visibility.Collapsed;

    public bool AutoStartEnabled => !PriorityStart;

    /// <summary>
    /// Task Scheduler 高优先级启动（ONLOGON + High），独立于注册表 Run
    /// </summary>
    private bool _priorityStart = IsTaskExists();
    public bool PriorityStart
    {
        get => _priorityStart;
        set
        {
            _priorityStart = value;
            OnPropertyChanged(nameof(AutoStartEnabled));
            OnPropertyChanged(nameof(PriorityStartHintVisibility));
            _ = ApplyPriorityStartAsync(value);
        }
    }

    private async Task ApplyPriorityStartAsync(bool value)
    {
        bool ok = await UpdateAutoStartTaskAsync(value);
        if (ok)
        {
            UpdateAutoStartRegistry(!value);
        }
        else
        {
            _priorityStart = !value;
            OnPropertyChanged(nameof(PriorityStart));
            OnPropertyChanged(nameof(AutoStartEnabled));
            OnPropertyChanged(nameof(PriorityStartHintVisibility));
        }
    }

    public Visibility PriorityStartHintVisibility =>
        _priorityStart ? Visibility.Visible : Visibility.Collapsed;

    private static bool IsTaskExists()
    {
        try
        {
            using var ts = new TaskService();
            return ts.GetTask("Starshot") is not null;
        }
        catch
        {
            return false;
        }
    }

    public int UpdateSource
    {
        get => AppConfig.UpdateSource;
        set => AppConfig.UpdateSource = value;
    }

    /// <summary>
    /// 每次启动把进程提升为高优先级（应用自设，与启动方式无关）。改后重启生效。
    /// </summary>
    public bool HighPriorityProcess
    {
        get => AppConfig.HighPriorityProcess;
        set
        {
            AppConfig.HighPriorityProcess = value;
            InAppToast.MainWindow?.Information(null, Lang.Starshot_RestartToTakeEffect, 3000);
        }
    }

    /// <summary>
    /// 全屏时静默截图通知：开=检测独占全屏，全屏中不弹浮窗；关（默认）=照弹且不检测。
    /// </summary>
    public bool MuteNotificationInFullscreen
    {
        get => AppConfig.MuteNotificationInFullscreen;
        set => AppConfig.MuteNotificationInFullscreen = value;
    }

    /// <summary>
    /// GitHub API 不走系统代理（仅 GitHub 源生效；CDN 源走系统代理不受影响）。改后重启生效。
    /// </summary>
    public bool GithubApiNoProxy
    {
        get => AppConfig.EnableGithubApiNoProxy;
        set
        {
            AppConfig.EnableGithubApiNoProxy = value;
            InAppToast.MainWindow?.Information(null, Lang.Starshot_RestartToTakeEffect, 3000);
        }
    }

    /// <summary>
    /// 开发者模式：显示调试组（流式解压测试）
    /// </summary>
    public bool DevMode
    {
        get => AppConfig.DevMode;
        set
        {
            AppConfig.DevMode = value;
            OnPropertyChanged(nameof(DevModeVisibility));
        }
    }

    public Visibility DevModeVisibility => DevMode ? Visibility.Visible : Visibility.Collapsed;

    public int LanguageIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                string? lang = value switch
                {
                    1 => "en-US",
                    2 => "zh-CN",
                    3 => "ja-JP",
                    _ => null,
                };
                AppConfig.Language = lang;
                AppConfig.SetLanguage(lang);
                Process.Start(
                    new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }
                );
                Environment.Exit(0);
            }
        }
    } =
        AppConfig.Language switch
        {
            "en-US" => 1,
            "zh-CN" => 2,
            "ja-JP" => 3,
            _ => 0,
        };

    private static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Starshot";

    private void UpdateAutoStartRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
                return;

            if (enable)
            {
                string launcherPath = GetLauncherPath();
                string args = AppConfig.AutoStartMinimized ? " --hide" : "";
                key.SetValue(RunValueName, $"\"{launcherPath}\"{args}");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-start registry");
        }
    }

    /// <summary>
    /// Task Scheduler 高优先级启动（ONLOGON 触发 + High 优先级），独立于注册表 Run。
    /// </summary>
    /// <returns>true 成功（ExitCode 0）；false 失败（子进程异常或非 0 退出）</returns>
    private async Task<bool> UpdateAutoStartTaskAsync(bool enable)
    {
        try
        {
            string launcherPath = GetLauncherPath();
            string taskArgs = AppConfig.AutoStartMinimized ? "--hide" : "";
            string mode = enable ? "create" : "delete";
            // 提权子进程：UAC 弹窗，admin 权限调 TaskScheduler API（同步）；await 不阻塞 UI
            var psi = new ProcessStartInfo(
                Environment.ProcessPath!,
                $"--manage-task {mode} \"{launcherPath}\" \"{taskArgs}\""
            )
            {
                Verb = "runas",
                UseShellExecute = true,
            };
            var p = Process.Start(psi);
            if (p is null)
                return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-start task");
            InAppToast.MainWindow?.Warning(
                null,
                new System.ComponentModel.Win32Exception(ex.NativeErrorCode).Message,
                5000
            );
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-start task");
            InAppToast.MainWindow?.Warning(null, ex.Message, 5000);
            return false;
        }
    }

    private static string GetLauncherPath()
    {
        string exePath = Environment.ProcessPath ?? "";
        // 安装版线：扁平目录，主程序就在根，自启直接指向自身
        if (AppConfig.Installer)
        {
            return exePath;
        }
        string appDir = Path.GetDirectoryName(exePath) ?? "";
        string rootDir = Path.GetDirectoryName(appDir) ?? "";
        string launcher = Path.Combine(rootDir, "Starshot.exe");
        return File.Exists(launcher) ? launcher : exePath;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private void LoadShieldIcon()
    {
        try
        {
            // IDI_SHIELD = 32518，系统标准 UAC 盾牌图标
            IntPtr hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32518);
            if (hIcon == IntPtr.Zero)
                return;
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
            ShieldSource = AppIconHelper.ToBitmapImage(icon);
        }
        catch { }
    }

    [RelayCommand]
    private async Task BrowseDest()
    {
        var folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            DebugDest = folder;
        }
    }

    [RelayCommand]
    private async Task OpenDest()
    {
        if (string.IsNullOrWhiteSpace(DebugDest) || !Directory.Exists(DebugDest))
            return;
        await Launcher.LaunchFolderPathAsync(DebugDest);
    }

    [RelayCommand]
    private async Task DebugExtract()
    {
        string? url = DebugUrlBox.Text?.Trim();
        string? dest = DebugDest?.Trim();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(dest))
        {
            s_extractState = ExtractState.Idle;
            s_extractStatus = Lang.Starshot_DebugUrlEmpty;
            RefreshExtractState();
            return;
        }
        s_extractState = ExtractState.Running;
        s_extractPercent = 0;
        s_extractStatus = "";
        RefreshExtractState();

        var progress = new Progress<(int percent, string stage)>(p =>
        {
            s_extractPercent = p.percent;
            s_extractStatus = $"{p.percent}%  {p.stage}";
            try
            {
                s_activeInstance?.RefreshExtractState();
            }
            catch { }
        });
        try
        {
            await UpdateService.ExtractToDirectoryAsync(
                url,
                dest,
                progress,
                disableCert: DisableCert
            );
            s_extractState = ExtractState.Completed;
            s_extractPercent = 100;
            s_extractStatus = Lang.Starshot_DebugExtractDone;
        }
        catch (Exception ex)
        {
            s_extractState = ExtractState.Failed;
            s_extractStatus = Lang.Starshot_DebugExtractFailed + ex.Message;
        }
        finally
        {
            try
            {
                s_activeInstance?.RefreshExtractState();
            }
            catch { }
        }
    }
}
