using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Starshot.Features.Setting;

/// <summary>
/// 数据库位置已变更提示（ScreenshotFolderManageDialog 同款样式：亚克力/圆角/自绘布局）。
/// 纯告知：重启后生效 + 数据以当前快照为准。
/// </summary>
public sealed partial class DatabaseLocationDialog : ContentDialog
{

    public DatabaseLocationDialog()
    {
        InitializeComponent();
    }


    private void Button_Apply_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

}
