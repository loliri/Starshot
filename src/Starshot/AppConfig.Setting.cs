using Dapper;
using Starshot.Features.Database;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

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
    /// 启用自定义壁纸（开则关 Mica，铺壁纸 + 亚克力隔层）
    /// </summary>
    public static bool EnableWallpaper
    {
        get => !string.IsNullOrWhiteSpace(WallpaperFile);
    }


    /// <summary>
    /// 壁纸文件名（拷贝在 CacheFolder/bg/ 下），空=null=无壁纸
    /// </summary>
    public static string? WallpaperFile
    {
        get => GetValue<string>();
        set => SetValue(value);
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
    /// 启用系统托盘图标（关闭主窗口时最小化到托盘）
    /// </summary>
    public static bool EnableSystemTrayIcon
    {
        get => GetValue(true);
        set => SetValue(value);
    }


    /// <summary>
    /// 开机自启
    /// </summary>
    public static bool EnableAutoStart
    {
        get => GetValue(false);
        set => SetValue(value);
    }


    /// <summary>
    /// 开机自启时最小化到托盘（需托盘已开）
    /// </summary>
    public static bool AutoStartMinimized
    {
        get => GetValue(true);
        set => SetValue(value);
    }


    /// <summary>
    /// 日志/缓存文件夹，默认 %LOCALAPPDATA%\Starshot
    /// </summary>
    public static string LogFolder
    {
        get => GetValue(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starshot"))!;
        set => SetValue(value);
    }


    /// <summary>
    /// 截图文件夹，默认 我的图片/Starshot
    /// </summary>
    public static string? ScreenshotFolder
    {
        get => GetValue(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Starshot"));
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
    /// 截图链路色彩管理（HDR 模式始终启用）
    /// </summary>
    public static bool EnableScreenshotColorManagement
    {
        get => GetValue(true);
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
        get => GetValue("{process}_{time}");
        set => SetValue(value);
    }


    /// <summary>
    /// 区域截图文件名模板（独立于全屏截图）
    /// </summary>
    public static string RegionScreenshotFileNamePattern
    {
        get => GetValue("{process}_region_{time}");
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


    private static Dictionary<string, string?> _settingCache;


    private static void InitializeSettingProvider()
    {
        try
        {
            if (_settingCache is null)
            {
                using var dapper = DatabaseService.CreateConnection();
                _settingCache = dapper.Query<(string Key, string? Value)>("SELECT Key, Value FROM Setting;").ToDictionary(x => x.Key, x => x.Value);
            }
        }
        catch { }
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
        if (_settingCache is null)
        {
            return defaultValue;
        }
        try
        {
            if (_settingCache.TryGetValue(key, out string? value))
            {
                return ConvertFromString(value, defaultValue);
            }
            using var dapper = DatabaseService.CreateConnection();
            value = dapper.QueryFirstOrDefault<string>("SELECT Value FROM Setting WHERE Key=@key LIMIT 1;", new { key });
            _settingCache[key] = value;
            return ConvertFromString(value, defaultValue);
        }
        catch
        {
            return defaultValue;
        }
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
        if (_settingCache is null)
        {
            return;
        }
        try
        {
            string? val = value?.ToString();
            if (_settingCache.TryGetValue(key, out string? cacheValue) && cacheValue == val)
            {
                return;
            }
            _settingCache[key] = val;
            using var dapper = DatabaseService.CreateConnection();
            dapper.Execute("INSERT OR REPLACE INTO Setting (Key, Value) VALUES (@key, @val);", new { key, val });
        }
        catch { }
    }


    public static void DeleteAllSettings()
    {
        try
        {
            using var dapper = DatabaseService.CreateConnection();
            dapper.Execute("DELETE FROM Setting WHERE TRUE;");
        }
        catch { }
    }


    public static void ClearCache()
    {
        _settingCache.Clear();
    }


    #endregion


}
