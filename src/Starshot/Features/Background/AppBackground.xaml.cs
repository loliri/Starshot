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
using System.IO;
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
    private CanvasRenderTarget? _videoSurface;
    private CanvasImageSource? _videoImageSource;
    private readonly SemaphoreSlim _videoSemaphore = new(1, 1);


    public AppBackground()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<BackgroundChangedMessage>(this, (_, _) => _ = UpdateBackgroundAsync());
        WeakReferenceMessenger.Default.Register<AccentRefreshRequestedMessage>(this, (_, _) => _ = RefreshAccentAsync());
        Loaded += (_, _) => _ = UpdateBackgroundAsync();
        Unloaded += (_, _) =>
        {
            DisposeVideoResource();
            WeakReferenceMessenger.Default.UnregisterAll(this);
        };
    }


    public async Task UpdateBackgroundAsync()
    {
        try
        {
            IsUpdateBackgroundRunning = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            string? file = ResolveWallpaperPath();
            if (!AppConfig.EnableWallpaper || string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                DisposeVideoResource();
                BackgroundImageSource = null;
                _lastFile = null;
                return;
            }
            if (file == _lastFile)
            {
                return;
            }

            DisposeVideoResource();
            BackgroundImageSource = null;

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


    private static string? ResolveWallpaperPath()
    {
        string? f = AppConfig.WallpaperFile;
        if (string.IsNullOrWhiteSpace(f))
        {
            return null;
        }
        return Path.Combine(AppConfig.CacheFolder, "bg", f);
    }


    private static bool IsSupportedVideo(string file)
    {
        string? ext = Path.GetExtension(file)?.ToLowerInvariant();
        return ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm";
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
        string? file = ResolveWallpaperPath();
        if (string.IsNullOrEmpty(file) || !File.Exists(file) || IsSupportedVideo(file))
        {
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
        _mediaPlayer = new MediaPlayer
        {
            IsLoopingEnabled = true,
            IsMuted = true,
            IsVideoFrameServerEnabled = true,
            Source = MediaSource.CreateFromUri(new Uri(file)),
        };
        _mediaPlayer.CommandManager.IsEnabled = false;
        _mediaPlayer.SystemMediaTransportControls.IsEnabled = false;
        _mediaPlayer.VideoFrameAvailable += MediaPlayer_VideoFrameAvailable;
        _mediaPlayer.MediaFailed += (_, a) => _logger.LogError(a.ExtendedErrorCode, "MediaPlayer failed");
        _mediaPlayer.Play();
    }


    private void MediaPlayer_VideoFrameAvailable(MediaPlayer sender, object args)
    {
        if (_videoSemaphore.CurrentCount == 0)
        {
            return;
        }
        _videoSemaphore.Wait();
        DispatcherQueue.TryEnqueue(() =>
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
                }
                sender.CopyFrameToVideoSurface(_videoSurface);
                using var ds = _videoImageSource.CreateDrawingSession(Colors.Transparent);
                ds.DrawImage(_videoSurface);
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
