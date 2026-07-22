using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Starshot.Features.Codec;
using Starshot.Features.Update;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.System;

namespace Starshot.Features.Setting;

public sealed partial class GeneralSetting : PageBase
{

    private readonly ILogger<GeneralSetting> _logger = AppConfig.GetLogger<GeneralSetting>();

    // 调试目标路径进程级保存：切走设置页再回来不丢
    private static string? s_debugDest;

    public string? DebugDest
    {
        get => s_debugDest;
        set
        {
            s_debugDest = value;
            OnPropertyChanged(nameof(DebugDest));
            OnPropertyChanged(nameof(DebugDestDisplay));
        }
    }

    public string DebugDestDisplay => string.IsNullOrWhiteSpace(DebugDest) ? "（未选择）" : DebugDest!;


    // 解压状态进程级：切走设置页再回来恢复（后台任务继续跑）
    private enum ExtractState { Idle, Running, Completed, Failed }

    private static ExtractState s_extractState = ExtractState.Idle;
    private static int s_extractPercent;
    private static string s_extractStatus = "";


    public GeneralSetting()
    {
        InitializeComponent();
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshExtractState();
    }


    private void RefreshExtractState()
    {
        DebugProgressBar.Value = s_extractPercent;
        DebugProgressBar.Visibility = s_extractState == ExtractState.Running ? Visibility.Visible : Visibility.Collapsed;
        DebugCompletedIcon.Visibility = s_extractState == ExtractState.Completed ? Visibility.Visible : Visibility.Collapsed;
        DebugStatus.Text = s_extractStatus;
    }


    public bool EnableSystemTrayIcon
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableSystemTrayIcon = value;
                if (value)
                {
                    App.Current.EnsureSystemTray();
                }
            }
        }
    } = AppConfig.EnableSystemTrayIcon;


    public bool EnableAutoStart
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateAutoStartRegistry(value);
                OnPropertyChanged(nameof(AutoStartMinimizedVisibility));
            }
        }
    } = AppConfig.EnableAutoStart;


    public bool AutoStartMinimized
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.AutoStartMinimized = value;
                if (AppConfig.EnableAutoStart) UpdateAutoStartRegistry(true);
            }
        }
    } = AppConfig.AutoStartMinimized;


    public Visibility AutoStartMinimizedVisibility => EnableAutoStart ? Visibility.Visible : Visibility.Collapsed;


    public int LanguageIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                string? lang = value switch { 1 => "en-US", 2 => "zh-CN", 3 => "ja-JP", _ => null };
                AppConfig.Language = lang;
                AppConfig.SetLanguage(lang);
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
                Environment.Exit(0);
            }
        }
    } = AppConfig.Language switch { "en-US" => 1, "zh-CN" => 2, "ja-JP" => 3, _ => 0 };


    private static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Starshot";


    private void UpdateAutoStartRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enable)
            {
                string launcherPath = GetLauncherPath();
                string args = (AppConfig.AutoStartMinimized && AppConfig.EnableSystemTrayIcon) ? " --hide" : "";
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


    private static string GetLauncherPath()
    {
        string exePath = Environment.ProcessPath ?? "";
        string appDir = Path.GetDirectoryName(exePath) ?? "";
        string rootDir = Path.GetDirectoryName(appDir) ?? "";
        string launcher = Path.Combine(rootDir, "Starshot.exe");
        return File.Exists(launcher) ? launcher : exePath;
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
        if (string.IsNullOrWhiteSpace(DebugDest) || !Directory.Exists(DebugDest)) return;
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
            s_extractStatus = "URL 和路径都不能为空";
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
            try { RefreshExtractState(); } catch { }
        });
        try
        {
            await UpdateService.ExtractToDirectoryAsync(url, dest, progress);
            s_extractState = ExtractState.Completed;
            s_extractPercent = 100;
            s_extractStatus = "完成！";
        }
        catch (Exception ex)
        {
            s_extractState = ExtractState.Failed;
            s_extractStatus = "失败：" + ex.Message;
        }
        finally
        {
            try { RefreshExtractState(); } catch { }
        }
    }


}
