using Starshot.Features.Database;
using Starshot.Features.ViewHost;
using Starshot.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Starshot;

public static partial class AppConfig
{

    public static string AppVersion { get; private set; }

    public static string CacheFolder { get; private set; }

    public static string UserDataFolder { get; private set; }

    public static string LogFile { get; internal set; }




    public static async Task CheckEnviromentAsync()
    {
        // 数据库固定放在根目录（app 的父目录）。AppContext.BaseDirectory 带尾部分隔符，先去掉再取父目录。
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        UserDataFolder = Path.GetDirectoryName(baseDir) ?? baseDir;

        // 先用默认 LogFolder 算 CacheFolder/LogFile：欢迎页选壁纸要拷 cache/bg，
        // 而 DB 在欢迎页之后才创建，读不到用户配置的 LogFolder
        string logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starshot");
        CacheFolder = Path.Combine(logFolder, "cache");
        LogFile = Path.Combine(logFolder, "log", $"Starshot_{DateTime.Now:yyMMdd}.log");
        Directory.CreateDirectory(CacheFolder);

        // 首次启动（DB 不存在）弹欢迎页；用户关掉不完成则退出
        string dbPath = Path.Combine(UserDataFolder, "StarshotDatabase.db");
        WelcomeWindow? welcome = null;
        if (!File.Exists(dbPath))
        {
            welcome = new Features.ViewHost.WelcomeWindow();
            if (!await welcome.WaitAsync())
            {
                Environment.Exit(0);
            }
        }

        DatabaseService.SetDatabase(UserDataFolder);

        // 欢迎页选的配置在 SetDatabase 之后才写 DB（之前 DB 没创建，直接写会丢）
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
                // 没选壁纸 → 默认用内置 vandesart.jpg（拷 Assets → cache/bg）
                string bgPath = Path.Combine(CacheFolder, "bg", "vandesart.jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(bgPath)!);
                string assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "vandesart.jpg");
                if (File.Exists(assetPath)) File.Copy(assetPath, bgPath, overwrite: true);
                AppConfig.WallpaperFile = "vandesart.jpg";
                AppConfig.WallpaperMode = 1;
            }
            if (!string.IsNullOrWhiteSpace(welcome.ScreenshotFolderPath))
            {
                AppConfig.ScreenshotFolder = welcome.ScreenshotFolderPath;
            }
        }

        // 版本号读 version.ini（启动器同源），无则 Local（debug/local release）
        AppVersion = ReadVersionFromIni();

        // 应用强调色与语言
        AccentColorHelper.ChangeAppAccentColor(AccentColor);
        SetLanguage(Language);

        // DB 后读用户配置的 LogFolder 覆盖（首次 DB 没值，保持默认）
        logFolder = LogFolder;
        CacheFolder = Path.Combine(logFolder, "cache");
        LogFile = Path.Combine(logFolder, "log", $"Starshot_{DateTime.Now:yyMMdd}.log");

        Directory.CreateDirectory(CacheFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);

        await Task.CompletedTask;
    }


    /// <summary>
    /// 读 version.ini 的 version 字段；无文件或格式错返回 "Local"（debug/local release）
    /// </summary>
    private static string ReadVersionFromIni()
    {
        try
        {
            string ini = Path.Combine(UserDataFolder, "version.ini");
            if (!File.Exists(ini)) return "Local";
            string content = File.ReadAllText(ini);
            int eq = content.IndexOf('=');
            if (eq > 0)
            {
                string v = content[(eq + 1)..].Trim();
                return string.IsNullOrEmpty(v) ? "Local" : v;
            }
            return "Local";
        }
        catch
        {
            return "Local";
        }
    }


    /// <summary>
    /// 设置界面语言（运行时切换，无需重启）
    /// </summary>
    public static void SetLanguage(string? language)
    {
        try
        {
            CultureInfo culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.InstalledUICulture : new CultureInfo(language);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch { }
    }


}
