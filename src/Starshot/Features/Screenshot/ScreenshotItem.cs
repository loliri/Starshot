using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Starshot.Features.Codec;
using Starshot.Language;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Starshot.Features.Screenshot;

public partial class ScreenshotItem : ObservableObject
{
    public string Name { get; set; }

    public string FilePath { get; set; }

    public string FileName { get; set; }

    public string FileInfo
    {
        get;
        set => SetProperty(ref field, value);
    }

    public DateTime CreationTime { get; set; }

    public string CreationTimeText { get; set; }

    public string TimeMonthDay { get; set; }

    /// <summary>剪贴板历史项本体（重新复制走 Clipboard.SetHistoryItemAsContent，专用 API）</summary>
    public ClipboardHistoryItem? HistoryItem { get; set; }

    /// <summary>剪贴板历史项的图片流引用（缓存首张缩略图用；点击预览/信息复用，避免反复 GetBitmapAsync）</summary>
    public RandomAccessStreamReference? ClipboardStream { get; set; }

    /// <summary>剪贴板项缩略图（BitmapImage）。文件项不用（走 FilePath 优化），故为 null。</summary>
    public ImageSource? ThumbImage
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// 统一缩略图源，供 CachedImage.Source 绑定：
    /// 文件项返回 FilePath（string → 触发 CachedImage 的 IsThumbnail 下采样优化，原管线不动）；
    /// 剪贴板项返回 ThumbImage（BitmapImage，直接显示）。
    /// </summary>
    public object? DisplaySource => ClipboardStream is null ? FilePath : ThumbImage;

    public ScreenshotItem(string file)
    {
        FilePath = file;
        FileName = Path.GetFileName(file);
        Name = Path.GetFileNameWithoutExtension(file);
        var info = new FileInfo(file);
        CreationTime = info.CreationTime;
        FileInfo = GetFileInfo(info);
        _fileInfoSet = true;
        CreationTimeText = CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
        TimeMonthDay = CreationTime.ToString("yyyy-MM-dd");
    }

    /// <summary>剪贴板历史图片项（无文件；存历史项本体 + 缓存流引用）</summary>
    public static ScreenshotItem FromClipboard(
        ClipboardHistoryItem hist,
        RandomAccessStreamReference stream
    )
    {
        var local = hist.Timestamp.LocalDateTime;
        return new ScreenshotItem
        {
            Name = "Clipboard",
            FilePath = "",
            FileName = "Clipboard",
            CreationTime = local,
            CreationTimeText = local.ToString("yyyy-MM-dd HH:mm:ss"),
            TimeMonthDay = Lang.Common_Clipboard,
            HistoryItem = hist,
            ClipboardStream = stream,
        };
    }

    /// <summary>
    /// 当前剪贴板内容项（非历史条目）：HistoryItem=null，信息（尺寸/大小）直接展示在卡上，
    /// 不挂右键菜单（重新复制/删除对无历史条目的内容无意义）。
    /// </summary>
    public static ScreenshotItem FromCurrentClipboard(RandomAccessStreamReference stream)
    {
        var now = DateTime.Now;
        return new ScreenshotItem
        {
            Name = "Clipboard",
            FilePath = "",
            FileName = "Clipboard",
            CreationTime = now,
            CreationTimeText = now.ToString("yyyy-MM-dd HH:mm:ss"),
            TimeMonthDay = Lang.ClipboardPage_CurrentClipboard,
            HistoryItem = null,
            ClipboardStream = stream,
        };
    }

    private ScreenshotItem() { }

    private static string GetFileInfo(FileInfo info)
    {
        const double KB = 1 << 10,
            MB = 1 << 20;
        string ext = info.Extension.Replace(".", "").ToUpper();
        string size = info.Length >= MB ? $"{info.Length / MB:F2} MB" : $"{info.Length / KB:F2} KB";
        return $"{ext}  {size}".Trim();
    }

    private bool _fileInfoSet = false;

    private bool _updatedPixelSize = false;

    public async void UpdatePixelSize()
    {
        try
        {
            if (_updatedPixelSize)
            {
                return;
            }
            if (!_fileInfoSet)
            {
                var info = new FileInfo(FilePath);
                FileInfo = GetFileInfo(info);
                _fileInfoSet = true;
            }
            (uint width, uint height) = await ImageLoader.GetImagePixelSizeAsync(FilePath);
            if (width > 0 && height > 0)
            {
                FileInfo = $"{FileInfo}  {width} x {height}";
                _updatedPixelSize = true;
            }
        }
        catch { }
    }
}

public class ScreenshotItemGroup : ObservableCollection<ScreenshotItem>
{
    public string Header { get; set; }

    public ScreenshotItemGroup(string header, IEnumerable<ScreenshotItem> list)
        : base(list)
    {
        Header = header;
    }

    public ScreenshotItemGroup(string header)
        : base()
    {
        Header = header;
    }
}
