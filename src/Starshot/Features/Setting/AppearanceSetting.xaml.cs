using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI.Xaml.Controls;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using Windows.UI;

namespace Starshot.Features.Setting;

public sealed partial class AppearanceSetting : PageBase
{

    public int Theme
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.Theme = value;
                App.Current.MainWindow?.ApplyTheme();
            }
        }
    } = AppConfig.Theme;


    public Windows.UI.Color AccentColorValue { get; set => SetProperty(ref field, value); } = ParseAccentHex(AppConfig.AccentColor);


    private static Windows.UI.Color ParseAccentHex(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                return hex.ToColor();
            }
        }
        catch { }
        return Windows.UI.Color.FromArgb(255, 0x2D, 0xBE, 0x9C);
    }


    [RelayCommand]
    private void ApplyAccent()
    {
        AppConfig.AccentColor = AccentColorValue.ToHex();
        AccentColorHelper.ChangeAppAccentColor(AccentColorValue);
    }

}
