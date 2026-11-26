using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    /// 壁纸不透明度（0–100，100=原样不透明）。只改壁纸图层自身的 Opacity，不重载壁纸；
    /// 0 时与无壁纸时的窗口底色一致，上层亚克力隔层与 UI 不受影响。
    /// </summary>
    public static int WallpaperOpacity
    {
        get => GetValue(100);
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
    /// 开机自启标识名，注册表 Run 值名与计划任务名共用（二者同构）：
    /// 注册表 <c>HKCU\...\Run\{AutoStartName}</c>，计划任务 <c>\{AutoStartName}</c>。
    /// 改名后旧版注册的 Run 值与计划任务都会失联，需在设置页重开关一次自启 /「高优先级启动」重建。
    /// </summary>
    public const string AutoStartName = "Starshot.Autorun";

    /// <summary>
    /// 开机自启：实时读注册表 HKCU\...\Run\{AutoStartName} 是否存在（用户可能在外部禁用，不能缓存到配置）
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
                return key?.GetValue(AutoStartName) is not null;
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
            if (ts.GetTask(AutoStartName) is not null)
                return;
        }
        catch { }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run"
            );
            string? cmd = key?.GetValue(AutoStartName) as string;
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
                wkey?.DeleteValue(AutoStartName, throwOnMissingValue: false);
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
            var task = ts.GetTask(AutoStartName);
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
    /// 更新源：0=Cloudflare CDN（默认，功能更多、国内访问更快），1=GitHub Release。选定后整条更新流程走该源，不跨源回退。
    /// </summary>
    public static int UpdateSource
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 匿名统计安装 ID（32 位小写十六进制）：首次发送时随机生成，仅用于区分安装数，不含任何设备信息。
    /// </summary>
    public static string? TelemetryInstallId
    {
        get => GetValue<string?>();
        set => SetValue(value);
    }

    /// <summary>
    /// 上次成功发送心跳的 UTC 日期（yyyy-MM-dd），用于同一 UTC 日只发一次。
    /// </summary>
    public static string? TelemetryLastSentDay
    {
        get => GetValue<string?>();
        set => SetValue(value);
    }

    /// <summary>
    /// 上次启动时记录的版本号：与当前版本不同即发生过更新，启动日志与心跳据此判定。
    /// </summary>
    public static string? LastAppVersion
    {
        get => GetValue<string?>();
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
    /// 匿名统计接收端基址（Cloudflare Worker + D1）
    /// </summary>
    public const string TelemetryBase = "https://starshot-stats.cialo.site";

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
    /// 主窗口位置与大小：“x,y,width,height,maximized”，单位为物理像素，空=首次启动用系统默认。
    /// 由 <see cref="Features.ViewHost.MainWindow"/> 在移动/缩放/隐藏/关闭时写入，
    /// 恢复时夹到最近显示器的工作区内，避免拔掉副屏后窗口留在屏幕外。
    /// </summary>
    public static string? MainWindowPlacement
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>
    /// 图片查看窗口位置与大小：“x,y,width,height,maximized”，单位为物理像素，空=首次打开用默认尺寸（1200×676，居中）。
    /// 所有查看器实例共用一份，由 <see cref="Features.Screenshot.ImageViewWindow"/> 在移动/缩放/关闭时写入；
    /// 恢复时夹到最近显示器的工作区内，不再默认最大化。
    /// </summary>
    public static string? ImageViewWindowPlacement
    {
        get => GetValue<string>();
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
        set
        {
            var history = LogFolderHistory;
            if (PushFolderHistory(history, LogFolder, value))
            {
                LogFolderHistory = history;
            }
            SetValue(value);
        }
    }

    /// <summary>
    /// 日志目录的曾用路径历史（旧值在改目录时进入，最多 5 条，新的在顶）
    /// </summary>
    public static List<string> LogFolderHistory
    {
        get => GetListValue();
        set => SetListValue(value);
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
        set
        {
            var history = ScreenshotFolderHistory;
            if (PushFolderHistory(history, ScreenshotFolder, value))
            {
                ScreenshotFolderHistory = history;
            }
            SetValue(value);
        }
    }

    /// <summary>
    /// 按文件夹分类截图：开则保存路径 = 截图目录下「模板展开的子文件夹名」内
    /// </summary>
    public static bool ScreenshotSubfolderEnabled
    {
        get => GetValue(false);
        set => SetValue(value);
    }

    /// <summary>
    /// 截图子文件夹名模板（文件名模板同款占位符语法，如 {process}）
    /// </summary>
    public static string ScreenshotSubfolderPattern
    {
        get => GetValue("{process}")!;
        set => SetValue(value);
    }

    /// <summary>
    /// 截图目录的曾用路径历史（旧值在改目录时进入，最多 5 条，新的在顶）
    /// </summary>
    public static List<string> ScreenshotFolderHistory
    {
        get => GetListValue();
        set => SetListValue(value);
    }

    /// <summary>
    /// 目录改走时把被替换的旧路径压进历史（就地改传入列表）：与当前值相同不记；
    /// 已在列先移除再置顶；容量 5。返回是否有变化（有则调用方需写回落盘）。
    /// </summary>
    private static bool PushFolderHistory(List<string> history, string? old, string? newValue)
    {
        try
        {
            if (
                string.IsNullOrWhiteSpace(old)
                || string.Equals(old, newValue, StringComparison.OrdinalIgnoreCase)
            )
                return false;
            history.RemoveAll(x => string.Equals(x, old, StringComparison.OrdinalIgnoreCase));
            history.Insert(0, old);
            if (history.Count > 5)
            {
                history.RemoveRange(5, history.Count - 5);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 用户配置的截图库文件夹列表，供库浏览。
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
    /// 截图信息浮窗总开关：开（默认）=正常显示；关=立即隐藏已有浮窗，后续截图不再弹出。
    /// </summary>
    public static bool ShowScreenshotInfoWindow
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
    /// SDR 白基准覆写（nit）：>0 = 全应用色调映射目标白用此固定值；
    /// 0（默认）= 跟随环境 HDR 显示器（SdrWhiteLevelInNits，无 HDR 屏回退 250）。
    /// </summary>
    public static int SdrWhiteLevelOverride
    {
        get => GetValue(0);
        set => SetValue(value);
    }

    /// <summary>
    /// 信息浮窗完成态停留时长（毫秒），默认 3000
    /// </summary>
    public static int InfoWindowDuration
    {
        get => GetValue(3000);
        set => SetValue(value);
    }

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE. 默认开启。
    /// </summary>
    public static bool HideInfoWindowFromCapture
    {
        get => GetValue(true);
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


    // 内存表示统一为 string / int / bool / List<string>；落盘形态由存储后端决定
    // （Debug 写 config.sjson，Release 写注册表，见 AppConfig.Store.cs）
    private static Dictionary<string, object?>? _settingCache;

    /// <summary>
    /// 设置是否已初始化：Debug 看配置文件是否存在，Release 看注册表里有没有 ConfigVersion 标记。
    /// 为 false 即首次启动或旧版升级：CheckEnviromentAsync 先尝试迁移旧配置，迁移不了才弹欢迎页，
    /// 走完后 MarkSettingsInitialized。
    /// </summary>
    public static bool SettingsInitialized => StoreIsInitialized;

    /// <summary>
    /// 落初始化标记（全默认不改设置的用户也落，避免重复弹欢迎页）
    /// </summary>
    public static void MarkSettingsInitialized()
    {
        InitializeSettingProvider();
        StoreMarkInitialized();
    }

    private static void InitializeSettingProvider()
    {
        if (_settingCache is not null)
        {
            return;
        }
        try
        {
            _settingCache = StoreLoadAll();
        }
        catch
        {
            _settingCache = [];
        }
    }

    public static T? GetValue<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T? defaultValue = default,
        [CallerMemberName] string? key = null
    )
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return defaultValue;
        }
        InitializeSettingProvider();
        return _settingCache!.TryGetValue(key, out object? value)
            ? ConvertValue(value, defaultValue)
            : defaultValue;
    }

    /// <summary>
    /// 内存值 → 属性类型：类型已一致直接用；字符串走类型转换器；其余（如手改注册表造成的类型错位）尽力转换。
    /// 任何转换失败都回默认值。
    /// </summary>
    private static T? ConvertValue<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T
    >(object? value, T? defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }
        if (value is T typed)
        {
            return typed;
        }
        try
        {
            return value is string text
                ? ConvertFromString(text, defaultValue)
                : (T?)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static T? ConvertFromString<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T
    >(string? value, T? defaultValue = default)
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
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return;
        }
        InitializeSettingProvider();
        try
        {
            // 只认 string / int / bool，其余类型按字符串存（与旧实现一致）
            object? stored = value is string or int or bool ? (object?)value : value?.ToString();
            if (_settingCache!.TryGetValue(key, out object? cached) && Equals(cached, stored))
            {
                return;
            }
            WriteSetting(key, stored);
        }
        catch { }
    }

    /// <summary>
    /// 写入（或 value 为 null 时删除）一个设置：先更新内存缓存，再交给存储后端
    /// </summary>
    private static void WriteSetting(string key, object? value)
    {
        if (value is null)
        {
            _settingCache!.Remove(key);
        }
        else
        {
            _settingCache![key] = value;
        }
        StoreWrite(key, value);
    }

    /// <summary>
    /// 列表键专用存取（三个文件夹历史 / 额外截图文件夹）。
    /// </summary>
    public static List<string> GetListValue([CallerMemberName] string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return [];
        }
        InitializeSettingProvider();
        return _settingCache!.TryGetValue(key, out object? value) && value is List<string> list
            ? list
            : [];
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
            WriteSetting(key, value);
        }
        catch { }
    }

    public static void DeleteAllSettings()
    {
        try
        {
            InitializeSettingProvider();
            _settingCache!.Clear();
            StoreDeleteAll();
        }
        catch { }
    }

    /// <summary>
    /// 绕过内存缓存，直接从存储回读一个值（内存表示，不存在为 null）。
    /// SetValue 先改内存缓存、写入异常被吞掉，回读属性看不出失败；回读存储才能确认真的落盘。
    /// </summary>
    internal static object? ReadSettingBack(string key) => StoreReadBack(key);

    /// <summary>
    /// 导出全部设置到文件（Debug 为 JSON，Release 为 .reg）。写文件在后台线程执行，失败抛异常。
    /// </summary>
    public static async System.Threading.Tasks.Task ExportSettingsAsync(string path)
    {
        InitializeSettingProvider();
        // 先在调用线程把缓存序列化成文本（快照），写文件放后台线程
        string content = StoreExportText();
        await System.Threading.Tasks.Task.Run(() => StoreWriteFile(path, content));
    }

    /// <summary>
    /// 解析设置文件（可在后台线程调用）。返回 null = 文件不存在或不是合法的设置文件，原设置不动。
    /// 校验只保证文件能完整解析，不校验个别键合法性（未知键忽略、值按默认转换容错）。
    /// </summary>
    public static Dictionary<string, object?>? ReadSettingsFile(string path) => StoreReadFile(path);

    /// <summary>
    /// 整档替换当前设置并写入存储（ReadSettingsFile 成功后在 UI 线程调用，与其它设置写入同一条线程）。
    /// </summary>
    public static void ApplySettings(Dictionary<string, object?> settings)
    {
        InitializeSettingProvider();
        _settingCache!.Clear();
        StoreDeleteAll();
        foreach (var (name, value) in settings)
        {
            WriteSetting(name, value);
        }
    }

    public static void ClearCache()
    {
        _settingCache = null;
    }

    #endregion
}
