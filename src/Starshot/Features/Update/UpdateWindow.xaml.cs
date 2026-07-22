using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

namespace Starshot.Features.Update;

[INotifyPropertyChanged]
public sealed partial class UpdateWindow : WindowEx
{
    private ReleaseInfo? _release;
    private CancellationTokenSource? _cts;


    public string CurrentVersionText { get; set => SetProperty(ref field, value); } = "";
    public string NewVersionText { get; set => SetProperty(ref field, value); } = "";
    public string ReleaseNotes { get; set => SetProperty(ref field, value); } = "";
    public string ChannelText { get; set => SetProperty(ref field, value); } = "";
    public string BuildTimeText { get; set => SetProperty(ref field, value); } = "";
    public string ProgressBytesText { get; set => SetProperty(ref field, value); } = "";
    public string ProgressPercentText { get; set => SetProperty(ref field, value); } = "";
    public double ProgressValue { get; set => SetProperty(ref field, value); }
    public Visibility IsProgressVisible { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;
    public string ErrorMessage { get; set => SetProperty(ref field, value); } = "";
    public Visibility HasError { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;


    public UpdateWindow()
    {
        InitializeComponent();
        Title = "Starshot";
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        // 锁死深色：Labs MarkdownTextBlock 代码块默认浅色字，深色背景才看得见
        RootGrid.RequestedTheme = ElementTheme.Dark;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AdaptTitleBarButtonColorToActuallTheme();
        CenterInScreen(1000, 680);
        this.Closed += (_, _) => _cts?.Cancel();
    }


    public void SetRelease(ReleaseInfo release)
    {
        _release = release;
        CurrentVersionText = AppConfig.AppVersion;
        NewVersionText = release.TagName;
        ChannelText = release.Prerelease ? "Preview" : "Stable";
        BuildTimeText = release.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        ReleaseNotes = string.IsNullOrWhiteSpace(release.Notes) ? "" : release.Notes;
        Activate();
    }


    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (_release is null) return;
        string? tag = (sender as FrameworkElement)?.Tag?.ToString();
        string? url = tag switch
        {
            "release" => $"https://github.com/loliri/Starshot/releases/tag/{_release.TagName}",
            "package" => _release.ZipUrl,
            _ => null,
        };
        if (!string.IsNullOrEmpty(url))
            _ = Launcher.LaunchUriAsync(new Uri(url));
    }


    [RelayCommand]
    private async Task UpdateNow()
    {
        if (_release is null) return;
        Button_Update.IsEnabled = false;
        Button_Remind.IsEnabled = false;
        IsProgressVisible = Visibility.Visible;
        HasError = Visibility.Collapsed;
        ProgressValue = 0;

        _cts = new CancellationTokenSource();
        var progress = new Progress<(int percent, string bytesText)>(p =>
        {
            ProgressValue = p.percent;
            ProgressPercentText = p.percent + "%";
            ProgressBytesText = p.bytesText;
        });
        try
        {
            await UpdateService.StartUpdateAsync(_release, progress, _cts.Token);
        }
        catch (Exception ex)
        {
            IsProgressVisible = Visibility.Collapsed;
            ErrorMessage = Lang.Starshot_UpdateFailed;
            HasError = Visibility.Visible;
            Button_Update.IsEnabled = true;
            Button_Remind.IsEnabled = true;
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_UpdateFailed);
        }
    }


    [RelayCommand]
    private void RemindLater()
    {
        _cts?.Cancel();
        Close();
    }


    [RelayCommand]
    private void Ignore()
    {
        if (_release is not null) AppConfig.IgnoreVersion = _release.Version.ToString();
        Close();
    }
}
