using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public ImageSource? BackgroundImageSource
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsUpdateBackgroundRunning
    {
        get;
        set => SetProperty(ref field, value);
    }

    private string? _lastFile;
    private CancellationTokenSource? _cts;

    // 在途的壁纸重载任务：RefreshAccentAsync 先等它收尾再读 _lastFile（末尾才更新），否则取到旧壁纸路径
    private Task _updateBackgroundTask = Task.CompletedTask;

    /// <summary>
    /// 文件夹随机模式下当前壁纸文件名（设置页打开时取初始值）。null = 未加载/非文件夹模式。
    /// </summary>
    public static string? CurrentWallpaperFileName { get; private set; }

    /// <summary>广播当前壁纸文件名变更（设置页 NowPlaying 跟随）。file 传 null = 清空。</summary>
    private static void ReportNowPlaying(string? file)
    {
        CurrentWallpaperFileName = file is null ? null : Path.GetFileName(file);
        WeakReferenceMessenger.Default.Send(
            new WallpaperNowPlayingChangedMessage { FileName = CurrentWallpaperFileName }
        );
    }

    // ===== 视频 =====
    private const int MediaOpenTimeoutMs = 5000;
    private const int FirstFrameTimeoutMs = 2000;
    private const int MediaPlayerMaxRetries = 2;

    private MediaPlayer? _mediaPlayer;
    private string? _currentVideoFile;
    private int _mediaPlayerRetryCount;
    private bool _mediaOpened;
    private bool _videoFramePresented;
    private CanvasRenderTarget? _videoSurface;
    private CanvasImageSource? _videoImageSource;
    private readonly SemaphoreSlim _videoSemaphore = new(1, 1);
    private bool _videoAccentExtracted; // 视频首帧取色标志（取一次，避免每帧取导致强调色乱跳）
    private bool _windowHidden;
    private bool _sessionLocked;

    public AppBackground()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<BackgroundChangedMessage>(
            this,
            (_, _) => _updateBackgroundTask = UpdateBackgroundAsync()
        );
        WeakReferenceMessenger.Default.Register<AccentRefreshRequestedMessage>(
            this,
            (_, _) => _ = RefreshAccentAsync()
        );
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(
            this,
            OnWindowStateChanged
        );
        Loaded += (_, _) => _updateBackgroundTask = UpdateBackgroundAsync();
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
        if (m.Hide)
        {
            _windowHidden = true;
        }
        else if (m.Activate)
        {
            _windowHidden = false;
        }

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
            else if (m.Activate && !_sessionLocked)
            {
                if (!_mediaOpened)
                {
                    _ = WatchMediaOpenAsync(_mediaPlayer);
                }
                else
                {
                    if (state is not MediaPlaybackState.Playing)
                    {
                        _mediaPlayer.Play();
                    }
                    if (!_videoFramePresented)
                    {
                        _ = WatchFirstFrameAsync(_mediaPlayer);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Change video playback for window state failed");
        }
    }

    private bool _pausedBySessionLocked;

    /// <summary>
    /// 锁屏暂停视频壁纸（省电：解码与 CopyFrameToVideoSurface 不再照跑）。
    /// sessionLock 标记暂停来源，只有因锁屏暂停的才在解锁时自动恢复。
    /// </summary>
    public void PauseVideo(bool sessionLock = false)
    {
        try
        {
            if (sessionLock)
            {
                _sessionLocked = true;
            }
            if (
                _mediaPlayer?.PlaybackSession?.PlaybackState
                is MediaPlaybackState.Playing
            )
            {
                _pausedBySessionLocked = sessionLock;
            }
            _mediaPlayer?.Pause();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pause video wallpaper failed");
        }
    }

    public void PlayVideo(bool sessionUnlock = false)
    {
        try
        {
            bool pausedBySessionLock = _pausedBySessionLocked;
            if (sessionUnlock)
            {
                _sessionLocked = false;
                _pausedBySessionLocked = false;
            }
            bool shouldResume =
                !sessionUnlock
                || pausedBySessionLock
                || (_mediaOpened && !_videoFramePresented);
            if (shouldResume && !_windowHidden && !_sessionLocked && _mediaPlayer is not null)
            {
                if (!_mediaOpened)
                {
                    _ = WatchMediaOpenAsync(_mediaPlayer);
                }
                else
                {
                    _mediaPlayer.Play();
                    if (!_videoFramePresented)
                    {
                        _ = WatchFirstFrameAsync(_mediaPlayer);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resume video wallpaper failed");
        }
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
                ReportNowPlaying(null);
                return;
            }

            var (file, fellBackToImage) = ResolveWallpaperPath();
            if (!AppConfig.EnableWallpaper || string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                DisposeVideoResource();
                BackgroundImageSource = null;
                _lastFile = null;
                ReportNowPlaying(null);
                return;
            }
            if (fellBackToImage)
            {
                InAppToast.MainWindow?.Warning(
                    null,
                    Lang.Starshot_WallpaperVideoFallbackToImage,
                    5000
                );
            }
            if (file == _lastFile)
            {
                return;
            }

            DisposeVideoResource();

            _logger.LogDebug(
                "UpdateBackground file={File} isVideo={IsVideo} fellBack={FellBack} lastFile={LastFile}",
                file,
                IsSupportedVideo(file),
                fellBackToImage,
                _lastFile
            );
            if (IsSupportedVideo(file))
            {
                // 先加载随机图占位：视频成功（VideoFrameAvailable 首帧）会覆盖；卡死则保持图，全程不黑屏。
                // 从视频文件所在目录抽图（mode 2 指定视频 / mode 3 文件夹随机都覆盖；目录无图则无占位）
                var placeholder = PickRandomImageFromFolder(Path.GetDirectoryName(file));
                if (placeholder is not null)
                {
                    try
                    {
                        await ChangeBackgroundImageAsync(placeholder, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // 占位图损坏或解码器不支持时不能阻断视频启动；保留当前背景直到首帧成功提交
                        _logger.LogWarning(ex, "Video placeholder failed {File}", placeholder);
                    }
                }
                // 占位加载尾段是否观察 token 取决于取色开关（开关开时 ExtractAccentAsync 内会主动抛）；
                // 关闭时尾段无 token-aware await，取消未必抛出，放行会把已取消会话的 MediaPlayer 启起来
                // 叠在新会话上（双视频同屏 + _lastFile 被旧值覆盖），此处强制拦截兜底
                ct.ThrowIfCancellationRequested();
                StartMediaPlayer(file, resetRetryCount: true);
            }
            else
            {
                BackgroundImageSource = null;
                await ChangeBackgroundImageAsync(file, ct);
                ct.ThrowIfCancellationRequested();
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

    private static readonly HashSet<string> WallpaperMediaExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp",
        ".gif",
        ".mp4",
        ".mkv",
        ".mov",
        ".avi",
        ".webm",
    };

    private static readonly HashSet<string> WallpaperVideoExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".mp4",
        ".mkv",
        ".mov",
        ".avi",
        ".webm",
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
            1 => (
                AppConfig.WallpaperFile is { Length: > 0 } f
                    ? Path.Combine(AppConfig.CacheFolder, "bg", f)
                    : null,
                false
            ),
            2 => (
                string.IsNullOrWhiteSpace(AppConfig.WallpaperVideoFile)
                    ? null
                    : AppConfig.WallpaperVideoFile,
                false
            ),
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
            var all = Directory
                .EnumerateFiles(folder)
                .Where(f => WallpaperMediaExtensions.Contains(Path.GetExtension(f)))
                .ToList();
            if (all.Count == 0)
                return (null, false);

            List<string> candidates;
            bool fellBack = false;
            if (AppConfig.WallpaperFolderPreferVideo)
            {
                var videos = all.Where(f => WallpaperVideoExtensions.Contains(Path.GetExtension(f)))
                    .ToList();
                if (videos.Count > 0)
                {
                    candidates = videos;
                }
                else
                {
                    // 仅视频但无视频 → 回退到图片（由调用方弹 warning）
                    candidates = all.Where(f =>
                            !WallpaperVideoExtensions.Contains(Path.GetExtension(f))
                        )
                        .ToList();
                    if (candidates.Count == 0)
                        return (null, false);
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

    /// <summary>
    /// 视频卡死兜底：从文件夹随机抽一张图片（非视频），避免视频管线失败后黑屏。
    /// </summary>
    private static string? PickRandomImageFromFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;
        try
        {
            var images = Directory
                .EnumerateFiles(folder)
                .Where(f =>
                    WallpaperMediaExtensions.Contains(Path.GetExtension(f))
                    && !WallpaperVideoExtensions.Contains(Path.GetExtension(f))
                )
                .ToList();
            return images.Count == 0 ? null : images[Random.Shared.Next(images.Count)];
        }
        catch
        {
            return null;
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
            case 1:
                AppConfig.WallpaperFile = null;
                break;
            case 2:
                AppConfig.WallpaperVideoFile = null;
                break;
            case 3:
                AppConfig.WallpaperFolder = null;
                break;
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
        using var bgra = new CanvasRenderTarget(
            device,
            w,
            h,
            96,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            CanvasAlphaMode.Premultiplied
        );
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
        ReportNowPlaying(file);

        await ExtractAccentAsync(bgra, w, h, ct);
    }

    /// <summary>
    /// 从 BGRA 位图提取主色应用为强调色（开关关则跳过）。
    /// </summary>
    private async Task ExtractAccentAsync(
        CanvasRenderTarget bgra,
        int w,
        int h,
        CancellationToken ct = default
    )
    {
        if (!AppConfig.EnableAccentFromWallpaper)
        {
            return;
        }
        try
        {
            byte[] bytes = bgra.GetPixelBytes();
            var color = await Task.Run(() => AccentColorHelper.GetAccentColor(bytes, w, h));
            // 取色算完但会话已被更新会话取消：已废弃图的颜色不再应用到全局 accent
            ct.ThrowIfCancellationRequested();
            if (color is not null)
            {
                AccentColorHelper.ChangeAppAccentColor(color);
                // 只应用不存储：AccentColor 存储项语义是「用户手动选择」，自动取色写它会把
                // 壁纸残留色冒充手动选择（关掉自动取色后取色器里显示的不是用户选的色）
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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
        // 设置页切换模式时 BackgroundChanged 与 AccentRefreshRequested 几乎同时到，
        // 直接读会拿到旧壁纸路径（重载任务末尾才更新 _lastFile），先等在途重载收尾
        await _updateBackgroundTask;
        // 取色对象必须是「正在显示的文件」（_lastFile）。此前用 ResolveWallpaperPath()：
        // mode 3 会重新随机抽一个（取到非显示图的颜色），抽到视频则只重置标志直接返回（本次取色丢失）
        string? file = _lastFile;
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
            using var bgra = new CanvasRenderTarget(
                device,
                w,
                h,
                96,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                CanvasAlphaMode.Premultiplied
            );
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

    private void StartMediaPlayer(string file, bool resetRetryCount)
    {
        if (resetRetryCount)
        {
            _mediaPlayerRetryCount = 0;
        }

        _logger.LogDebug(
            "StartMediaPlayer file={File} retry={Retry}",
            file,
            _mediaPlayerRetryCount
        );
        _currentVideoFile = file;
        _mediaOpened = false;
        _videoFramePresented = false;
        _videoAccentExtracted = false;

        var player = new MediaPlayer
        {
            IsLoopingEnabled = true,
            IsMuted = true,
        };
        player.CommandManager.IsEnabled = false;
        player.SystemMediaTransportControls.IsEnabled = false;
        player.VideoFrameAvailable += MediaPlayer_VideoFrameAvailable;
        player.MediaFailed += MediaPlayer_MediaFailed;
        player.MediaOpened += MediaPlayer_MediaOpened;
        _mediaPlayer = player;

        try
        {
            // 事件全部订阅完成后再设置 Source，避免本地文件快速打开/失败时漏掉状态事件
            player.Source = MediaSource.CreateFromUri(new Uri(file));
            _ = WatchMediaOpenAsync(player);
        }
        catch (Exception ex)
        {
            RetryOrKeepPlaceholder(player, "source setup", ex);
        }
    }

    private async Task WatchMediaOpenAsync(MediaPlayer player)
    {
        await Task.Delay(MediaOpenTimeoutMs).ConfigureAwait(false);
        if (
            DispatcherQueue is null
            || !DispatcherQueue.TryEnqueue(() =>
            {
                if (
                    _mediaPlayer == player
                    && !_mediaOpened
                    && !_windowHidden
                    && !_sessionLocked
                )
                {
                    RetryOrKeepPlaceholder(player, "MediaOpened timeout");
                }
            })
        )
        {
            _logger.LogWarning("MediaOpened watchdog could not enqueue");
        }
    }

    private async Task WatchFirstFrameAsync(MediaPlayer player)
    {
        await Task.Delay(FirstFrameTimeoutMs).ConfigureAwait(false);
        if (
            DispatcherQueue is null
            || !DispatcherQueue.TryEnqueue(() =>
            {
                if (
                    _mediaPlayer == player
                    && !_videoFramePresented
                    && !_windowHidden
                    && !_sessionLocked
                )
                {
                    RetryOrKeepPlaceholder(player, "first frame timeout");
                }
            })
        )
        {
            _logger.LogWarning("First-frame watchdog could not enqueue");
        }
    }

    private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        bool queued =
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (_mediaPlayer != sender)
                {
                    return;
                }

                try
                {
                    _mediaOpened = true;
                    sender.IsVideoFrameServerEnabled = true;
                    if (!_windowHidden && !_sessionLocked)
                    {
                        _logger.LogDebug("MediaOpened, enter frame server + Play");
                        sender.Play();
                        _ = WatchFirstFrameAsync(sender);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "MediaOpened while paused hidden={Hidden} sessionLocked={SessionLocked}",
                            _windowHidden,
                            _sessionLocked
                        );
                    }
                }
                catch (Exception ex)
                {
                    RetryOrKeepPlaceholder(sender, "MediaOpened handler", ex);
                }
            }) ?? false;
        if (!queued)
        {
            _logger.LogWarning("MediaOpened could not enqueue to UI dispatcher");
        }
    }

    private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        QueueVideoFailure(sender, "MediaFailed", args.ExtendedErrorCode);
    }

    private void QueueVideoFailure(MediaPlayer sender, string phase, Exception? exception = null)
    {
        if (
            DispatcherQueue is null
            || !DispatcherQueue.TryEnqueue(() => RetryOrKeepPlaceholder(sender, phase, exception))
        )
        {
            _logger.LogError(exception, "Video failure could not enqueue phase={Phase}", phase);
        }
    }

    private void RetryOrKeepPlaceholder(
        MediaPlayer sender,
        string phase,
        Exception? exception = null
    )
    {
        if (_mediaPlayer != sender)
        {
            return;
        }

        string? file = _currentVideoFile;
        if (_mediaPlayerRetryCount < MediaPlayerMaxRetries && file is not null)
        {
            _mediaPlayerRetryCount++;
            _logger.LogWarning(
                exception,
                "Video initialization failed phase={Phase}; rebuild MediaPlayer retry={Retry}/{MaxRetries}",
                phase,
                _mediaPlayerRetryCount,
                MediaPlayerMaxRetries
            );
            DisposeVideoResource();
            StartMediaPlayer(file, resetRetryCount: false);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Video initialization failed phase={Phase}; retries exhausted, keeping placeholder image",
                phase
            );
            DisposeVideoResource();
        }
    }

    private void MediaPlayer_VideoFrameAvailable(MediaPlayer sender, object args)
    {
        if (_mediaPlayer != sender || !_videoSemaphore.Wait(0))
        {
            return;
        }

        bool queued;
        try
        {
            queued = DispatcherQueue?.TryEnqueue(() =>
            {
                CanvasRenderTarget? pendingSurface = null;
                try
                {
                    // Dispose/rebuild 后旧播放器可能仍有已排队的帧回调，不能再触碰新会话的共享 surface
                    if (_mediaPlayer != sender)
                    {
                        return;
                    }

                    int w = (int)sender.PlaybackSession.NaturalVideoWidth;
                    int h = (int)sender.PlaybackSession.NaturalVideoHeight;
                    if (w <= 0 || h <= 0)
                    {
                        throw new InvalidOperationException($"Invalid video frame size {w}x{h}.");
                    }

                    bool needsNewSurface =
                        _videoSurface is null
                        || _videoImageSource is null
                        || (int)_videoSurface.SizeInPixels.Width != w
                        || (int)_videoSurface.SizeInPixels.Height != h;
                    CanvasRenderTarget surface;
                    CanvasImageSource imageSource;
                    if (needsNewSurface)
                    {
                        pendingSurface = new CanvasRenderTarget(
                            CanvasDevice.GetSharedDevice(),
                            w,
                            h,
                            96
                        );
                        surface = pendingSurface;
                        imageSource = new CanvasImageSource(
                            CanvasDevice.GetSharedDevice(),
                            w,
                            h,
                            96
                        );
                    }
                    else
                    {
                        surface = _videoSurface!;
                        imageSource = _videoImageSource!;
                    }

                    // 只有 Copy + Draw 都成功，才发布新 surface 并用它替换占位图
                    sender.CopyFrameToVideoSurface(surface);
                    using (var ds = imageSource.CreateDrawingSession(Colors.Transparent))
                    {
                        ds.DrawImage(surface);
                    }

                    if (needsNewSurface)
                    {
                        _videoSurface?.Dispose();
                        _videoSurface = surface;
                        _videoImageSource = imageSource;
                        pendingSurface = null;
                    }
                    BackgroundImageSource = imageSource;

                    if (!_videoFramePresented)
                    {
                        _videoFramePresented = true;
                        _mediaPlayerRetryCount = 0;
                        ReportNowPlaying(_currentVideoFile);
                        _logger.LogDebug("Video first frame presented {W}x{H}", w, h);
                    }

                    // 视频首个成功呈现帧取一次色（自动取色开时），避免强调色随帧乱跳
                    if (!_videoAccentExtracted && AppConfig.EnableAccentFromWallpaper)
                    {
                        _videoAccentExtracted = true;
                        try
                        {
                            var color = AccentColorHelper.GetAccentColor(
                                surface.GetPixelBytes(),
                                (int)surface.SizeInPixels.Width,
                                (int)surface.SizeInPixels.Height
                            );
                            if (color is not null)
                            {
                                AccentColorHelper.ChangeAppAccentColor(color);
                                // 同图片壁纸：只应用不存储，不污染手动色
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Accent from video first frame");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 不把空画布发布为成功首帧；看门狗仍会在超时后重建播放器
                    _logger.LogWarning(ex, "Video frame copy/draw failed");
                }
                finally
                {
                    pendingSurface?.Dispose();
                    _videoSemaphore.Release();
                }
            }) ?? false;
        }
        catch (Exception ex)
        {
            queued = false;
            _logger.LogWarning(ex, "Enqueue video frame failed");
        }
        if (!queued)
        {
            _videoSemaphore.Release();
            _logger.LogWarning("Video frame could not enqueue to UI dispatcher");
        }
    }

    private void DisposeVideoResource()
    {
        // 先使 sender 身份失效，让已排进 DispatcherQueue 的旧帧回调只负责释放信号量后退出
        var player = _mediaPlayer;
        _mediaPlayer = null;
        _currentVideoFile = null;
        _mediaOpened = false;
        _videoFramePresented = false;
        if (player is not null)
        {
            player.VideoFrameAvailable -= MediaPlayer_VideoFrameAvailable;
            player.MediaFailed -= MediaPlayer_MediaFailed;
            player.MediaOpened -= MediaPlayer_MediaOpened;
            player.Dispose();
        }
        _videoSurface?.Dispose();
        _videoSurface = null;
        _videoImageSource = null;
    }
}
