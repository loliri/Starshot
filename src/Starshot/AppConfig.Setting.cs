using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dapper;
using Microsoft.Win32.TaskScheduler;
using Starshot.Features.Database;

namespace Starshot;

public enum CaptureMonitorSource
{
    ForegroundWindow = 0,
    Cursor = 1,
}

public static partial class AppConfig
{
    #region Static Setting


    public static string? AccentColor
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>
    /// 主题：0=跟随系统, 1=浅色, 2=深色
    /// </summary>
    public static int Theme
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 启用亚克力效果（导航栏/内容覆盖层/弹窗的磨砂玻璃）。关则用纯色背景。
    /// </summary>
    public static bool EnableAcrylic
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// 壁纸模式：0=无，1=指定图片(复制到 bg/)，2=指定视频(读源)，3=文件夹随机(读源)
    /// </summary>
    public static int WallpaperMode
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 壁纸文件名（模式 1，拷贝在 CacheFolder/bg/ 下），空=null=无
    /// </summary>
    public static string? WallpaperFile
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>
    /// 壁纸源文件夹（模式 3，读源不复制），空=null=无
    /// </summary>
    public static string? WallpaperFolder
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>
    /// 壁纸源视频文件（模式 2，读源不复制），空=null=无
    /// </summary>
    public static string? WallpaperVideoFile
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>
    /// 文件夹随机模式仅抽视频（模式 3 子选项），默认 false=图/视频混合
    /// </summary>
    public static bool WallpaperFolderVideoOnly
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 启用自定义壁纸（开则关 Mica，铺壁纸 + 亚克力隔层）。模式 0=无 → false；1/2/3 看对应路径。
    /// </summary>
    public static bool EnableWallpaper
    {
        get =>
            WallpaperMode switch
            {
                1 => !string.IsNullOrWhiteSpace(WallpaperFile),
                2 => !string.IsNullOrWhiteSpace(WallpaperVideoFile),
                3 => !string.IsNullOrWhiteSpace(WallpaperFolder),
                _ => false,
            };
    }

    /// <summary>
    /// 从壁纸自动取色应用为强调色
    /// </summary>
    public static bool EnableAccentFromWallpaper
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// 语言代码（如 en-US, zh-CN），空=跟随系统
    /// </summary>
    public static string? Language
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>
    /// 系统托盘总是启用（关闭主窗口最小化到托盘）
    /// </summary>
    public static bool EnableSystemTrayIcon => true;

