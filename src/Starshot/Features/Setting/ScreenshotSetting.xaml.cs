using Microsoft.UI.Xaml.Controls;
using Starshot.Frameworks;

namespace Starshot.Features.Setting;

public sealed partial class ScreenshotSetting : PageBase
{


    public int ScreenshotSDRFormat
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.ScreenCaptureSDRFormat = value;
            }
        }
    } = AppConfig.ScreenCaptureSDRFormat;


    public int ScreenshotHDRFormat
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.ScreenCaptureHDRFormat = value;
            }
        }
    } = AppConfig.ScreenCaptureHDRFormat;


    public int ScreenshotQuality
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.ScreenCaptureEncodeQuality = value;
            }
        }
    } = AppConfig.ScreenCaptureEncodeQuality;


    public bool EnableScreenshotColorManagement
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableScreenshotColorManagement = value;
            }
        }
    } = AppConfig.EnableScreenshotColorManagement;


    public bool AutoConvertScreenshotToSDR
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.AutoConvertScreenshotToSDR = value;
            }
        }
    } = AppConfig.AutoConvertScreenshotToSDR;


    public bool DeleteHDRIfSDRContent
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.DeleteHDRIfSDRContent = value;
            }
        }
    } = AppConfig.DeleteHDRIfSDRContent;


    public bool AutoCopyScreenshotToClipboard
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.AutoCopyScreenshotToClipboard = value;
            }
        }
    } = AppConfig.AutoCopyScreenshotToClipboard;


    public int CaptureMonitorSource
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.ScreenshotCaptureMonitorSource = value;
            }
        }
    } = AppConfig.ScreenshotCaptureMonitorSource;


    public ScreenshotSetting()
    {
        InitializeComponent();
    }


}
