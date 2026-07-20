using Starshot.Features.Database;
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
        DatabaseService.SetDatabase(UserDataFolder);

        // 版本号读 version.ini（启动器同源），无则 Local（debug/local release）
        AppVersion = ReadVersionFromIni();

        // 应用强调色与语言
        AccentColorHelper.ChangeAppAccentColor(AccentColor);
        SetLanguage(Language);

        // 日志/缓存文件夹：存在数据库里的设置，默认 %LOCALAPPDATA%\Starshot
        string logFolder = LogFolder;
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
