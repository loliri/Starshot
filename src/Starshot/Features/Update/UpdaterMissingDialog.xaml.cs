using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Starshot.Features.Update;

/// <summary>
/// 安装版线缺失 Starshot.Update.exe 的提示（DatabaseLocationDialog 同款样式）：
/// 更新器是更新链的唯一入口，缺了只能重下安装包，不再回退便携流程（装线布局下回退必然装出错布局）。
/// </summary>
public sealed partial class UpdaterMissingDialog : ContentDialog
{

    public UpdaterMissingDialog()
    {
        InitializeComponent();
    }


    private void Button_OK_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

}
