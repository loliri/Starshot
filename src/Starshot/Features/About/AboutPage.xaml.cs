using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Navigation;
using Starshot.Frameworks;
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

}
