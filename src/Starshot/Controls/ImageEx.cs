using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Starshot.Controls;

/// <summary>
/// 带异步加载钩子的图片控件
/// Source 接 object：Uri / string 走 ProvideCachedResourceAsync 异步管线，ImageSource 直接显示；
/// 换源自动取消上一次加载，加载中/失败显示 Background 底色占位。
/// 基类用 ContentControl（WinUI 的 Border 是密封类不可继承）：默认模板的 Border 呈现 Background/CornerRadius。
/// </summary>
public partial class ImageEx : ContentControl
{
    private readonly Image _image = new();
    private CancellationTokenSource? _cts;

    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(object),
        typeof(ImageEx),
        new PropertyMetadata(null, OnSourceChanged)
    );

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(ImageEx),
        new PropertyMetadata(Stretch.Uniform, OnStretchChanged)
    );

    public ImageEx()
    {
        Content = _image;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ImageEx)d).SetSource(e.NewValue);
    }

    private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ImageEx)d)._image.Stretch = (Stretch)e.NewValue;
    }

    private async void SetSource(object? value)
    {
        // 换源先取消旧加载；旧异步完成后落图前查 token，迟到的丢弃
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        // 清空旧图：列表虚拟化复用控件时保留旧图会显示上一项的内容
        _image.Source = null;
        try
        {
            switch (value)
            {
                case null:
                    return;
                case ImageSource src:
                    _image.Source = src;
                    return;
                case Uri uri:
                    await SetSourceFromUriAsync(uri, token);
                    return;
                case string s when Uri.TryCreate(s, UriKind.Absolute, out var u):
                    await SetSourceFromUriAsync(u, token);
                    return;
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            // 取图管线非取消异常（文件损坏 / 解码失败）：保持 Background 占位，不让 async void 异常崩进程
            _image.Source = null;
        }
    }

    private async Task SetSourceFromUriAsync(Uri uri, CancellationToken token)
    {
        var source = await ProvideCachedResourceAsync(uri, token);
        if (!token.IsCancellationRequested)
        {
            _image.Source = source;
        }
    }

    /// <summary>
    /// 异步取图钩子：默认直出 BitmapImage，派生类覆写接入缓存/缩略图管线。
    /// </summary>
    protected virtual Task<ImageSource?> ProvideCachedResourceAsync(
        Uri imageUri,
        CancellationToken token
    )
    {
        return Task.FromResult<ImageSource?>(new BitmapImage(imageUri));
    }
}
