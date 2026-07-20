using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics;
using Starshot.Features.Screenshot;

namespace Starshot.Features.ViewHost;

[ObservableObject]
public sealed partial class WelcomeWindow : WindowEx
{
    private TaskCompletionSource<bool> _tcs;

    private static readonly (string, string)[] WallpaperFilters = {
        ("Image", ".jpg"), ("Image", ".jpeg"), ("Image", ".png"), ("Image", ".bmp"), ("Image", ".webp"),
        ("Video", ".mp4"), ("Video", ".mkv"), ("Video", ".mov"), ("Video", ".avi"), ("Video", ".webm"),
    };

    // 欢迎页选的配置：暂存，不直接写 AppConfig（DB 还没创建），CheckEnviromentAsync 在 SetDatabase 后读
    public string? WallpaperFileName { get; private set; }
    public string? WallpaperVideoPath { get; private set; }
    public bool WallpaperIsVideo { get; private set; }
    public string? ScreenshotFolderPath { get; private set; }


    public WelcomeWindow()
    {
        InitializeComponent();
        InitializeWindow();
        _tcs = new();
    }


    private void InitializeWindow()
    {
        this.Closed += (_, _) => _tcs.TrySetResult(false);
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        CenterInScreen(1020, 630);
        AdaptTitleBarButtonColorToActuallTheme();
        SetDragRectangles(new RectInt32(0, 0, 100000, (int)(48 * UIScale)));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsMaximizable = false;
            p.IsResizable = false;
        }
        new SystemBackdropHelper(this).TrySetAcrylic();
    }


    public async Task<bool> WaitAsync()
    {
        Activate();
        return await _tcs.Task;
    }


    // Window 没有 XamlRoot，包装 Content 的 XamlRoot 给 Picker 用
    public XamlRoot XamlRoot => (Content as FrameworkElement)?.XamlRoot!;


    // DXGI 支持检测结果（互补的 Visibility）
    public Visibility DxgiSupported { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;

    public Visibility DxgiNotSupported { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;

    // 选完显示选中路径
    public string WallpaperDisplay { get; set => SetProperty(ref field, value); } = "";

    public string ScreenshotFolderDisplay { get; set => SetProperty(ref field, value); } = "";


    private async void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        await Task.Delay(100);
        try
        {
            // 实际尝试截图：IsSupported 只查 API 是否存在，不查实际能否捕获
            //（不支持 DXGI 的机器接口在也返回 true），必须真截一次才知道
            var displays = Microsoft.UI.Windowing.DisplayArea.FindAll();
            var mainDisplay = displays.Count > 0 ? displays[0] : null;
            if (mainDisplay is null)
            {
                DxgiSupported = Visibility.Collapsed;
                DxgiNotSupported = Visibility.Visible;
                return;
            }
            using var bitmap = await ScreenCaptureHelper.CaptureMonitorAsync((nint)mainDisplay.DisplayId.Value, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, default);
            if (bitmap is null || bitmap.SizeInPixels.Width == 0 || bitmap.SizeInPixels.Height == 0)
            {
                DxgiSupported = Visibility.Collapsed;
                DxgiNotSupported = Visibility.Visible;
                return;
            }
            DxgiSupported = Visibility.Visible;
            DxgiNotSupported = Visibility.Collapsed;
        }
        catch
        {
            DxgiSupported = Visibility.Collapsed;
            DxgiNotSupported = Visibility.Visible;
        }
    }


    [RelayCommand]
    private async Task PickWallpaper()
    {
        try
        {
            string? path = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, WallpaperFilters);
            if (string.IsNullOrWhiteSpace(path)) return;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            WallpaperIsVideo = ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm";

            if (WallpaperIsVideo)
            {
                WallpaperVideoPath = path;
                WallpaperDisplay = Path.GetFileName(path);
            }
            else
            {
                // 图片拷到 cache/bg，文件名暂存（SetDatabase 后 CheckEnviromentAsync 写 AppConfig）
                string fileName = Path.GetFileName(path);
                string bgDir = Path.Combine(AppConfig.CacheFolder, "bg");
                Directory.CreateDirectory(bgDir);
                File.Copy(path, Path.Combine(bgDir, fileName), overwrite: true);
                WallpaperFileName = fileName;
                WallpaperDisplay = fileName;
            }
        }
        catch { }
    }


    [RelayCommand]
    private async Task PickScreenshotFolder()
    {
        try
        {
            var folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                ScreenshotFolderPath = folder;
                ScreenshotFolderDisplay = folder;
            }
        }
        catch { }
    }


    [RelayCommand]
    private void Start()
    {
        _tcs.SetResult(true);
        Close();
    }
}
