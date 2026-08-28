using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Starshot.Features.ViewHost;
using Starshot.Helpers;

namespace Starshot;

public static partial class AppConfig
{
    public static string AppVersion { get; private set; }

    public static string CacheFolder { get; private set; }

    public static string UserDataFolder { get; private set; }

    public static string LogFile { get; internal set; }

    /// <summary>日志文件名：Starshot_{版本}_{yyMMdd}.log。AppVersion 已设用缓存，否则读 assembly 兜底（启动早期崩溃时 AppVersion 还没赋值）。</summary>
    internal static string BuildLogFileName()
    {
        string ver = !string.IsNullOrEmpty(AppVersion)
            ? AppVersion
            : typeof(App)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? "0.0.0-local";
        return $"Starshot_{ver}_{DateTime.Now:yyMMdd}.log";
    }

    public static async Task CheckEnviromentAsync()
    {
        // 数据库固定放在根目录（app 的父目录）。AppContext.BaseDirectory 带尾部分隔符，先去掉再取父目录。
        string baseDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
        UserDataFolder = Path.GetDirectoryName(baseDir) ?? baseDir;

        // LocalAppData 根（日志/缓存默认家，安装版数据位置）
        string localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Starshot"
        );

        // 版本号：Debug 构建显示 "Debug"（日志 Starshot_Debug_*.log + 启动 vDebug）；Release 读 assembly 内嵌
#if DEBUG
        AppVersion = "Debug";
#else
        AppVersion =
            typeof(App)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "0.0.0-local";
#endif

        // 先用默认 LogFolder 算 CacheFolder/LogFile：欢迎页选壁纸要拷 bg/，
        // 而 DB 在欢迎页之后才创建，读不到用户配置的 LogFolder
        string logFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Starshot"
        );
        CacheFolder = logFolder;
        LogFile = Path.Combine(logFolder, "log", BuildLogFileName());
        Directory.CreateDirectory(CacheFolder);

        // 分发线位置判定（最先做，之后一切逻辑都在正确的数据位置语义下）：
        // 安装版 = exe 内烙有安装标志（DOS stub，CI 打包时写入）→ 数据落 LocalAppData；便携 = 父目录
        if (Installer)
        {
            UserDataFolder = localAppData;
        }

        // 首次启动（配置文件不存在）弹欢迎页；用户关掉不完成则退出
        WelcomeWindow? welcome = null;
        if (!File.Exists(ConfigFilePath))
        {
            welcome = new Features.ViewHost.WelcomeWindow();
            if (!await welcome.WaitAsync())
            {
                Environment.Exit(0);
            }
            // 落盘首启判定文件：全默认不改设置的用户 config.sjson 也会生成，不再重复弹欢迎页
            EnsureConfigFile();
        }

        // 高优先级运行开关：开了每次启动自提升（对自身 SetPriorityClass 无需权限，任何启动方式都生效）；关=系统默认
        try
        {
            if (HighPriorityProcess)
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch { }

        // 欢迎页选的配置在此写入（之前配置缓存未初始化，直接写会丢）
        if (welcome is not null)
        {
            if (welcome.WallpaperIsVideo && !string.IsNullOrWhiteSpace(welcome.WallpaperVideoPath))
            {
                AppConfig.WallpaperVideoFile = welcome.WallpaperVideoPath;
                AppConfig.WallpaperMode = 2;
            }
            else if (!string.IsNullOrWhiteSpace(welcome.WallpaperFileName))
            {
                AppConfig.WallpaperFile = welcome.WallpaperFileName;
                AppConfig.WallpaperMode = 1;
            }
            else
            {
                // 没选壁纸 → 默认用内置 pic.jpg（拷 Assets → cache/bg）
                string bgPath = Path.Combine(CacheFolder, "bg", "pic.jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(bgPath)!);
                string assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "pic.jpg");
                if (File.Exists(assetPath))
                    File.Copy(assetPath, bgPath, overwrite: true);
                AppConfig.WallpaperFile = "pic.jpg";
                AppConfig.WallpaperMode = 1;
            }
        }

        // 应用强调色与语言
        AccentColorHelper.ChangeAppAccentColor(AccentColor);
        SetLanguage(Language);

        // DB 后读用户配置的 LogFolder 覆盖（首次 DB 没值，保持默认）
        logFolder = LogFolder;
        CacheFolder = logFolder;
        LogFile = Path.Combine(logFolder, "log", BuildLogFileName());

        Directory.CreateDirectory(CacheFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);

        MigrateOldCacheLayout(CacheFolder);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 旧布局 CacheFolder=根/cache，把里面的 bg/thumb 展开到根，与 log 平级。
    /// 幂等：新布局已无 根/cache（或里面已无 bg/thumb）时啥也不做。
    /// </summary>
    private static void MigrateOldCacheLayout(string rootFolder)
    {
        try
        {
            string oldCache = Path.Combine(rootFolder, "cache");
            if (!Directory.Exists(oldCache))
                return;
            foreach (var sub in new[] { "bg", "thumb" })
            {
                string src = Path.Combine(oldCache, sub);
                string dst = Path.Combine(rootFolder, sub);
                if (Directory.Exists(src) && !Directory.Exists(dst))
                {
                    try
                    {
                        Directory.Move(src, dst);
                    }
                    catch { }
                }
            }
            // 旧 cache 空了就删；还有残留（移动失败的）就留着不强行删
            if (Directory.Exists(oldCache) && Directory.GetFileSystemEntries(oldCache).Length == 0)
            {
                try
                {
                    Directory.Delete(oldCache);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 设置界面语言（运行时切换，无需重启）
    /// </summary>
    public static void SetLanguage(string? language)
    {
        try
        {
            CultureInfo culture = string.IsNullOrWhiteSpace(language)
                ? CultureInfo.InstalledUICulture
                : new CultureInfo(language);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch { }
    }
}