    /// <summary>
    /// 开机自启：实时读注册表 HKCU\...\Run\Starshot 是否存在（用户可能在外部禁用，不能缓存到 DB）
    /// </summary>
    public static bool EnableAutoStart
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"
                );
                return key?.GetValue("Starshot") is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 启动检测：自启项指向的 exe 不存在则清除（不判断是否本程序，单纯文件不存在就清）；为 true 时 MainWindow 打开后 toast 提示
    /// </summary>
    public static bool AutoStartInvalid;

    public static bool TaskInvalid;

    public static void CheckAutoStartValidity()
    {
        // Task 模式（高优先级启动）→ 不检查注册表（互斥，避免重叠 toast）
        try
        {
            using var ts = new TaskService();
            if (ts.GetTask("Starshot") is not null)
                return;
        }
        catch { }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run"
            );
            string? cmd = key?.GetValue("Starshot") as string;
            if (string.IsNullOrWhiteSpace(cmd))
                return;
            string exePath = cmd.Trim();
            if (exePath.StartsWith('"'))
            {
                int end = exePath.IndexOf('"', 1);
                exePath = end > 0 ? exePath[1..end] : exePath.Trim('"');
            }
            else
            {
                int space = exePath.IndexOf(' ');
                if (space > 0)
                    exePath = exePath[..space];
            }
            if (!File.Exists(exePath))
            {
                using var wkey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    writable: true
                );
                wkey?.DeleteValue("Starshot", throwOnMissingValue: false);
                AutoStartInvalid = true;
            }
        }
        catch { }
    }

    public static void CheckTaskValidity()
    {
        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask("Starshot");
            if (task is null)
                return;
            var action = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
            if (action is null || !File.Exists(action.Path))
            {
                TaskInvalid = true;
            }
        }
        catch { }
    }

    /// <summary>
    /// 开机自启时最小化到托盘
    /// </summary>
    public static bool AutoStartMinimized
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// 启动时自动检查更新（节流 24h）
    /// </summary>
    public static bool EnableAutoUpdateCheck
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// 安装版线：true 时更新拉起 Starshot.Update.exe、隐藏更新源等便携版专属 UI。
    /// 首次启动由欢迎页流程按包内更新器存在与否写入，之后一直不变（更新线与数据位置独立锚定）。
    /// </summary>
    public static bool Installer
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 检查更新时是否包含预发布版本。开 = 用 /releases 端点（含 pre-release）；关 = 用 /releases/latest（只看正式版）。
    /// </summary>
    public static bool EnablePreReleaseUpdateCheck
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// GitHub API（api.github.com）不走系统代理（直连）。仅影响 release 查询 API，不影响 zip 下载（CDN）。默认开。
    /// </summary>
    public static bool EnableGithubApiNoProxy
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// 跳过的更新版本（用户点「跳过此版本」）
    /// </summary>
    public static string? IgnoreVersion
    {
        get => GetValue<string?>();
        set => SetValue(value);
    }

    /// <summary>
    /// 上次检查更新的 Unix 时间戳（秒），用于节流
    /// </summary>
    public static long LastCheckUpdateTime
    {
        get => GetValue(0L);
        set => SetValue(value);
    }

    /// <summary>
    /// 更新源：0=Cloudflare CDN（默认，功能更多、国内访问更快），1=GitHub Release。选定后整条更新流程走该源，不跨源回退。
    /// </summary>
    public static int UpdateSource
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 官网地址（下载页等由此拼接）
    /// </summary>
    public const string WebSiteUrl = "https://starshot.cialo.site";

    /// <summary>
    /// CDN 更新源基址（UpdateSource=Cloudflare 时用）
    /// </summary>
    public const string CdnBase = "https://starshot-release.cialo.site";

    /// <summary>
    /// GitHub 仓库基址（网页版；releases / blob 等链接由此拼接）
    /// </summary>
    public const string RepoBaseUrl = "https://github.com/loliri/Starshot";

    /// <summary>
    /// GitHub API 基址（查 release 信息用）
    /// </summary>
    public const string RepoApiBaseUrl = "https://api.github.com/repos/loliri/Starshot";

    /// <summary>
    /// 每次启动把进程提升为高优先级（应用自设，与启动方式无关）。关=跟随系统默认（Normal）。
    /// </summary>
    public static bool HighPriorityProcess
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 开发者模式：显示设置页调试组（流式解压测试）。默认关。
    /// </summary>
    public static bool DevMode
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 日志/缓存文件夹，默认 %LOCALAPPDATA%\Starshot
    /// </summary>
    public static string LogFolder
    {
        get =>
            GetValue(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Starshot"
                )
            )!;
        set => SetValue(value);
    }

    /// <summary>
    /// 日志级别：0=关 / 1=Error / 2=Warn / 3=Info(默认) / 4=Debug。重启生效。
    /// </summary>
    public static int LogLevelConfig
    {
        get => GetValue(
#if DEBUG
                4
#else
                3
#endif
            );
        set => SetValue(value);
    }

    /// <summary>
    /// 截图文件夹，默认 我的图片/Starshot
    /// </summary>
    public static string? ScreenshotFolder
    {
        get =>
            GetValue(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Starshot"
                )
            );
        set => SetValue(value);
    }

    /// <summary>
    /// 用户配置的截图库文件夹列表（分号分隔），供库浏览
    /// </summary>
    public static string? ScreenshotFolders
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>
    /// 截图快捷键
    /// </summary>
    public static string? ScreenshotCaptureHotkey
    {
        // Alt + W
        get => GetValue("1+87");
        set => SetValue(value);
    }

    /// <summary>
    /// 区域截图快捷键
    /// </summary>
    public static string? RegionCaptureHotkey
    {
        // Alt + Q
        get => GetValue("1+81");
        set => SetValue(value);
    }

    /// <summary>
    /// 仅复制快捷键（区域选区 → 只进剪贴板不存文件）
    /// </summary>
    public static string? RegionCopyHotkey
    {
        // Alt + A
        get => GetValue("1+65");
        set => SetValue(value);
    }

    public static bool AutoConvertScreenshotToSDR
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// HDR 格式但内容为 SDR（maxCLL 不达 HDR 阈值）时，转为 SDR 并删除 HDR 文件。
    /// 启用后无视 AutoConvertScreenshotToSDR。
    /// </summary>
    public static bool DeleteHDRIfSDRContent
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    public static bool AutoCopyScreenshotToClipboard
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// 全屏时静默截图通知：开=截图弹浮窗前检测独占全屏（SHQueryUserNotificationState），
    /// 全屏中不弹（浮窗会把游戏最小化到桌面），平时照常弹。
    /// 关（默认）=始终弹浮窗且不做检测，行为与引入本开关前完全一致。
    /// </summary>
    public static bool MuteNotificationInFullscreen
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 截图链路色彩管理（HDR 模式始终启用）
    /// </summary>
    public static bool EnableScreenshotColorManagement
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// SDR 截图格式：0: PNG, 1: AVIF, 2: JPEG XL
    /// </summary>
    public static int ScreenCaptureSDRFormat
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// HDR 截图格式：0: AVIF, 1: JPEG XL
    /// </summary>
    public static int ScreenCaptureHDRFormat
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 0: Middle, 1: High, 2: Lossless
    /// </summary>
    public static int ScreenCaptureEncodeQuality
    {
        get => GetValue(2);
        set => SetValue(value);
    }

    /// <summary>
    /// 截图文件名模板。占位符：{process} {processPath} {title} {timestamp} {time} {date} {width} {height} {year} {month} {day} {hour} {minute} {second}
    /// </summary>
    public static string ScreenshotFileNamePattern
    {
        get => GetValue("{title}_{process}_{width}x{height}_{timestamp}");
        set => SetValue(value);
    }

    /// <summary>
    /// 区域截图文件名模板（独立于全屏截图）
    /// </summary>
    public static string RegionScreenshotFileNamePattern
    {
        get => GetValue("{title}_region_{width}x{height}_{timestamp}");
        set => SetValue(value);
    }

    /// <summary>
    /// 文件名模板中 {title} 的最大字符数（截断），0 表示不截断
    /// </summary>
    public static int ScreenshotFileNameTitleMaxLength
    {
        get => GetValue(50);
        set => SetValue(value);
    }

    /// <summary>
    /// 截图目标显示器来源：0=前台窗口所在显示器，1=鼠标所在显示器
    /// </summary>
    public static int ScreenshotCaptureMonitorSource
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    #endregion


    #region Setting Method


    private static Dictionary<string, string?>? _settingCache;

    private static string ConfigFilePath => Path.Combine(UserDataFolder, "config.sjson");

    private static void InitializeSettingProvider()
    {
        if (_settingCache is not null)
            return;
        _settingCache = [];
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                _settingCache =
                    JsonSerializer.Deserialize<Dictionary<string, string?>>(
                        File.ReadAllText(ConfigFilePath)
                    ) ?? [];
            }
        }
        catch { }
    }

    /// <summary>
    /// 全量写回 config.sjson。temp + Move 原子替换，防写一半崩溃损坏整档。
    /// </summary>
    private static void SaveConfigFile()
    {
        string tmp = ConfigFilePath + ".tmp";
        File.WriteAllText(
            tmp,
            JsonSerializer.Serialize(_settingCache, new JsonSerializerOptions { WriteIndented = true })
        );
        File.Move(tmp, ConfigFilePath, overwrite: true);
    }

    public static T? GetValue<T>(T? defaultValue = default, [CallerMemberName] string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return defaultValue;
        }
        if (string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return defaultValue;
        }
        InitializeSettingProvider();
        if (_settingCache?.TryGetValue(key, out string? value) ?? false)
        {
            try
            {
                return ConvertFromString(value, defaultValue);
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    private static T? ConvertFromString<T>(string? value, T? defaultValue = default)
    {
        if (value is null)
        {
            return defaultValue;
        }
        var converter = TypeDescriptor.GetConverter(typeof(T));
        if (converter == null)
        {
            return defaultValue;
        }
        return (T?)converter.ConvertFromString(value);
    }

    public static void SetValue<T>(T? value, [CallerMemberName] string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return;
        }
        InitializeSettingProvider();
        try
        {
            string? val = value?.ToString();
            if (_settingCache!.TryGetValue(key, out string? cacheValue) && cacheValue == val)
            {
                return;
            }
            _settingCache[key] = val;
            SaveConfigFile();
        }
        catch { }
    }

    public static void DeleteAllSettings()
    {
        try
        {
            _settingCache = [];
            SaveConfigFile();
        }
        catch { }
    }

    public static void ClearCache()
    {
        _settingCache = null;
    }

    #endregion
}
