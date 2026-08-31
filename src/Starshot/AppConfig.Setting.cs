using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Win32.TaskScheduler;

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
    /// 文件夹随机模式优先抽视频（模式 3 子选项）：有视频只从视频抽，没有回退图片。默认 false=图/视频混合。
    /// </summary>
    public static bool WallpaperFolderPreferVideo
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
    /// 开机自启：实时读注册表 HKCU\...\Run\Starshot 是否存在（用户可能在外部禁用，不能缓存到配置）
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

    private static bool? _installer;

    /// <summary>
    /// 分发线身份：打包阶段烙在主程序 exe DOS stub（偏移 0x40）里的标志，CI 只对 kachina 安装版产物写入。
    /// 启动读一次缓存；便携版 exe 无标志恒 false。true 时更新拉起 Starshot.Update.exe、隐藏便携专属 UI。
    /// </summary>
    public static bool Installer => _installer ??= ReadInstallerFlag();

    private static bool ReadInstallerFlag()
    {
        try
        {
            using var fs = File.OpenRead(Environment.ProcessPath!);
            fs.Seek(0x40, SeekOrigin.Begin);
            ReadOnlySpan<byte> magic = "STARSHOT-INSTALLER"u8;
            Span<byte> buf = stackalloc byte[magic.Length];
            return fs.ReadAtLeast(buf, magic.Length) == magic.Length && buf.SequenceEqual(magic);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查更新时包含预览版（默认只看正式版）。
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
    public static long LastUpdateCheckTime
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
    /// OCR 引擎包 CDN 地址（oneocr.dll + oneocr.onemodel 平铺 zip，供按需下载）
    /// </summary>
    public const string OcrCdnUrl = CdnBase + "/ocr/oneocr.zip";

    /// <summary>
    /// OCR 引擎选择：0=OneOCR（默认，精度高，需 exe 旁的 oneocr 文件），1=系统引擎（Windows.Media.Ocr，免下载精度低）
    /// </summary>
    public static int OcrEngine
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 启动首页：0=截图库（默认），1=剪贴板
    /// </summary>
    public static int StartPage
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 每次启动把进程提升为高优先级（应用自设，与启动方式无关）。关=跟随系统默认（Normal）。
    /// </summary>
    public static bool HighPriorityProcess
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 开发者模式
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
    public static int LogLevel
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
    public static string ScreenshotFolder
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
    /// 用户配置的截图库文件夹列表（JSON 数组），供库浏览。
    /// 兼容读旧版三种形态（原生数组 / 双重序列化文本 / 分号串），读到的旧值在下次保存时自动转为原生数组
    /// </summary>
    public static List<string> ExtraScreenshotFolders
    {
        get => GetListValue();
        set => SetListValue(value);
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

    /// <summary>
    /// 识别文字快捷键（区域选区 → OCR 文本进剪贴板，不存文件）
    /// </summary>
    public static string? RegionOcrHotkey
    {
        // Alt + O
        get => GetValue("1+79");
        set => SetValue(value);
    }

    /// <summary>
    /// HDR 截图在主 HDR 文件之外额外保存一份 Ultra HDR JPEG（SDR 基图 + gain map，
    /// 不支持 HDR 的软件也能正常显示）。主文件不受影响。
    /// </summary>
    public static bool AutoSaveUltraHDRJpeg
    {
        get => GetValue(true);
        set => SetValue(value);
    }

    /// <summary>
    /// HDR 格式但内容为 SDR（maxCLL 不达 HDR 阈值）时，转为 SDR 并删除 HDR 文件。
    /// 启用后无视 AutoSaveUltraHDRJpeg。
    /// </summary>
    public static bool DeleteHDRIfSDRContent
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 截图自动写剪贴板总开关：全屏保存后放文件（CF_HDROP），区域选区自动复制放位图（CF_DIB）。
    /// 用户主动复制（图库右键等 force 路径）不受影响。
    /// </summary>
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


    // 值放宽为 object：普通键是 string，数组键（ExtraScreenshotFolders）是 List<string> 原生存取，
    // 读文件时统一解析为 JsonElement 占位（字符串/数组都原样保留，写回时原生输出，无嵌套转义）
    private static Dictionary<string, object?>? _settingCache;

    /// <summary>
    /// 当前配置文件路径（导出/备份直接拷此文件）。
    /// </summary>
    public static string ConfigFilePath => Path.Combine(UserDataFolder, "config.sjson");

    private static void InitializeSettingProvider()
    {
        if (_settingCache is not null)
            return;
        _settingCache = [];
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    File.ReadAllText(ConfigFilePath)
                );
                if (dict is not null)
                {
                    foreach (var (k, v) in dict)
                    {
                        _settingCache[k] = v;
                    }
                }
                NormalizeLegacyListValue();
            }
        }
        catch { }
    }

    /// <summary>
    /// 旧形态数组值一次性修复（仅 Initialize 时检测）：双重序列化文本 / 分号串 → List<string>，
    /// 修好立即落盘——文件当场升级为原生数组，此后每次启动此函数直接 miss 退出，零成本
    /// </summary>
    private static void NormalizeLegacyListValue()
    {
        const string key = nameof(ExtraScreenshotFolders);
        if (
            _settingCache!.TryGetValue(key, out object? value)
            && value is JsonElement { ValueKind: JsonValueKind.String } je
        )
        {
            string? raw = je.GetString();
            List<string>? list = null;
            try
            {
                list = JsonSerializer.Deserialize<List<string>>(raw!);
            }
            catch (JsonException) { }
            list ??= raw
                ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (list is not null)
            {
                _settingCache[key] = list;
                SaveConfigFile();
            }
        }
    }

    /// <summary>
    /// 全量写回 config.sjson。temp + Move 原子替换，防写一半崩溃损坏整档。
    /// </summary>
    private static void SaveConfigFile()
    {
        string tmp = ConfigFilePath + ".tmp";
        File.WriteAllText(
            tmp,
            JsonSerializer.Serialize(
                _settingCache,
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
        File.Move(tmp, ConfigFilePath, overwrite: true);
    }

    /// <summary>
    /// 确保配置文件存在（首启流程的判定文件；全默认不改设置的用户也生成，避免重复弹欢迎页）。
    /// </summary>
    public static void EnsureConfigFile()
    {
        InitializeSettingProvider();
        if (!File.Exists(ConfigFilePath))
        {
            SaveConfigFile();
        }
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
        if (_settingCache?.TryGetValue(key, out object? value) ?? false)
        {
            string? raw = value switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
                _ => null,
            };
            try
            {
                return ConvertFromString(raw, defaultValue);
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
            if (
                _settingCache!.TryGetValue(key, out object? cacheValue)
                && AsString(cacheValue) == val
            )
            {
                return;
            }
            _settingCache[key] = val;
            SaveConfigFile();
        }
        catch { }
    }

    /// <summary>
    /// 数组键专用存取：值在文件里是原生 JSON 数组（无嵌套转义）。
    /// 旧形态（双重序列化文本 / 分号串）在 Initialize 时已归一化为 List，此主路径不做兼容判断
    /// </summary>
    public static List<string> GetListValue([CallerMemberName] string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return [];
        }
        InitializeSettingProvider();
        if (_settingCache?.TryGetValue(key, out object? value) ?? false)
        {
            return value switch
            {
                List<string> list => list,
                JsonElement { ValueKind: JsonValueKind.Array } je => je.Deserialize<List<string>>()
                    ?? [],
                _ => [],
            };
        }
        return [];
    }

    public static void SetListValue(List<string> value, [CallerMemberName] string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return;
        }
        InitializeSettingProvider();
        try
        {
            _settingCache![key] = value;
            SaveConfigFile();
        }
        catch { }
    }

    private static string? AsString(object? value)
    {
        return value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            _ => null,
        };
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

    /// <summary>
    /// 导入配置：读 JSON 文件 → 校验为合法配置字典（键非空、值为字符串或字符串数组）→ 整档替换并写盘。
    /// 返回 false = 文件不存在/不是合法 JSON/结构不符（调用方提示无效，原配置不动）。
    /// </summary>
    public static bool ImportConfigFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                File.ReadAllText(path)
            );
            if (
                dict is null
                || dict.Any(kv => string.IsNullOrWhiteSpace(kv.Key) || !IsValidValue(kv.Value))
            )
                return false;
            _settingCache = dict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            SaveConfigFile();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => true,
            JsonValueKind.Array => value
                .EnumerateArray()
                .All(e => e.ValueKind == JsonValueKind.String),
            _ => false,
        };
    }

    public static void ClearCache()
    {
        _settingCache = null;
    }

    #endregion
}
