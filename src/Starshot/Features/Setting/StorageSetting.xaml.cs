using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Starshot.Features.Codec;
using Starshot.Features.Screenshot;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.System;

namespace Starshot.Features.Setting;

public sealed partial class StorageSetting : PageBase
{

    private readonly ILogger<StorageSetting> _logger = AppConfig.GetLogger<StorageSetting>();


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
        return ScreenCaptureService.BuildFileName("explorer", "explorer.exe", "Genshin Impact", DateTimeOffset.Now, 1920, 1080, pattern) + ".png";
    }


    public StorageSetting()
    {
        InitializeComponent();
        InitializeScreenshotFolder();
        LogFolder = AppConfig.LogFolder;
        _ = RefreshStatsAsync();
    }


    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }



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



    #region Storage Stats


    public string ScreenshotFolderSize { get; set => SetProperty(ref field, value); } = "—";


    public string ImageCacheSize { get; set => SetProperty(ref field, value); } = "—";


    public string WallpaperSize { get; set => SetProperty(ref field, value); } = "—";


    public string LogSize { get; set => SetProperty(ref field, value); } = "—";


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

            var (ssSize, cacheSize, bgSize, logSize) = await Task.Run(() =>
            {
                long s = StorageStatsHelper.GetDirectorySize(ssFolder);
                long bg = StorageStatsHelper.GetDirectorySize(bgDir);
                long cc = StorageStatsHelper.GetDirectorySize(cache) - bg;  // 缓存不含壁纸，避免重复计数
                long ll = StorageStatsHelper.GetDirectorySize(logDir);
                return (s, cc, bg, ll);
            });

            ScreenshotFolderSize = StorageStatsHelper.FormatSize(ssSize);
            ImageCacheSize = StorageStatsHelper.FormatSize(cacheSize);
            WallpaperSize = StorageStatsHelper.FormatSize(bgSize);
            LogSize = StorageStatsHelper.FormatSize(logSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute storage stats");
        }
    }


    #endregion


}
