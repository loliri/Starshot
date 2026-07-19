using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Starshot.Features.Setting;
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


}
