using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starshot.Features.Background;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
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


    public Visibility AccentFromWallpaperVisibility => AppConfig.EnableWallpaper ? Visibility.Visible : Visibility.Collapsed;

    public Visibility WallpaperRowVisibility => AppConfig.WallpaperMode == 0 ? Visibility.Collapsed : Visibility.Visible;


    public int WallpaperMode
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.WallpaperMode = value;
                OnPropertyChanged(nameof(WallpaperChooseLabel));
                OnPropertyChanged(nameof(WallpaperValue));
                OnPropertyChanged(nameof(WallpaperRowVisibility));
                OnPropertyChanged(nameof(AccentFromWallpaperVisibility));
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
            }
        }
    } = AppConfig.WallpaperMode;


    public string WallpaperChooseLabel => AppConfig.WallpaperMode switch
    {
        2 => Lang.Starshot_WallpaperChooseFolder,
        3 => Lang.Starshot_WallpaperChooseVideo,
        _ => Lang.Starshot_WallpaperChooseImage,
    };


    public string WallpaperValue => AppConfig.WallpaperMode switch
    {
        2 => string.IsNullOrWhiteSpace(AppConfig.WallpaperFolder) ? Lang.Starshot_WallpaperNone : AppConfig.WallpaperFolder!,
        3 => string.IsNullOrWhiteSpace(AppConfig.WallpaperVideoFile) ? Lang.Starshot_WallpaperNone : AppConfig.WallpaperVideoFile!,
        _ => string.IsNullOrWhiteSpace(AppConfig.WallpaperFile) ? Lang.Starshot_WallpaperNone : AppConfig.WallpaperFile!,
    };


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
                case 2:  // 文件夹随机 → 读源
                {
                    string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                    AppConfig.WallpaperFolder = folder;
                    break;
                }
                case 3:  // 指定视频 → 读源
                {
                    string? path = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, VideoFilters);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    AppConfig.WallpaperVideoFile = path;
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
            string? target = AppConfig.WallpaperMode switch
            {
                2 => AppConfig.WallpaperFolder,
                3 => string.IsNullOrEmpty(AppConfig.WallpaperVideoFile) ? null : Path.GetDirectoryName(AppConfig.WallpaperVideoFile),
                _ => Path.Combine(AppConfig.CacheFolder, "bg"),
            };
            if (string.IsNullOrEmpty(target)) return;
            Directory.CreateDirectory(target);
            await Launcher.LaunchFolderPathAsync(target);
        }
        catch
        {
        }
    }


    private void RefreshWallpaperBindings()
    {
        OnPropertyChanged(nameof(WallpaperValue));
        OnPropertyChanged(nameof(AccentFromWallpaperVisibility));
    }


    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }

}
