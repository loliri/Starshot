using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Starshot.Features.Setting;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.UI;

namespace Starshot.Helpers;

internal static class AccentColorHelper
{


    public static void ChangeAppAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return;
        }
        try
        {
            ChangeAppAccentColor(hex.ToColor());
        }
        catch { }
    }


    public static void ChangeAppAccentColor(Color? color)
    {
        if (color is null)
        {
            return;
        }

        Color light1 = ColorMix(color.Value, Colors.White, 0.8);
        Color light2 = ColorMix(color.Value, Colors.White, 0.6);
        Color light3 = ColorMix(color.Value, Colors.White, 0.4);
        Color dark1 = ColorMix(color.Value, Colors.Black, 0.8);
        Color dark2 = ColorMix(color.Value, Colors.Black, 0.6);
        Color dark3 = ColorMix(color.Value, Colors.Black, 0.4);

        Application.Current.Resources["SystemAccentColor"] = color;
        Application.Current.Resources["SystemAccentColorLight1"] = light1;
        Application.Current.Resources["SystemAccentColorLight2"] = light2;
        Application.Current.Resources["SystemAccentColorLight3"] = light3;
        Application.Current.Resources["SystemAccentColorDark1"] = dark1;
        Application.Current.Resources["SystemAccentColorDark2"] = dark2;
        Application.Current.Resources["SystemAccentColorDark3"] = dark3;

        WeakReferenceMessenger.Default.Send(new AccentColorChangedMessage());
    }


    private static Color ColorMix(Color input, Color blend, double percent)
    {
        return Color.FromArgb(255,
                              (byte)(input.R * percent + blend.R * (1 - percent)),
                              (byte)(input.G * percent + blend.G * (1 - percent)),
                              (byte)(input.B * percent + blend.B * (1 - percent)));
    }


    /// <summary>
    /// 从 BGRA 像素数组提取主色（2x2 降采样→均色→HSV→饱和度提到 0.6）。移植自 Starward。
    /// </summary>
    public static unsafe Color? GetAccentColor(byte[] bgra, int width, int height)
    {
        if (bgra.Length % 4 == 0)
        {
            fixed (byte* ptr = bgra)
            {
                return GetAccentColorInternal(ptr, width, height);
            }
        }
        return null;
    }


    private static unsafe Color? GetAccentColorInternal(void* bgra, int width, int height)
    {
        try
        {
            uint* p = (uint*)bgra;
            long b = 0, g = 0, r = 0;
            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 2)
                {
                    Bgra32 pixel = Unsafe.AsRef<Bgra32>(p);
                    b += pixel.B;
                    g += pixel.G;
                    r += pixel.R;
                    p += 2;
                }
                p += width - width % 2;
            }
            int c = (width / 2) * (height / 2);
            Color color = Color.FromArgb(255, (byte)(r / c), (byte)(g / c), (byte)(b / c));
            var hsv = color.ToHsv();
            return CommunityToolkit.WinUI.Helpers.ColorHelper.FromHsv(hsv.H, 0.6, hsv.V);
        }
        catch { }
        return null;
    }


    [StructLayout(LayoutKind.Explicit, Size = 4)]
    private readonly struct Bgra32
    {
        [FieldOffset(0)] public readonly byte B;
        [FieldOffset(1)] public readonly byte G;
        [FieldOffset(2)] public readonly byte R;
        [FieldOffset(3)] public readonly byte A;
    }


}
