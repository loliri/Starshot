using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using Starshot.Features.Update;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;
using System;
using System.Threading.Tasks;
using Windows.System;

namespace Starshot.Features.About;

public sealed partial class AboutPage : PageBase
{

    public string Version { get; set; } =
#if DEBUG
        "Debug";
#else
        // 安装版线（kachina）带 Installer 后缀，两条线在关于页可辨
        $"Release {AppConfig.AppVersion}{(AppConfig.Installer ? " Installer" : "")}";
#endif


    /// <summary>
    /// 检查更新时是否包含预发布版本（代理 AppConfig.EnablePreReleaseUpdateCheck）。
    /// </summary>
    public bool PreReleaseCheck
    {
        get => AppConfig.EnablePreReleaseUpdateCheck;
        set => AppConfig.EnablePreReleaseUpdateCheck = value;
    }


    public AboutPage()
    {
        InitializeComponent();
#if DEBUG
        // DEBUG 不查更新，隐藏按钮和更新相关开关（CheckUpdateAsync 直接 return null，显示「最新」是假的）
        CheckUpdateButton.Visibility = Visibility.Collapsed;
        PreReleaseSwitch.Visibility = Visibility.Collapsed;
#endif
        // 安装版线（kachina）当前只走 stable 渠道，预览开关会误导（检查到预览版但更新器按 hash 只认 stable）；
        // devmode 开发者保留（调双渠道用）
        if (AppConfig.Installer && !AppConfig.DevMode)
        {
            PreReleaseSwitch.Visibility = Visibility.Collapsed;
        }
    }


    private int _logoTaps;
    private DateTime _lastLogoTap = DateTime.MinValue;


    /// <summary>
    /// logo 点 5 次开启开发者模式（2.5 秒内连点算连续）。只开不关：已开启时只提示，关闭走设置页开关。
    /// </summary>
    private void Button_Logo_Click(object sender, RoutedEventArgs e)
    {
        if ((DateTime.Now - _lastLogoTap).TotalSeconds > 2.5)
        {
            _logoTaps = 0;
        }
        _lastLogoTap = DateTime.Now;
        _logoTaps++;
        if (_logoTaps < 5) return;
        _logoTaps = 0;
        AppConfig.DevMode = true;
        InAppToast.MainWindow?.Information(Lang.Starshot_DevModeOn, null, 3000);
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }


    [RelayCommand]
    private async Task CheckUpdate()
    {
        try
        {
            var (release, tag) = await UpdateService.CheckUpdateAsync(ignoreSkipped: false);
            if (release is null)
            {
                // 显示 GitHub 最新版号（不是当前版本号——当前可能比 GitHub 还新）
                var t = tag ?? AppConfig.AppVersion;
                InAppToast.MainWindow?.Information(null, Lang.Starshot_LatestVersion, 3000, t);
                return;
            }
            var window = new UpdateWindow();
            window.SetRelease(release);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AboutPage.CheckUpdate failed");
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_UpdateFailed);
        }
    }

}
