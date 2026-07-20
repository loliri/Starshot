using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Starshot.Features.Codec;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Starshot.Features.Setting;

public sealed partial class GeneralSetting : PageBase
{

    private readonly ILogger<GeneralSetting> _logger = AppConfig.GetLogger<GeneralSetting>();


    public GeneralSetting()
    {
        InitializeComponent();
    }


    public bool EnableSystemTrayIcon
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableSystemTrayIcon = value;
                if (value)
                {
                    App.Current.EnsureSystemTray();
                }
            }
        }
    } = AppConfig.EnableSystemTrayIcon;


    public bool EnableAutoStart
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UpdateAutoStartRegistry(value);
                OnPropertyChanged(nameof(AutoStartMinimizedVisibility));
            }
        }
    } = AppConfig.EnableAutoStart;


    public bool AutoStartMinimized
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.AutoStartMinimized = value;
                if (AppConfig.EnableAutoStart) UpdateAutoStartRegistry(true);
            }
        }
    } = AppConfig.AutoStartMinimized;


    public Visibility AutoStartMinimizedVisibility => EnableAutoStart ? Visibility.Visible : Visibility.Collapsed;


    public int LanguageIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                string? lang = value switch { 1 => "en-US", 2 => "zh-CN", _ => null };
                AppConfig.Language = lang;
                AppConfig.SetLanguage(lang);
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
                Environment.Exit(0);
            }
        }
    } = AppConfig.Language switch { "en-US" => 1, "zh-CN" => 2, _ => 0 };


    private static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Starshot";


    private void UpdateAutoStartRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enable)
            {
                string launcherPath = GetLauncherPath();
                string args = (AppConfig.AutoStartMinimized && AppConfig.EnableSystemTrayIcon) ? " --hide" : "";
                key.SetValue(RunValueName, $"\"{launcherPath}\"{args}");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-start registry");
        }
    }


    private static string GetLauncherPath()
    {
        string exePath = Environment.ProcessPath ?? "";
        string appDir = Path.GetDirectoryName(exePath) ?? "";
        string rootDir = Path.GetDirectoryName(appDir) ?? "";
        string launcher = Path.Combine(rootDir, "Starshot.exe");
        return File.Exists(launcher) ? launcher : exePath;
    }


}
