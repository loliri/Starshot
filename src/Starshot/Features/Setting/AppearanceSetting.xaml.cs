using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starshot.Features.Background;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI;

namespace Starshot.Features.Setting;

public sealed partial class AppearanceSetting : PageBase
{


    public AppearanceSetting()
    {
        InitializeComponent();
    }


    public int Theme
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.Theme = value;
                App.Current.MainWindow?.ApplyTheme();
            }
        }
    } = AppConfig.Theme;


    public bool EnableAcrylic
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableAcrylic = value;
                App.Current.MainWindow?.ApplyBackdrop();
            }
        }
    } = AppConfig.EnableAcrylic;


    public Windows.UI.Color AccentColorValue { get; set => SetProperty(ref field, value); } = ParseAccentHex(AppConfig.AccentColor);


    private static Windows.UI.Color ParseAccentHex(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                return hex.ToColor();
            }
        }
        catch { }
        return Windows.UI.Color.FromArgb(255, 0x2D, 0xBE, 0x9C);
    }


    [RelayCommand]
    private void ApplyAccent()
    {
        AppConfig.AccentColor = AccentColorValue.ToHex();
        AccentColorHelper.ChangeAppAccentColor(AccentColorValue);
        EnableAccentFromWallpaper = false;  // 手动选色后关掉自动取色，避免下次换壁纸覆盖
        AccentColorFlyout?.Hide();
    }


    public bool EnableAccentFromWallpaper
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableAccentFromWallpaper = value;
                if (value)
                {
                    // 重新开启 → 立即从当前壁纸重新取色（不动壁纸显示）
                    WeakReferenceMessenger.Default.Send(new AccentRefreshRequestedMessage());
                }
            }
        }
    } = AppConfig.EnableAccentFromWallpaper;


    public Visibility WallpaperRowVisibility => AppConfig.WallpaperMode == 0 ? Visibility.Collapsed : Visibility.Visible;


    public int WallpaperMode
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.WallpaperMode = value;
                // 切换模式时检查目标路径是否还在；不在就清空配置项（不提示、不回退到无）
                switch (value)
                {
                    case 1:
                        if (!string.IsNullOrEmpty(AppConfig.WallpaperFile)
                            && !File.Exists(Path.Combine(AppConfig.CacheFolder, "bg", AppConfig.WallpaperFile)))
                            AppConfig.WallpaperFile = null;
                        break;
                    case 2:
                        if (!string.IsNullOrEmpty(AppConfig.WallpaperVideoFile)
                            && !File.Exists(AppConfig.WallpaperVideoFile))
                            AppConfig.WallpaperVideoFile = null;
                        break;
                    case 3:
                        if (!string.IsNullOrEmpty(AppConfig.WallpaperFolder)
                            && !Directory.Exists(AppConfig.WallpaperFolder))
                            AppConfig.WallpaperFolder = null;
                        break;
                }
                OnPropertyChanged(nameof(WallpaperChooseLabel));
                OnPropertyChanged(nameof(WallpaperValue));
                OnPropertyChanged(nameof(WallpaperRowVisibility));
                OnPropertyChanged(nameof(WallpaperFolderVideoOnlyVisibility));
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
                // 开着自动取色：切换模式后强制重新从新壁纸取色（避免 _lastFile 短路 / 视频模式不取色）
                if (AppConfig.EnableAccentFromWallpaper)
                {
                    WeakReferenceMessenger.Default.Send(new AccentRefreshRequestedMessage());
                }
            }
        }
    } = AppConfig.WallpaperMode;


    public string WallpaperChooseLabel => AppConfig.WallpaperMode switch
    {
        2 => Lang.Starshot_WallpaperChooseVideo,
        3 => Lang.Starshot_WallpaperChooseFolder,
        _ => Lang.Starshot_WallpaperChooseImage,
    };


    public string WallpaperValue => AppConfig.WallpaperMode switch
    {
        2 => string.IsNullOrWhiteSpace(AppConfig.WallpaperVideoFile) ? Lang.Starshot_WallpaperNone : AppConfig.WallpaperVideoFile!,
        3 => string.IsNullOrWhiteSpace(AppConfig.WallpaperFolder) ? Lang.Starshot_WallpaperNone : AppConfig.WallpaperFolder!,
        _ => string.IsNullOrWhiteSpace(AppConfig.WallpaperFile) ? Lang.Starshot_WallpaperNone : AppConfig.WallpaperFile!,
    };


    public Visibility WallpaperFolderVideoOnlyVisibility => AppConfig.WallpaperMode == 3 ? Visibility.Visible : Visibility.Collapsed;


    public bool WallpaperFolderVideoOnly
    {
        get => AppConfig.WallpaperFolderVideoOnly;
        set
        {
            if (AppConfig.WallpaperFolderVideoOnly != value)
            {
                AppConfig.WallpaperFolderVideoOnly = value;
                OnPropertyChanged();
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
            }
        }
    }


    private static readonly (string, string)[] ImageFilters =
    {
        ("Images", ".jpg"), ("Images", ".jpeg"), ("Images", ".png"),
        ("Images", ".bmp"), ("Images", ".webp"), ("Images", ".gif"),
    };

    private static readonly (string, string)[] VideoFilters =
    {
        ("Videos", ".mp4"), ("Videos", ".mkv"), ("Videos", ".mov"), ("Videos", ".avi"), ("Videos", ".webm"),
    };


    [RelayCommand]
    private async Task ChooseWallpaper()
    {
        try
        {
            switch (AppConfig.WallpaperMode)
            {
                case 2:  // 指定视频 → 读源
                {
                    string? path = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, VideoFilters);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    AppConfig.WallpaperVideoFile = path;
                    break;
                }
                case 3:  // 文件夹随机（图/视频混合）→ 读源
                {
                    string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                    AppConfig.WallpaperFolder = folder;
                    break;
                }
                default:  // 1 指定图片 → 复制到 bg/
                {
                    string? path = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, ImageFilters);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    string fileName = Path.GetFileName(path);
                    string bgDir = Path.Combine(AppConfig.CacheFolder, "bg");
                    Directory.CreateDirectory(bgDir);
                    File.Copy(path, Path.Combine(bgDir, fileName), overwrite: true);
                    AppConfig.WallpaperFile = fileName;
                    break;
                }
            }
            RefreshWallpaperBindings();
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
        }
        catch
        {
        }
    }


    [RelayCommand]
    private async Task OpenWallpaperFolder()
    {
        try
        {
            // 模式 3 = 文件夹 → 直接打开文件夹；模式 1/2 = 文件 → explorer 选中文件（不是开父目录）
            switch (AppConfig.WallpaperMode)
            {
                case 3:
                {
                    string? folder = AppConfig.WallpaperFolder;
                    if (!string.IsNullOrEmpty(folder)) await Launcher.LaunchFolderPathAsync(folder);
                    break;
                }
                case 2:
                {
                    string? file = AppConfig.WallpaperVideoFile;
                    if (!string.IsNullOrEmpty(file) && File.Exists(file)) SelectInExplorer(file);
                    break;
                }
                default:  // 1
                {
                    string? f = AppConfig.WallpaperFile;
                    if (!string.IsNullOrEmpty(f))
                    {
                        string path = Path.Combine(AppConfig.CacheFolder, "bg", f);
                        if (File.Exists(path)) SelectInExplorer(path);
                    }
                    break;
                }
            }
        }
        catch
        {
        }
    }


    /// <summary>explorer 打开并选中指定文件。</summary>
    private static void SelectInExplorer(string filePath)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }


    private void RefreshWallpaperBindings()
    {
        OnPropertyChanged(nameof(WallpaperValue));
    }


    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }

}
