using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

namespace Starshot.Features.Update;

[INotifyPropertyChanged]
public sealed partial class UpdateWindow : WindowEx
{
    private ReleaseInfo? _release;
    private CancellationTokenSource? _cts;
    private bool _userClosed;


    public string CurrentVersionText { get; set => SetProperty(ref field, value); } = "";
    public string ArchitectureText { get; set => SetProperty(ref field, value); } = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
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
        // 安装版线（kachina）：更新由外部更新器接管，无差分/全量之分，主按钮换成单按钮
        if (AppConfig.Installer)
        {
            Button_Update.Visibility = Visibility.Collapsed;
            Button_UpdateInstaller.Visibility = Visibility.Visible;
        }
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        // Labs MarkdownTextBlock 只认系统主题（不看 app 主题），窗口也跟随系统保持一致
        RootGrid.RequestedTheme = ElementTheme.Default;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AdaptTitleBarButtonColorToActuallTheme();
        CenterInScreen(1000, 680);
        this.Closed += (_, _) =>
        {
            if (_cts is null) return; // 没在下载，正常关闭不提示
            _userClosed = true;
            _cts?.Cancel();
            // 马上提示（不等 StartUpdateAsync 的 catch 链走完）
            InAppToast.MainWindow?.Warning(null, Lang.Starshot_UpdateFailed, 5000);
        };
    }


    public void SetRelease(ReleaseInfo release)
    {
        _release = release;
        CurrentVersionText = AppConfig.AppVersion;
        NewVersionText = release.TagName;
        ChannelText = release.Prerelease ? "Preview" : "Stable";
        BuildTimeText = release.PublishedAt == default ? "-" : release.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        ReleaseNotes = string.IsNullOrWhiteSpace(release.Notes) ? "" : release.Notes;
        Activate();
        // CDN 模式 Notes 空（检查更新时没拿 body 避免阻塞）；弹出后异步加载，在乎的直接更新不等
        if (string.IsNullOrWhiteSpace(release.Notes))
        {
            _ = LoadReleaseNotesAsync();
        }
    }


    private async Task LoadReleaseNotesAsync()
    {
        if (_release is null) return;
        try
        {
            var body = await ReleaseClient.GetGitHubReleaseBodyAsync(_release.TagName, default);
            ReleaseNotes = string.IsNullOrWhiteSpace(body)
                ? "Release notes will be available once the update is published."
                : body;
        }
        catch
        {
            ReleaseNotes = "Release notes will be available once the update is published.";
        }
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
    private Task UpdateNow() => RunUpdateAsync(forceFull: false);

    [RelayCommand]
    private Task UpdateFull() => RunUpdateAsync(forceFull: true);

    private async Task RunUpdateAsync(bool forceFull)
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
            // 多层 delta：bytesText 格式 "1/3  2MB / 5MB"，百分比位置显示层号
            var sep = p.bytesText.IndexOf("  ");
            if (sep > 0 && p.bytesText.Contains('/'))
            {
                ProgressPercentText = p.bytesText[..sep];
                var size = p.bytesText[(sep + 2)..];
                // 层切换过渡帧（空大小）：保留上一层最后的大小直到本层数据到达，避免"消失再出现"的空档
                if (size.Length > 0) ProgressBytesText = size;
            }
            else
            {
                ProgressPercentText = p.percent + "%";
                ProgressBytesText = p.bytesText;
            }
        });
        try
        {
            await UpdateService.StartUpdateAsync(_release, progress, _cts.Token, forceFull: forceFull);
        }
        catch (Exception ex)
        {
            IsProgressVisible = Visibility.Collapsed;
            ErrorMessage = Lang.Starshot_UpdateFailed;
            HasError = Visibility.Visible;
            Button_Update.IsEnabled = true;
            Button_Remind.IsEnabled = true;
            if (!_userClosed) InAppToast.MainWindow?.Error(ex, Lang.Starshot_UpdateFailed);
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
