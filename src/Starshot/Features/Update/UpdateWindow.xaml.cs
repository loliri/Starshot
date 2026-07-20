using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starshot.Features.Update;

[INotifyPropertyChanged]
public sealed partial class UpdateWindow : WindowEx
{
    private ReleaseInfo? _release;
    private CancellationTokenSource? _cts;


    public UpdateWindow()
    {
        InitializeComponent();
        this.Bindings.Update();
        Title = "Starshot";
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(680 * UIScale), (int)(420 * UIScale)));
        new SystemBackdropHelper(this).TrySetAcrylic();
        this.Closed += (_, _) => _cts?.Cancel();
    }


    public void SetRelease(ReleaseInfo release)
    {
        _release = release;
        TitleText.Text = Lang.Starshot_UpdateAvailableTitle;
        CurrentVersionText.Text = "v" + AppConfig.AppVersion;
        NewVersionText.Text = "v" + release.Version;
        Activate();
    }


    [RelayCommand]
    private async Task UpdateNow()
    {
        if (_release is null) return;
        UpdateButton.IsEnabled = false;
        RemindButton.IsEnabled = false;
        IgnoreButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;

        _cts = new CancellationTokenSource();
        var progress = new Progress<(int percent, string stage)>(p =>
        {
            Progress.Value = p.percent;
            StatusText.Text = $"{p.percent}%  {p.stage}";
        });
        try
        {
            await UpdateService.StartUpdateAsync(_release, progress, _cts.Token);
        }
        catch (Exception ex)
        {
            Progress.Visibility = Visibility.Collapsed;
            StatusText.Text = Lang.Starshot_UpdateFailed;
            UpdateButton.IsEnabled = true;
            RemindButton.IsEnabled = true;
            IgnoreButton.IsEnabled = true;
            InAppToast.MainWindow?.Error(ex, Lang.Starshot_UpdateFailed, 5000);
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
