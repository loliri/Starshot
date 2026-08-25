using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Starshot.Features.Codec;
using Starshot.Features.Database;
using Starshot.Features.Screenshot;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace Starshot.Features.Setting;

public sealed partial class StorageSetting : PageBase
{

    private readonly ILogger<StorageSetting> _logger = AppConfig.GetLogger<StorageSetting>();

    private TextBox? _lastFocusedTemplateBox;

    private static readonly string[] _tokens =
    {
        "process", "processPath", "title", "timestamp", "time", "date",
        "year", "month", "day", "hour", "minute", "second", "width", "height",
    };


    public string FileNamePattern
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.ScreenshotFileNamePattern = value;
                FileNamePreview = BuildPreview(value);
            }
        }
    } = AppConfig.ScreenshotFileNamePattern;


    public string FileNamePreview { get; set => SetProperty(ref field, value); } = BuildPreview(AppConfig.ScreenshotFileNamePattern);


    public string RegionFileNamePattern
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.RegionScreenshotFileNamePattern = value;
                RegionFileNamePreview = BuildPreview(value);
            }
        }
    } = AppConfig.RegionScreenshotFileNamePattern;


    public string RegionFileNamePreview { get; set => SetProperty(ref field, value); } = BuildPreview(AppConfig.RegionScreenshotFileNamePattern);


    public int FileNameTitleMaxLength
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.ScreenshotFileNameTitleMaxLength = value;
                FileNamePreview = BuildPreview(FileNamePattern);
                RegionFileNamePreview = BuildPreview(RegionFileNamePattern);
            }
        }
    } = AppConfig.ScreenshotFileNameTitleMaxLength;


    private static string BuildPreview(string pattern)
    {
        return ScreenCaptureService.BuildFileName("explorer", "explorer.exe", "StarRail", DateTimeOffset.Now, 3840, 2160, pattern) + ".png";
    }


    public StorageSetting()
    {
        InitializeComponent();
        DatabaseFolder = AppConfig.UserDataFolder;
#if DEBUG
        // Debug 数据库锁死父目录，不读写 database.json，更改位置入口隐藏
        Grid_ChangeDatabaseFolder.Visibility = Visibility.Collapsed;
#endif
        // 当前目录存在 database.json（便携化锚定，优先级高于 LocalAppData）时不允许再改：
        // 按钮只写 LocalAppData 那份，写了也会被当前目录的压住，改了无效
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "database.json")))
        {
            Button_ChangeDatabaseFolder.IsEnabled = false;
        }
        InitializeScreenshotFolder();
        LogFolder = AppConfig.LogFolder;
        _lastFocusedTemplateBox = FileNameTextBox;
        BuildPlaceholderLinks();
        RefreshLastBackup();
        _ = RefreshStatsAsync();
    }


    public string DatabaseFolder { get; set => SetProperty(ref field, value); } = "";


    [RelayCommand]
    private async Task ChangeDatabaseFolder()
    {
#if DEBUG
        return; // Debug 数据库锁死父目录，不产生任何锚定写入
#else
        try
        {
            var folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            folder = Path.GetFullPath(folder);
            if (string.Equals(folder, Path.GetFullPath(AppConfig.UserDataFolder), StringComparison.OrdinalIgnoreCase)) return;
            // 复用备份逻辑把当前库整套快照到新位置（原库不动，保兼容）
            await Task.Run(() => DatabaseService.BackupDatabase(Path.Combine(folder, "StarshotDatabase.db")));
            // 写锚定 JSON，下次启动按它定位数据库
            string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starshot");
            Directory.CreateDirectory(localAppData);
            await File.WriteAllTextAsync(Path.Combine(localAppData, "database.json"),
                System.Text.Json.JsonSerializer.Serialize(new { DatabaseFolder = folder }));
            DatabaseFolder = folder;
            var dialog = new DatabaseLocationDialog { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChangeDatabaseFolder");
        }
#endif
    }


    private void BuildPlaceholderLinks()
    {
        PlaceholderTextBlock.Inlines.Clear();
        // 第一行：说明 + GitHub 链接
        PlaceholderTextBlock.Inlines.Add(new Run { Text = Lang.Starshot_ClickToInsert });
        var help = new Hyperlink { NavigateUri = new Uri(GetHelpUrl()) };
        help.Inlines.Add(new Run { Text = "Github" + Lang.Starshot_ClickToInsertSuffix });
        PlaceholderTextBlock.Inlines.Add(help);
        PlaceholderTextBlock.Inlines.Add(new LineBreak());
        // 按钮区：每个占位符一个链接（文字不带 {}，点击插入 {token}）
        for (int i = 0; i < _tokens.Length; i++)
        {
            if (i > 0)
            {
                PlaceholderTextBlock.Inlines.Add(new Run { Text = "  " });
            }
            string token = "{" + _tokens[i] + "}";
            var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
            link.Inlines.Add(new Run
            {
                Text = _tokens[i],
                FontFamily = new FontFamily("Consolas, Cascadia Code, Microsoft YaHei UI"),
            });
            link.Click += (_, _) => InsertToken(token);
            PlaceholderTextBlock.Inlines.Add(link);
        }
    }


    private static string GetHelpUrl()
    {
        string repo = AppConfig.RepoBaseUrl;
        return AppConfig.Language switch
        {
            "zh-CN" => $"{repo}/blob/main/docs/README.zh-CN.md#文件名模板",
            "zh-TW" => $"{repo}/blob/main/docs/README.zh-TW.md#檔案名稱範本",
            "ja-JP" => $"{repo}/blob/main/docs/README.ja.md#ファイル名テンプレート",
            "fr-FR" => $"{repo}/blob/main/docs/README.fr.md#modèles-de-nom-de-fichier",
            "ru-RU" => $"{repo}/blob/main/docs/README.ru.md#шаблоны-имён-файлов",
            "es-ES" => $"{repo}/blob/main/docs/README.es.md#plantillas-de-nombre-de-archivo",
            _ => $"{repo}#filename-templates",
        };
    }


    private void TemplateTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _lastFocusedTemplateBox = (TextBox)sender;
    }


    private void InsertToken(string token)
    {
        var box = _lastFocusedTemplateBox ?? FileNameTextBox;
        int pos = box.SelectionStart;
        box.Text = box.Text.Insert(pos, token);
        box.SelectionStart = pos + token.Length;
        box.Focus(FocusState.Programmatic);
    }


    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }



    #region Data Folder


    [RelayCommand]
    private async Task OpenDataFolder()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppConfig.UserDataFolder))
            {
                await Launcher.LaunchFolderPathAsync(AppConfig.UserDataFolder);
            }
        }
        catch { }
    }


    #endregion



    #region Screenshot Folder


    public string ScreenshotFolder { get; set => SetProperty(ref field, value); }


    private void InitializeScreenshotFolder()
    {
        try
        {
            string? folder = AppConfig.ScreenshotFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Path.Join(AppConfig.LogFolder, "Screenshots");
            }
            Directory.CreateDirectory(folder);
            ScreenshotFolder = folder;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize screenshot folder");
        }
    }


    [RelayCommand]
    private async Task ChangeScreenshotFolder()
    {
        try
        {
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (Directory.Exists(folder))
            {
                ScreenshotFolder = folder;
                AppConfig.ScreenshotFolder = folder;
                await RefreshStatsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change screenshot folder");
        }
    }


    [RelayCommand]
    private async Task OpenScreenshotFolder()
    {
        try
        {
            if (Directory.Exists(ScreenshotFolder))
            {
                await Launcher.LaunchFolderPathAsync(ScreenshotFolder);
            }
        }
        catch { }
    }


    #endregion



    #region Log Folder


    public string LogFolder { get; set => SetProperty(ref field, value); } = AppConfig.LogFolder;


    public int LogLevel
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.LogLevelConfig = value;
                InAppToast.MainWindow?.Information(null, Lang.Starshot_LogFolderRestartTip, 3000);
            }
        }
    } = AppConfig.LogLevelConfig;


    [RelayCommand]
    private async Task ChangeLogFolder()
    {
        try
        {
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (Directory.Exists(folder))
            {
                AppConfig.LogFolder = folder;
                LogFolder = folder;
                InAppToast.MainWindow?.Information(null, Lang.Starshot_LogFolderRestartTip, 3000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change log folder");
        }
    }


    [RelayCommand]
    private async Task OpenLogFolder()
    {
        try
        {
            if (Directory.Exists(LogFolder))
            {
                await Launcher.LaunchFolderPathAsync(LogFolder);
            }
        }
        catch { }
    }


    #endregion



    #region Database Backup


    public string LastBackupTime { get; set => SetProperty(ref field, value); } = "";


    public Visibility LastBackupVisible { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;


    private string? _lastBackupPath;

    // 安装版线备份进日志文件夹体系（LocalAppData）；便携版照旧在数据目录（便携布局的根）
    private static string DatabaseBackupFolder => Path.Combine(AppConfig.Installer ? AppConfig.LogFolder : AppConfig.UserDataFolder, "backup");


    private void RefreshLastBackup()
    {
        try
        {
            string dir = DatabaseBackupFolder;
            if (!Directory.Exists(dir))
            {
                LastBackupVisible = Visibility.Collapsed;
                return;
            }
            var last = Directory.GetFiles(dir, "StarshotDatabase_*.db")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (last is null)
            {
                LastBackupVisible = Visibility.Collapsed;
                return;
            }
            _lastBackupPath = last;
            LastBackupTime = $"{Lang.Starshot_LastBackup}  {File.GetLastWriteTime(last):yyyy-MM-dd HH:mm:ss}";
            LastBackupVisible = Visibility.Visible;
        }
        catch { }
    }


    [RelayCommand]
    private async Task BackupDatabase()
    {
        try
        {
            Directory.CreateDirectory(DatabaseBackupFolder);
            string file = Path.Combine(DatabaseBackupFolder, $"StarshotDatabase_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            await Task.Run(() => DatabaseService.BackupDatabase(file));
            RefreshLastBackup();
            _ = RefreshStatsAsync();
            InAppToast.MainWindow?.Success(Lang.Starshot_BackupSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup database");
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_BackupFailed);
        }
    }


    [RelayCommand]
    private async Task OpenLastBackup()
    {
        try
        {
            if (!string.IsNullOrEmpty(_lastBackupPath) && File.Exists(_lastBackupPath))
            {
                var item = await StorageFile.GetFileFromPathAsync(_lastBackupPath);
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(_lastBackupPath)!);
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(item);
                await Launcher.LaunchFolderAsync(folder, options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open last backup");
        }
    }


    #endregion



    #region Storage Stats


    public string ScreenshotFolderSize { get; set => SetProperty(ref field, value); } = "—";


    public string ImageCacheSize { get; set => SetProperty(ref field, value); } = "—";


    public string WallpaperSize { get; set => SetProperty(ref field, value); } = "—";


    public string LogSize { get; set => SetProperty(ref field, value); } = "—";


    public string BackupSize { get; set => SetProperty(ref field, value); } = "—";


    [RelayCommand]
    private async Task RefreshStats()
    {
        await RefreshStatsAsync();
    }


    [RelayCommand]
    private void ClearCache()
    {
        try
        {
            ImageThumbnail.ClearThumbnailCache();
            // 删除未使用的壁纸文件（保留当前在用的）
            string bgDir = Path.Combine(AppConfig.CacheFolder, "bg");
            if (Directory.Exists(bgDir))
            {
                string? current = AppConfig.WallpaperFile;
                foreach (var f in Directory.EnumerateFiles(bgDir))
                {
                    if (!string.Equals(Path.GetFileName(f), current, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            InAppToast.MainWindow?.Success(Lang.ScreenshotSetting_ClearSuccessfully);
            _ = RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear cache");
            InAppToast.MainWindow?.Error(ex, Lang.ScreenshotSetting_ClearFailed);
        }
    }


    private async Task RefreshStatsAsync()
    {
        try
        {
            string ssFolder = ScreenshotFolder;
            string cache = AppConfig.CacheFolder;
            string bgDir = Path.Combine(cache, "bg");
            string logDir = Path.Combine(AppConfig.LogFolder, "log");
            string backupDir = DatabaseBackupFolder;

            var (ssSize, cacheSize, bgSize, logSize, backupSize) = await Task.Run(() =>
            {
                long s = StorageStatsHelper.GetDirectorySize(ssFolder);
                long bg = StorageStatsHelper.GetDirectorySize(bgDir);
                long cc = StorageStatsHelper.GetDirectorySize(Path.Combine(cache, "thumb"));
                long ll = StorageStatsHelper.GetDirectorySize(logDir);
                long bk = StorageStatsHelper.GetDirectorySize(backupDir);
                return (s, cc, bg, ll, bk);
            });

            ScreenshotFolderSize = StorageStatsHelper.FormatSize(ssSize);
            ImageCacheSize = StorageStatsHelper.FormatSize(cacheSize);
            WallpaperSize = StorageStatsHelper.FormatSize(bgSize);
            LogSize = StorageStatsHelper.FormatSize(logSize);
            BackupSize = StorageStatsHelper.FormatSize(backupSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute storage stats");
        }
    }


    #endregion


}
