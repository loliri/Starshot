using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Scighost.WinUI.ImageEx;
using Starshot.Features.Codec;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starshot.Controls;

public sealed partial class CachedImage : ImageEx
{


    public bool IsThumbnail
    {
        get { return (bool)GetValue(IsThumbnailProperty); }
        set { SetValue(IsThumbnailProperty, value); }
    }

    public static readonly DependencyProperty IsThumbnailProperty =
        DependencyProperty.Register("IsThumbnail", typeof(bool), typeof(CachedImage), new PropertyMetadata(false));


    public bool PngThumbnail
    {
        get { return (bool)GetValue(PngThumbnailProperty); }
        set { SetValue(PngThumbnailProperty, value); }
    }

    public static readonly DependencyProperty PngThumbnailProperty =
        DependencyProperty.Register(nameof(PngThumbnail), typeof(bool), typeof(CachedImage), new PropertyMetadata(false));



    protected override async Task<ImageSource?> ProvideCachedResourceAsync(Uri imageUri, CancellationToken token)
    {
        if (imageUri.Scheme is "ms-appx")
        {
            return new BitmapImage(imageUri);
        }
        else if (imageUri.Scheme is "file")
        {
            if (IsThumbnail)
            {
                return await ImageThumbnail.GetImageThumbnailAsync(imageUri.LocalPath, PngThumbnail, token);
            }
            else
            {
                return new BitmapImage(imageUri);
            }
        }
        return new BitmapImage(imageUri);
    }


}
