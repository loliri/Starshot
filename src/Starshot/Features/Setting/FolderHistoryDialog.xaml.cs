using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starshot.Helpers;
using Starshot.Language;
using Windows.System;

namespace Starshot.Features.Setting;

/// <summary>历史条目：路径 + 目录是否存在（不存在时隐藏「打开」）</summary>
public sealed class FolderHistoryItem
{
    public string Path { get; set; } = "";
    public bool Exists { get; set; }
}

/// <summary>
/// 目录更改历史对话框（通用：截图 / 日志目录共用）。
/// 条目仅打开与删除，不提供还原；恢复默认从右下角入口（回调注入，含已是默认提示）。
/// </summary>
public sealed partial class FolderHistoryDialog : ContentDialog
{
    private readonly Func<List<string>> _load;
    private readonly Action<List<string>> _save;
    private readonly Action _reset;

    public ObservableCollection<FolderHistoryItem> Items { get; } = new();

    public bool HasNoItems => Items.Count == 0;

    public FolderHistoryDialog(
        string title,
        Func<List<string>> load,
        Action<List<string>> save,
        Action reset
    )
    {
        _load = load;
        _save = save;
        _reset = reset;
        InitializeComponent();
        TextBlock_Title.Text = title;
        Reload();
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var path in _load())
        {
            Items.Add(new FolderHistoryItem { Path = path, Exists = Directory.Exists(path) });
        }
        Bindings.Update();
    }

    private async void Button_Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: FolderHistoryItem item })
            {
                await Launcher.LaunchFolderPathAsync(item.Path);
            }
        }
        catch { }
    }

    private void Button_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderHistoryItem item })
        {
            var list = _load();
            list.Remove(item.Path);
            _save(list);
            Reload();
        }
    }

    private void Button_Reset_Click(object sender, RoutedEventArgs e)
    {
        _reset();
        Hide();
    }

    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
