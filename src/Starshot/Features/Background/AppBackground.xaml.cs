using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starshot.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Starshot.Features.Background;

/// <summary>
/// 自定义壁纸（图片/视频）渲染层。移植自 Starward AppBackground，剥除游戏耦合。
/// 图片：CanvasBitmap → B8G8R8A8 RenderTarget → CanvasImageSource 显示 + GetPixelBytes 取色。
/// 视频：MediaPlayer frame-server（静音循环）→ CopyFrameToVideoSurface → CanvasImageSource。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class AppBackground : UserControl
{

    private readonly ILogger<AppBackground> _logger = AppConfig.GetLogger<AppBackground>();


    public ImageSource? BackgroundImageSource { get; set => SetProperty(ref field, value); }

    public bool IsUpdateBackgroundRunning { get; set => SetProperty(ref field, value); }


    private string? _lastFile;
    private CancellationTokenSource? _cts;


    // ===== 视频 =====
    private MediaPlayer? _mediaPlayer;
    private int _mediaPlayerRetryCount;  // 管线卡死重建次数，首帧到则清零
    private CanvasRenderTarget? _videoSurface;
    private CanvasImageSource? _videoImageSource;
    private readonly SemaphoreSlim _videoSemaphore = new(1, 1);
    private bool _videoAccentExtracted;  // 视频首帧取色标志（取一次，避免每帧取导致强调色乱跳）


    public AppBackground()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<BackgroundChangedMessage>(this, (_, _) => _ = UpdateBackgroundAsync());
        WeakReferenceMessenger.Default.Register<AccentRefreshRequestedMessage>(this, (_, _) => _ = RefreshAccentAsync());
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnWindowStateChanged);
        Loaded += (_, _) => _ = UpdateBackgroundAsync();
        Unloaded += (_, _) =>
        {
            DisposeVideoResource();
            WeakReferenceMessenger.Default.UnregisterAll(this);
        };
    }


    /// <summary>
    /// 窗口隐藏 → 暂停视频壁纸；激活 → 续播。避免不可见时占 GPU。
    /// </summary>
    private void OnWindowStateChanged(object _, MainWindowStateChangedMessage m)
    {
        if (_mediaPlayer is null)
        {
            return;
        }
        try
        {
            var state = _mediaPlayer.PlaybackSession.PlaybackState;
            if (m.Hide)
            {
                _mediaPlayer.Pause();
            }
            else if (m.Activate && state is not MediaPlaybackState.Playing)
            {
                _mediaPlayer.Play();
            }
        }
        catch { }
    }


    public async Task UpdateBackgroundAsync()
    {
        try
        {
            IsUpdateBackgroundRunning = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            // 启动/刷新时检查源是否丢失（区分"未配置"和"配置了但文件没了"）
            if (IsWallpaperSourceMissing())
            {
                ClearWallpaperConfigForCurrentMode();
                AppConfig.WallpaperMode = 0;
                InAppToast.MainWindow?.Warning(null, Lang.Starshot_WallpaperNotFound, 5000);
                DisposeVideoResource();
                BackgroundImageSource = null;
                _lastFile = null;
                return;
            }

            var (file, fellBackToImage) = ResolveWallpaperPath();
            if (!AppConfig.EnableWallpaper || string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                DisposeVideoResource();
                BackgroundImageSource = null;
                _lastFile = null;
                return;
            }
            if (fellBackToImage)
            {
                InAppToast.MainWindow?.Warning(null, Lang.Starshot_WallpaperVideoFallbackToImage, 5000);
            }
            if (file == _lastFile)
            {
                return;
            }

            DisposeVideoResource();
            BackgroundImageSource = null;

            _logger.LogInformation("UpdateBackground file={File} isVideo={IsVideo} fellBack={FellBack} lastFile={LastFile}", file, IsSupportedVideo(file), fellBackToImage, _lastFile);
            if (IsSupportedVideo(file))
            {
                StartMediaPlayer(file);
            }
            else
            {
                await ChangeBackgroundImageAsync(file, ct);
            }
            _lastFile = file;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateBackgroundAsync");
        }
        finally
        {
            IsUpdateBackgroundRunning = false;
        }
    }


    private static readonly HashSet<string> WallpaperMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif",
        ".mp4", ".mkv", ".mov", ".avi", ".webm",
    };

    private static readonly HashSet<string> WallpaperVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm",
    };


    /// <summary>
    /// 按模式解析当前要加载的壁纸文件路径，并指示是否发生了"仅视频无视频→回退图片"。
    /// 模式 0=无；1：CacheFolder/bg/WallpaperFile（复制件）；2：WallpaperVideoFile（读源）；
    /// 3：枚举 WallpaperFolder 随机抽一个图/视频（读源，混合）；仅视频开关开且无视频时回退到图片。模式 2/3 不复制。
    /// </summary>
    private static (string? path, bool fellBackToImage) ResolveWallpaperPath()
    {
        return AppConfig.WallpaperMode switch
        {
            1 => (AppConfig.WallpaperFile is { Length: > 0 } f ? Path.Combine(AppConfig.CacheFolder, "bg", f) : null, false),
            2 => (string.IsNullOrWhiteSpace(AppConfig.WallpaperVideoFile) ? null : AppConfig.WallpaperVideoFile, false),
            3 => PickRandomFromFolder(AppConfig.WallpaperFolder),
            _ => (null, false),
        };
    }


    private static (string? path, bool fellBackToImage) PickRandomFromFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return (null, false);
        }
        try
        {
            var all = Directory.EnumerateFiles(folder)
                .Where(f => WallpaperMediaExtensions.Contains(Path.GetExtension(f)))
                .ToList();
            if (all.Count == 0) return (null, false);

            List<string> candidates;
            bool fellBack = false;
            if (AppConfig.WallpaperFolderVideoOnly)
            {
                var videos = all.Where(f => WallpaperVideoExtensions.Contains(Path.GetExtension(f))).ToList();
                if (videos.Count > 0)
                {
                    candidates = videos;
                }
                else
                {
                    // 仅视频但无视频 → 回退到图片（由调用方弹 warning）
                    candidates = all.Where(f => !WallpaperVideoExtensions.Contains(Path.GetExtension(f))).ToList();
                    if (candidates.Count == 0) return (null, false);
                    fellBack = true;
                }
            }
            else
            {
                candidates = all;
            }
            return (candidates[Random.Shared.Next(candidates.Count)], fellBack);
        }
        catch
        {
            return (null, false);
        }
    }


    private static bool IsSupportedVideo(string file)
    {
        string? ext = Path.GetExtension(file)?.ToLowerInvariant();
        return ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm";
    }


    /// <summary>
    /// 检查当前模式的壁纸源是否存在（配置了但文件/文件夹丢失）。
    /// </summary>
    private static bool IsWallpaperSourceMissing()
    {
        return AppConfig.WallpaperMode switch
        {
            1 => !string.IsNullOrWhiteSpace(AppConfig.WallpaperFile)
                 && !File.Exists(Path.Combine(AppConfig.CacheFolder, "bg", AppConfig.WallpaperFile)),
            2 => !string.IsNullOrWhiteSpace(AppConfig.WallpaperVideoFile)
                 && !File.Exists(AppConfig.WallpaperVideoFile),
            3 => !string.IsNullOrWhiteSpace(AppConfig.WallpaperFolder)
                 && !Directory.Exists(AppConfig.WallpaperFolder),
            _ => false,
        };
    }


    /// <summary>
    /// 清空当前模式的壁纸配置项（文件/文件夹路径置 null）。
    /// </summary>
    private static void ClearWallpaperConfigForCurrentMode()
    {
        switch (AppConfig.WallpaperMode)
        {
            case 1: AppConfig.WallpaperFile = null; break;
            case 2: AppConfig.WallpaperVideoFile = null; break;
            case 3: AppConfig.WallpaperFolder = null; break;
        }
    }


    private async Task ChangeBackgroundImageAsync(string file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var device = CanvasDevice.GetSharedDevice();
        using var fs = File.OpenRead(file);
        using var bitmap = await CanvasBitmap.LoadAsync(device, fs.AsRandomAccessStream(), 96);
        int w = (int)bitmap.SizeInPixels.Width;
        int h = (int)bitmap.SizeInPixels.Height;

        // 统一转 B8G8R8A8：取色按 BGRA 字节序，显示也复用
        using var bgra = new CanvasRenderTarget(device, w, h, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var ds = bgra.CreateDrawingSession())
        {
            ds.DrawImage(bitmap);
        }
        ct.ThrowIfCancellationRequested();

        var src = new CanvasImageSource(device, w, h, 96);
        using (var ds2 = src.CreateDrawingSession(Colors.Transparent))
        {
            ds2.DrawImage(bgra);
        }
        BackgroundImageSource = src;

        await ExtractAccentAsync(bgra, w, h);
    }


    /// <summary>
    /// 从 BGRA 位图提取主色应用为强调色（开关关则跳过）。
    /// </summary>
    private async Task ExtractAccentAsync(CanvasRenderTarget bgra, int w, int h)
    {
        if (!AppConfig.EnableAccentFromWallpaper)
        {
            return;
        }
        try
        {
            byte[] bytes = bgra.GetPixelBytes();
            var color = await Task.Run(() => AccentColorHelper.GetAccentColor(bytes, w, h));
            if (color is not null)
            {
                AccentColorHelper.ChangeAppAccentColor(color);
                AppConfig.AccentColor = color?.ToHex();
                // ChangeAppAccentColor 已发 AccentColorChangedMessage → MainWindow 会重解析主题
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Accent from wallpaper");
        }
    }


    /// <summary>
    /// 重新从当前壁纸取色（只解码取色，不动显示/视频）。从壁纸取色开关打开时调用。
    /// </summary>
    public async Task RefreshAccentAsync()
    {
        string? file = ResolveWallpaperPath().path;
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            return;
        }
        // 视频：重置首帧取色标志，下一帧到达时重新取色（解决切换模式到视频时 _lastFile 短路不重载的问题）
        if (IsSupportedVideo(file))
        {
            _videoAccentExtracted = false;
            return;
        }
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            using var fs = File.OpenRead(file);
            using var bitmap = await CanvasBitmap.LoadAsync(device, fs.AsRandomAccessStream(), 96);
            int w = (int)bitmap.SizeInPixels.Width;
            int h = (int)bitmap.SizeInPixels.Height;
            using var bgra = new CanvasRenderTarget(device, w, h, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
            using (var ds = bgra.CreateDrawingSession())
            {
                ds.DrawImage(bitmap);
            }
            await ExtractAccentAsync(bgra, w, h);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RefreshAccentAsync");
        }
    }


    private void StartMediaPlayer(string file)
    {
        _logger.LogInformation("StartMediaPlayer {File}", file);
        _mediaPlayer = new MediaPlayer
        {
            IsLoopingEnabled = true,
            IsMuted = true,
            Source = MediaSource.CreateFromUri(new Uri(file)),
        };
        _mediaPlayer.CommandManager.IsEnabled = false;
        _mediaPlayer.SystemMediaTransportControls.IsEnabled = false;
        _mediaPlayer.VideoFrameAvailable += MediaPlayer_VideoFrameAvailable;
        _mediaPlayer.MediaFailed += (_, a) => _logger.LogError(a.ExtendedErrorCode, "MediaPlayer failed");
        _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
        _videoAccentExtracted = false;  // 新视频重置取色标志
        // 不在此 Play：等 MediaOpened（Source 解析完成、NaturalVideoWidth/Height 可用）再进 frame server + Play，
        // 规避启动期 MF 管线（demux/decode/首帧）未就绪时 VideoFrameAvailable 间歇不触发的竞争
    }


    private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        _logger.LogInformation("MediaOpened, enter frame server + Play");
        sender.IsVideoFrameServerEnabled = true;
        sender.Play();
        // MediaOpened 后 2s 仍无首帧 = 管线卡死，重建 MediaPlayer（重 Play 幂等唤不醒损坏实例，重建才稳；最多 2 次防死循环）
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (_mediaPlayer == sender && _videoImageSource is null && _mediaPlayerRetryCount < 2)
                {
                    _mediaPlayerRetryCount++;
                    _logger.LogWarning("No first frame 2s after MediaOpened, rebuild MediaPlayer (retry {Retry})", _mediaPlayerRetryCount);
                    var f = _lastFile;
                    if (f is not null)
                    {
                        DisposeVideoResource();
                        StartMediaPlayer(f);
                    }
                }
            });
        });
    }


    private void MediaPlayer_VideoFrameAvailable(MediaPlayer sender, object args)
    {
        if (_videoSemaphore.CurrentCount == 0)
        {
            return;
        }
        _videoSemaphore.Wait();
        DispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                if (_videoSurface is null || _videoImageSource is null)
                {
                    _videoSurface?.Dispose();
                    int w = (int)sender.PlaybackSession.NaturalVideoWidth;
                    int h = (int)sender.PlaybackSession.NaturalVideoHeight;
                    _videoSurface = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), w, h, 96);
                    _videoImageSource = new CanvasImageSource(CanvasDevice.GetSharedDevice(), w, h, 96);
                    BackgroundImageSource = _videoImageSource;
                    _mediaPlayerRetryCount = 0;
                    _logger.LogInformation("VideoFrameAvailable first frame {W}x{H}", w, h);
                }
                sender.CopyFrameToVideoSurface(_videoSurface);
                using var ds = _videoImageSource.CreateDrawingSession(Colors.Transparent);
                ds.DrawImage(_videoSurface);
                // 视频首帧取一次色（自动取色开时）。不每帧取，否则强调色随帧乱跳。
                if (!_videoAccentExtracted && AppConfig.EnableAccentFromWallpaper)
                {
                    _videoAccentExtracted = true;
                    try
                    {
                        var color = AccentColorHelper.GetAccentColor(_videoSurface.GetPixelBytes(),
                            (int)_videoSurface.SizeInPixels.Width, (int)_videoSurface.SizeInPixels.Height);
                        if (color is not null)
                        {
                            AccentColorHelper.ChangeAppAccentColor(color);
                            AppConfig.AccentColor = color?.ToHex();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Accent from video first frame");
                    }
                }
            }
            catch { }
            finally
            {
                _videoSemaphore.Release();
            }
        });
    }


    private void DisposeVideoResource()
    {
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
        _videoSurface?.Dispose();
        _videoSurface = null;
        _videoImageSource = null;
    }

}
