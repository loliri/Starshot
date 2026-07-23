using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Starshot.Features.Screenshot;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;

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


    private bool _enableColorManagement = AppConfig.EnableScreenshotColorManagement;
    public bool EnableScreenshotColorManagement
    {
        get => _enableColorManagement;
        set
        {
            if (value && !_enableColorManagement)
            {
                // 打开前先校验主显示器 primaries；畸形（VM/无 ICC）则弹 Error 并弹回关，避免截图编码时 lcms2 崩溃
                _ = TryEnableColorManagementAsync();
                return;
            }
            if (SetProperty(ref _enableColorManagement, value))
            {
                AppConfig.EnableScreenshotColorManagement = value;
            }
        }
    }


    private async Task TryEnableColorManagementAsync()
    {
        bool ok = await ScreenCaptureService.CanEnableColorManagementAsync();
        if (ok)
        {
            _enableColorManagement = true;
            AppConfig.EnableScreenshotColorManagement = true;
            OnPropertyChanged(nameof(EnableScreenshotColorManagement));
        }
        else
        {
            InAppToast.MainWindow?.Error((string?)null, Lang.Starshot_ColorManagementUnavailable, 7000);
            OnPropertyChanged(nameof(EnableScreenshotColorManagement));  // 刷新绑定，UI 弹回关
        }
    }


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
