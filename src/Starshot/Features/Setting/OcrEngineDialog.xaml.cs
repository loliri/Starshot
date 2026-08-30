using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starshot.Features.Update;
using Starshot.Helpers;
using Starshot.Language;

namespace Starshot.Features.Setting;

[INotifyPropertyChanged]
public sealed partial class OcrEngineDialog : ContentDialog
{
    private readonly ILogger<OcrEngineDialog> _logger = AppConfig.GetLogger<OcrEngineDialog>();

    private CancellationTokenSource? _cts;

    public OcrEngineDialog()
    {
        InitializeComponent();
        RefreshState();
    }

    public int EngineIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.OcrEngine = value;
            }
        }
    } = AppConfig.OcrEngine;

    public bool IsReady
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            OnPropertyChanged(nameof(IsNotReady));
        }
    }

    /// <summary>未就绪且不在获取中（红字 + 获取按钮面板的显隐）。</summary>
    public bool IsNotReady => !IsReady && !IsBusy;

    public bool IsBusy
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            OnPropertyChanged(nameof(IsNotReady));
        }
    }

    public string ReadySizeText
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public double ProgressPercent
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsIndeterminate
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            OnPropertyChanged(nameof(ShowCancel));
        }
    }

    /// <summary>取消按钮仅对 CDN 下载（可取消）显示；本机 File.Copy 不可取消。</summary>
    public bool ShowCancel => IsBusy && !IsIndeterminate;

    public string ProgressText
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string StatusMessage
    {
        get;
        private set => SetProperty(ref field, value);
    }

    private void RefreshState()
    {
        IsReady = OcrHelper.IsOneOcrReady;
        ReadySizeText = IsReady
            ? $"{Lang.OcrEngineDialog_Ready} · {FormatSize(GetEngineFileSize())}"
            : "";
    }

    private long GetEngineFileSize()
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            return new FileInfo(Path.Combine(dir, "oneocr.dll")).Length
                + new FileInfo(Path.Combine(dir, "oneocr.onemodel")).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        const double MB = 1 << 20;
        return $"{bytes / MB:F1} MB";
    }

    /// <summary>
    /// 本机 oneocr 文件目录：优先 Windows 截图工具（SnippingTool 子目录），没有回退照片应用（包根目录平铺）。
    /// 都拿不到返回 null。
    /// </summary>
    private static string? TryGetLocalOcrDir()
    {
        // (包 family name, 包内相对目录)
        (string Family, string SubDir)[] candidates =
        [
            ("Microsoft.ScreenSketch_8wekyb3d8bbwe", "SnippingTool"),
            ("Microsoft.Windows.Photos_8wekyb3d8bbwe", ""),
        ];
        foreach (var (family, subDir) in candidates)
        {
            string? dir = TryGetPackageOcrDir(family, subDir);
            if (dir is not null)
                return dir;
        }
        return null;
    }

    private static string? TryGetPackageOcrDir(string familyName, string subDir)
    {
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            var pkg =
                pm.FindPackagesForUser(string.Empty, familyName).FirstOrDefault()
                ?? pm.FindPackages(familyName).FirstOrDefault();
            if (pkg is null)
                return null;
            string? installDir = null;
            try
            {
                installDir = pkg.InstalledLocation.Path;
            }
            catch
            {
                // 未安装位置的包可能抛（已卸载残项）
            }
            if (string.IsNullOrEmpty(installDir))
                return null;
            string dir = Path.Combine(installDir, subDir);
            return
                File.Exists(Path.Combine(dir, "oneocr.dll"))
                && File.Exists(Path.Combine(dir, "oneocr.onemodel"))
                ? dir
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async void Button_FromLocal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? src = TryGetLocalOcrDir();
            if (src is null)
            {
                StatusMessage = Lang.OcrEngineDialog_LocalMissing;
                return;
            }
            IsBusy = true;
            IsIndeterminate = true;
            ProgressText = "";
            StatusMessage = "";
            string dir = AppContext.BaseDirectory;
            await Task.Run(() =>
            {
                File.Copy(
                    Path.Combine(src, "oneocr.dll"),
                    Path.Combine(dir, "oneocr.dll"),
                    overwrite: true
                );
                File.Copy(
                    Path.Combine(src, "oneocr.onemodel"),
                    Path.Combine(dir, "oneocr.onemodel"),
                    overwrite: true
                );
            });
            OcrHelper.ResetEngineCache();
            IsBusy = false;
            RefreshState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // oneocr.dll 一旦 P/Invoke 加载，进程内无法卸载，覆盖目标被锁
            _logger.LogError(ex, "OCR engine copy from local package failed (locked)");
            IsBusy = false;
            RefreshState();
            StatusMessage = Lang.OcrEngineDialog_UpdateNeedRestart;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR engine copy from local package failed");
            IsBusy = false;
            RefreshState();
            StatusMessage = ex.Message;
        }
    }

    private async void Button_FromCdn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IsBusy = true;
            IsIndeterminate = false;
            ProgressPercent = 0;
            ProgressText = "";
            StatusMessage = "";
            _cts = new CancellationTokenSource();
            var progress = new Progress<(int percent, string bytesText)>(p =>
            {
                ProgressPercent = p.percent;
                ProgressText = p.bytesText;
            });
            await UpdateService.ExtractToDirectoryAsync(
                AppConfig.OcrCdnUrl,
                AppContext.BaseDirectory,
                progress,
                _cts.Token
            );
            OcrHelper.ResetEngineCache();
            IsBusy = false;
            RefreshState();
        }
        catch (OperationCanceledException)
        {
            IsBusy = false;
            RefreshState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR engine download failed");
            IsBusy = false;
            RefreshState();
            StatusMessage = $"{Lang.OcrEngineDialog_DownloadFailed}: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Button_Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void Button_Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            File.Delete(Path.Combine(dir, "oneocr.dll"));
            File.Delete(Path.Combine(dir, "oneocr.onemodel"));
            OcrHelper.ResetEngineCache();
            RefreshState();
        }
        catch (Exception ex)
        {
            // oneocr.dll 一旦 P/Invoke 加载，进程内无法卸载，文件被锁
            _logger.LogError(ex, "OCR engine delete failed");
            StatusMessage = Lang.OcrEngineDialog_DeleteNeedRestart;
        }
    }

    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Hide();
    }
}
