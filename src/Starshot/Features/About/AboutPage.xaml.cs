using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
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


    public AboutPage()
    {
        InitializeComponent();
#if DEBUG
        // DEBUG 不查更新，隐藏按钮（CheckUpdateAsync 直接 return null，显示「最新」是假的）
        CheckUpdateButton.Visibility = Visibility.Collapsed;
#endif
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }


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
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_UpdateFailed, 5000);
        }
    }

}
