using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Starshot.Features.Screenshot;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;
using Windows.Graphics;
using Windows.Graphics.Capture;

namespace Starshot.Features.ViewHost;

[ObservableObject]
public sealed partial class WelcomeWindow : WindowEx
{
    private TaskCompletionSource<bool> _tcs;

    private static readonly (string, string)[] WallpaperFilters =
    {
        ("Image", ".jpg"),
        ("Image", ".jpeg"),
        ("Image", ".png"),
        ("Image", ".bmp"),
        ("Image", ".webp"),
        ("Video", ".mp4"),
        ("Video", ".mkv"),
        ("Video", ".mov"),
        ("Video", ".avi"),
        ("Video", ".webm"),
    };

    // 欢迎页选的配置：暂存，不直接写 AppConfig（DB 还没创建），CheckEnviromentAsync 在 SetDatabase 后读
    public string? WallpaperFileName { get; private set; }
    public string? WallpaperVideoPath { get; private set; }
    public bool WallpaperIsVideo { get; private set; }

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
    public Visibility DxgiSupported
    {
        get;
        set => SetProperty(ref field, value);
    } = Visibility.Collapsed;

    public Visibility DxgiNotSupported
    {
        get;
        set => SetProperty(ref field, value);
    } = Visibility.Collapsed;

    // 选完显示选中路径
    public string WallpaperDisplay
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    // 分发线判定：与 AppConfig.Installer 同源（exe 内烙印标志），不再单独探测包内文件
    private bool IsInstallerLine => AppConfig.Installer;

    public string EditionName =>
        IsInstallerLine ? Lang.Starshot_EditionInstaller : Lang.Starshot_EditionPortable;

    public string DbFolderDescTail =>
        IsInstallerLine
            ? Lang.Starshot_WelcomeDbDescInstaller
            : Lang.Starshot_WelcomeDbDescPortable;

    private void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 只查 API 可用性：真截一张的实测判定有误判风险，API 检测是确定性结论
            DxgiSupported = GraphicsCaptureSession.IsSupported()
                ? Visibility.Visible
                : Visibility.Collapsed;
            DxgiNotSupported = DxgiSupported is Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
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
            string? path = await FileDialogHelper.PickSingleFileAsync(
                this.XamlRoot,
                WallpaperFilters
            );
            if (string.IsNullOrWhiteSpace(path))
                return;

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
    private void Start()
    {
        _tcs.SetResult(true);
        Close();
    }
}
