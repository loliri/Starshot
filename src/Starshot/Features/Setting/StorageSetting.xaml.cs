using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Starshot.Features.Codec;
using Starshot.Features.Screenshot;
using Starshot.Frameworks;
using Starshot.Helpers;
using Windows.Storage;
using Windows.System;

namespace Starshot.Features.Setting;

public sealed partial class StorageSetting : PageBase
{
    private readonly ILogger<StorageSetting> _logger = AppConfig.GetLogger<StorageSetting>();

    private TextBox? _lastFocusedTemplateBox;

    private static readonly string[] _tokens =
    {
        "process",
        "processPath",
        "title",
        "timestamp",
        "time",
        "date",
        "year",
        "month",
        "day",
        "hour",
        "minute",
        "second",
        "width",
        "height",
    };

    public string FileNamePattern
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.ScreenshotFileNamePattern = value;
                FileNamePreview = BuildPreview(value);
            }
        }
    } = AppConfig.ScreenshotFileNamePattern;

    public string FileNamePreview
    {
        get;
        set => SetProperty(ref field, value);
    } = BuildPreview(AppConfig.ScreenshotFileNamePattern);

    public string RegionFileNamePattern
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.RegionScreenshotFileNamePattern = value;
                RegionFileNamePreview = BuildPreview(value);
            }
        }
    } = AppConfig.RegionScreenshotFileNamePattern;

    public string RegionFileNamePreview
    {
        get;
        set => SetProperty(ref field, value);
    } = BuildPreview(AppConfig.RegionScreenshotFileNamePattern);

    public int FileNameTitleMaxLength
    {
        get;
        set
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
        return ScreenCaptureService.BuildFileName(
                "explorer",
                "explorer.exe",
                "StarRail",
                DateTimeOffset.Now,
                3840,
                2160,
                pattern
            ) + ".png";
    }

    public StorageSetting()
    {
        InitializeComponent();
        InitializeScreenshotFolder();
        LogFolder = AppConfig.LogFolder;
        _lastFocusedTemplateBox = FileNameTextBox;
        BuildPlaceholderLinks();
        RefreshLastBackup();
        _ = RefreshStatsAsync();
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
            link.Inlines.Add(
                new Run
                {
                    Text = _tokens[i],
                    FontFamily = new FontFamily("Consolas, Cascadia Code, Microsoft YaHei UI"),
                }
            );
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

    private void TextBlock_IsTextTrimmedChanged(
        TextBlock sender,
        IsTextTrimmedChangedEventArgs args
    )
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }

    #region Screenshot Folder


    public string ScreenshotFolder
    {
        get;
        set => SetProperty(ref field, value);
    }

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

    [RelayCommand]
    private async Task ShowScreenshotFolderHistory()
    {
        var dialog = new FolderHistoryDialog(
            Lang.Starshot_ScreenshotFolderHistoryTitle,
            () => AppConfig.ScreenshotFolderHistory,
            list => AppConfig.ScreenshotFolderHistory = list,
            ResetScreenshotFolder
        )
        {
            XamlRoot = this.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    [RelayCommand]
    private async Task ShowLogFolderHistory()
    {
        var dialog = new FolderHistoryDialog(
            Lang.Starshot_LogFolderHistoryTitle,
            () => AppConfig.LogFolderHistory,
            list => AppConfig.LogFolderHistory = list,
            ResetLogFolder
        )
        {
            XamlRoot = this.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void ResetScreenshotFolder()
    {
        try
        {
            // 默认值与 AppConfig.ScreenshotFolder 的 getter 默认一致（我的图片\Starshot）
            string defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Starshot"
            );
            if (
                string.Equals(
                    AppConfig.ScreenshotFolder,
                    defaultFolder,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                InAppToast.MainWindow?.Information(null, Lang.Starshot_AlreadyDefault, 3000);
                return;
            }
            Directory.CreateDirectory(defaultFolder);
            ScreenshotFolder = defaultFolder;
            AppConfig.ScreenshotFolder = defaultFolder;
            _ = RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset screenshot folder");
        }
    }

    #endregion


    #region Log Folder


    public Visibility DevModeVisibility =>
        AppConfig.DevMode ? Visibility.Collapsed : Visibility.Visible;

    public string LogFolder
    {
        get;
        set => SetProperty(ref field, value);
    } = AppConfig.LogFolder;

    public int LogLevel
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.LogLevel = value;
                InAppToast.MainWindow?.Information(null, Lang.Starshot_LogFolderRestartTip, 3000);
            }
        }
    } = AppConfig.LogLevel;

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

    private void ResetLogFolder()
    {
        string defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Starshot"
        );
        if (string.Equals(AppConfig.LogFolder, defaultFolder, StringComparison.OrdinalIgnoreCase))
        {
            InAppToast.MainWindow?.Information(null, Lang.Starshot_AlreadyDefault, 3000);
            return;
        }
        AppConfig.LogFolder = defaultFolder;
        LogFolder = defaultFolder;
        InAppToast.MainWindow?.Information(null, Lang.Starshot_LogFolderRestartTip, 3000);
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


    public string LastBackupTime
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public Visibility LastBackupVisible
    {
        get;
        set => SetProperty(ref field, value);
    } = Visibility.Collapsed;

    private string? _lastBackupPath;

    // 备份固定写 LocalAppData（不随日志文件夹/数据位置变化）
    private static string ConfigBackupFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Starshot",
            "backup"
        );

    private void RefreshLastBackup()
    {
        try
        {
            string dir = ConfigBackupFolder;
            if (!Directory.Exists(dir))
            {
                LastBackupVisible = Visibility.Collapsed;
                return;
            }
            var last = Directory
                .GetFiles(dir, "config_*.sjson")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (last is null)
            {
                LastBackupVisible = Visibility.Collapsed;
                return;
            }
            _lastBackupPath = last;
            LastBackupTime =
                $"{Lang.Starshot_LastBackup}  {File.GetLastWriteTime(last):yyyy-MM-dd HH:mm:ss}";
            LastBackupVisible = Visibility.Visible;
        }
        catch { }
    }

    [RelayCommand]
    private async Task BackupConfig()
    {
        try
        {
            Directory.CreateDirectory(ConfigBackupFolder);
            string file = Path.Combine(
                ConfigBackupFolder,
                $"config_{DateTime.Now:yyyyMMdd_HHmmss}.sjson"
            );
            await Task.Run(() => File.Copy(AppConfig.ConfigFilePath, file, overwrite: true));
            RefreshLastBackup();
            _ = RefreshStatsAsync();
            InAppToast.MainWindow?.Success(Lang.Starshot_BackupSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup config");
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_BackupFailed);
        }
    }

    /// <summary>
    /// 导出配置：config.sjson 副本存到用户选的位置（.json 扩展，便于识别与编辑）。
    /// </summary>
    [RelayCommand]
    private async Task ExportConfig()
    {
        try
        {
            string? path = await FileDialogHelper.OpenSaveFileDialogAsync(
                this.XamlRoot,
                $"Starshot_config_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                ("JSON", ".json")
            );
            if (string.IsNullOrWhiteSpace(path))
                return;
            await Task.Run(() => File.Copy(AppConfig.ConfigFilePath, path, overwrite: true));
            InAppToast.MainWindow?.Success(Lang.Starshot_ExportSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export config");
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_ExportFailed);
        }
    }

    /// <summary>
    /// 导入配置：选 JSON 文件 → 校验为合法配置字典 → 覆盖 config.sjson + 清缓存重载。
    /// 校验只保证结构与值类型，不校验个别键合法性（未知键忽略、值按默认转换容错）。
    /// </summary>
    [RelayCommand]
    private async Task ImportConfig()
    {
        try
        {
            string? path = await FileDialogHelper.PickSingleFileAsync(
                this.XamlRoot,
                new[] { ("JSON", ".json"), ("Starshot Settings", ".sjson") }
            );
            if (string.IsNullOrWhiteSpace(path))
                return;
            bool ok = await Task.Run(() => AppConfig.ImportConfigFile(path));
            if (ok)
            {
                InAppToast.MainWindow?.Success(Lang.Starshot_ImportSuccess);
            }
            else
            {
                InAppToast.MainWindow?.Warning(null, Lang.Starshot_ImportInvalid, 5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import config");
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_ImportFailed);
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
                var folder = await StorageFolder.GetFolderFromPathAsync(
                    Path.GetDirectoryName(_lastBackupPath)!
                );
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


    public string ScreenshotFolderSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "—";

    public string ImageCacheSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "—";

    public string WallpaperSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "—";

    public string LogSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "—";

    public string BackupSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "—";

    /// <summary>OCR 引擎文件（exe 旁 oneocr.dll + oneocr.onemodel）大小，仅展示，不参与任何清理逻辑</summary>
    public string OcrEngineSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "—";

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
                    if (
                        !string.Equals(
                            Path.GetFileName(f),
                            current,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        try
                        {
                            File.Delete(f);
                        }
                        catch { }
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
            string backupDir = ConfigBackupFolder;

            var (ssSize, cacheSize, bgSize, logSize, backupSize, ocrSize) = await Task.Run(() =>
            {
                long s = StorageStatsHelper.GetDirectorySize(ssFolder);
                long bg = StorageStatsHelper.GetDirectorySize(bgDir);
                long cc = StorageStatsHelper.GetDirectorySize(Path.Combine(cache, "thumb"));
                long ll = StorageStatsHelper.GetDirectorySize(logDir);
                long bk = StorageStatsHelper.GetDirectorySize(backupDir);
                long ocr = 0;
                try
                {
                    string dir = AppContext.BaseDirectory;
                    string dll = Path.Combine(dir, "oneocr.dll");
                    string model = Path.Combine(dir, "oneocr.onemodel");
                    if (File.Exists(dll))
                        ocr += new FileInfo(dll).Length;
                    if (File.Exists(model))
                        ocr += new FileInfo(model).Length;
                }
                catch { }
                return (s, cc, bg, ll, bk, ocr);
            });

            ScreenshotFolderSize = StorageStatsHelper.FormatSize(ssSize);
            ImageCacheSize = StorageStatsHelper.FormatSize(cacheSize);
            WallpaperSize = StorageStatsHelper.FormatSize(bgSize);
            LogSize = StorageStatsHelper.FormatSize(logSize);
            BackupSize = StorageStatsHelper.FormatSize(backupSize);
            OcrEngineSize = StorageStatsHelper.FormatSize(ocrSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute storage stats");
        }
    }

    #endregion
}
