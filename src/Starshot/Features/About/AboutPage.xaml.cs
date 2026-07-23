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
        $"Release {AppConfig.AppVersion}";
#endif


    /// <summary>
    /// 检查更新时是否包含预发布版本（代理 AppConfig.EnablePreReleaseUpdateCheck）。
    /// </summary>
    public bool PreReleaseCheck
    {
        get => AppConfig.EnablePreReleaseUpdateCheck;
        set => AppConfig.EnablePreReleaseUpdateCheck = value;
    }


    /// <summary>
    /// GitHub API 不走系统代理（代理 AppConfig.EnableGithubApiNoProxy）。仅 API，zip 下载走 CDN 不受影响。
    /// </summary>
    public bool GithubApiNoProxy
    {
        get => AppConfig.EnableGithubApiNoProxy;
        set
        {
            AppConfig.EnableGithubApiNoProxy = value;
            // _http 是 static，启动时读一次，改开关要重启才生效
            InAppToast.MainWindow?.Information(null, Lang.Starshot_RestartToTakeEffect, 3000);
        }
    }


    public AboutPage()
    {
        InitializeComponent();
#if DEBUG
        // DEBUG 不查更新，隐藏按钮和更新相关开关（CheckUpdateAsync 直接 return null，显示「最新」是假的）
        CheckUpdateButton.Visibility = Visibility.Collapsed;
        PreReleaseSwitch.Visibility = Visibility.Collapsed;
        GithubApiNoProxySwitch.Visibility = Visibility.Collapsed;
#endif
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
            var release = await UpdateService.CheckUpdateAsync(ignoreSkipped: false);
            if (release is null)
            {
                InAppToast.MainWindow?.Information(null, Lang.Starshot_LatestVersion, 3000);
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
