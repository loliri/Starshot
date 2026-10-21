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
using Starshot.Features.Telemetry;
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
        RefreshTelemetryState();
    }

    /// <summary>
    /// 调试页的统计状态快照（安装 ID / 上次发送 / 待上报更新 / 是否虚拟机）。只读展示，不参与任何逻辑。
    /// </summary>
    private void RefreshTelemetryState()
    {
        var (installId, lastSentDay, pendingUpdate, isVm) = TelemetryService.GetDebugInfo();
        TelemetryInstallIdText.Text = string.IsNullOrEmpty(installId)
            ? Lang.Starshot_DebugTelemetryNone
            : installId;
        TelemetryLastSentText.Text = string.IsNullOrEmpty(lastSentDay)
            ? Lang.Starshot_DebugTelemetryNone
            : lastSentDay;
        TelemetryPendingText.Text = string.IsNullOrEmpty(pendingUpdate)
            ? Lang.Starshot_DebugTelemetryNone
            : pendingUpdate;
        TelemetryVmText.Text = isVm
            ? Lang.Starshot_DebugTelemetryYes
            : Lang.Starshot_DebugTelemetryNo;
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
        NotifyAutoStartStatusChanged();
    }

    private static bool IsAutoStartActive()
    {
        if (AppConfig.EnableAutoStart)
            return true;
        try
        {
            using var ts = new TaskService();
            return ts.GetTask(AppConfig.AutoStartName) is not null;
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
        NotifyAutoStartStatusChanged();
    }

    public Visibility AutoStartMinimizedVisibility =>
        EnableAutoStart ? Visibility.Visible : Visibility.Collapsed;

    public Microsoft.UI.Xaml.Media.ImageSource? ShieldSource { get; set; }

    public Visibility PriorityStartVisibility =>
        EnableAutoStart ? Visibility.Visible : Visibility.Collapsed;

    public bool AutoStartEnabled => !PriorityStart;

    /// <summary>
    /// Task Scheduler 启动（ONLOGON 登录触发），独立于注册表 Run。
    /// 名称里的「高优先级」指登录触发的启动时机早于注册表 Run 排队，任务本身不设 Priority。
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
        NotifyAutoStartStatusChanged();
    }

    public Visibility PriorityStartHintVisibility =>
        _priorityStart ? Visibility.Visible : Visibility.Collapsed;

    private static bool IsTaskExists()
    {
        try
        {
            using var ts = new TaskService();
            return ts.GetTask(AppConfig.AutoStartName) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 计划任务的真实状态：null = 任务不存在，true = 存在且启用，false = 存在但被禁用。
    /// </summary>
    private static bool? GetTaskRealState()
    {
        try
        {
            using var ts = new TaskService();
            return ts.GetTask(AppConfig.AutoStartName)?.Enabled;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 注册表 Run 项的真实状态：null = Run 值不存在，true = 存在且启用，false = 存在但被禁用。
    /// 任务管理器的「禁用」不改 Run 项，只在 StartupApproved\Run 下写一条 12 字节记录：
    /// 首字节 0x03 / 0x07 = 禁用，0x02 / 0x06 = 启用；没有记录表示从未改过，按启用处理。
    /// </summary>
    private static bool? GetRegistryAutoStartRealState()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (run?.GetValue(AppConfig.AutoStartName) is null)
                return null;
            using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath);
            if (approved?.GetValue(AppConfig.AutoStartName) is not byte[] data || data.Length == 0)
                return true;
            byte flag = data[0];
            return flag != 0x03 && flag != 0x07;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 开机自启的真实状态：走计划任务则查任务，否则查注册表 Run 项。
    /// null = 未定义（本应用未创建任何自启项），true = 启用，false = 已被禁用。
    /// </summary>
    private bool? AutoStartRealState =>
        PriorityStart ? GetTaskRealState() : GetRegistryAutoStartRealState();

    /// <summary>开机自启当前状态文案（启用 / 禁用 / 未定义）</summary>
    public string AutoStartStatusText =>
        AutoStartRealState switch
        {
            true => Lang.Starshot_AutoStartStatusEnabled,
            false => Lang.Starshot_AutoStartStatusDisabled,
            null => Lang.Starshot_AutoStartStatusUndefined,
        };

    /// <summary>
    /// 开机自启当前状态配色：启用 = 成功色，禁用 = 危险色，未定义 = 普通文字色。
    /// 取自系统主题资源，不硬编码颜色值。
    /// </summary>
    public Microsoft.UI.Xaml.Media.Brush AutoStartStatusBrush =>
        AutoStartRealState switch
        {
            true => (Microsoft.UI.Xaml.Media.Brush)
                App.Current.Resources["SystemFillColorSuccessBrush"],
            false => (Microsoft.UI.Xaml.Media.Brush)
                App.Current.Resources["SystemFillColorCriticalBrush"],
            null => (Microsoft.UI.Xaml.Media.Brush)
                App.Current.Resources["TextFillColorPrimaryBrush"],
        };

    /// <summary>三个自启开关任一变动后刷新状态文案与配色</summary>
    private void NotifyAutoStartStatusChanged()
    {
        OnPropertyChanged(nameof(AutoStartStatusText));
        OnPropertyChanged(nameof(AutoStartStatusBrush));
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
    /// 开发者模式
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

    /// <summary>启动首页：0=截图库，1=剪贴板；下次启动生效</summary>
    public int StartPage
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.StartPage = value;
            }
        }
    } = AppConfig.StartPage;

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

    /// <summary>
    /// 任务管理器「启动」页的启用 / 禁用记录。与 Run 项分离——禁用只写这里，Run 项本身不动。
    /// </summary>
    private static readonly string StartupApprovedRunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

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
                key.SetValue(AppConfig.AutoStartName, $"\"{launcherPath}\"{args}");
            }
            else
            {
                key.DeleteValue(AppConfig.AutoStartName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-start registry");
        }
    }

    /// <summary>
    /// 建 / 删计划任务的实际动作：拉起自身提权子进程执行 <c>--manage-task create|delete</c>，
    /// 由子进程（Program.cs）调 TaskScheduler API 完成注册 / 删除。
    /// </summary>
    /// <param name="enable">true 建任务，false 删任务。</param>
    /// <returns>true 成功（子进程 ExitCode 0）；false 失败（用户取消 UAC、子进程启动失败或非 0 退出）</returns>
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
