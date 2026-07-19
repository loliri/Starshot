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


    public string WallpaperFileName { get; set => SetProperty(ref field, value); } =
        string.IsNullOrWhiteSpace(AppConfig.WallpaperFile) ? Lang.Starshot_WallpaperNone : AppConfig.WallpaperFile!;


    [RelayCommand]
    private async Task ChooseWallpaperFile()
    {
        try
        {
            (string, string)[] filters =
            {
                ("Images", ".jpg"), ("Images", ".jpeg"), ("Images", ".png"),
                ("Images", ".bmp"), ("Images", ".webp"), ("Images", ".gif"),
                ("Videos", ".mp4"), ("Videos", ".mkv"), ("Videos", ".mov"), ("Videos", ".webm"),
            };
            string? path = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, filters);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            string fileName = Path.GetFileName(path);
            string bgDir = Path.Combine(AppConfig.CacheFolder, "bg");
            Directory.CreateDirectory(bgDir);
            File.Copy(path, Path.Combine(bgDir, fileName), overwrite: true);

            AppConfig.WallpaperFile = fileName;
            WallpaperFileName = fileName;
            OnPropertyChanged(nameof(AccentFromWallpaperVisibility));
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
        }
        catch
        {
        }
    }


    [RelayCommand]
    private void RemoveWallpaper()
    {
        AppConfig.WallpaperFile = null;
        WallpaperFileName = Lang.Starshot_WallpaperNone;
        OnPropertyChanged(nameof(AccentFromWallpaperVisibility));
        WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
    }


    [RelayCommand]
    private async Task OpenWallpaperFolder()
    {
        try
        {
            string bgDir = Path.Combine(AppConfig.CacheFolder, "bg");
            Directory.CreateDirectory(bgDir);
            await Launcher.LaunchFolderPathAsync(bgDir);
        }
        catch
        {
        }
    }


    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }

}
