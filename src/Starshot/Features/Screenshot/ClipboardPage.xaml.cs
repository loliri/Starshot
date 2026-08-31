using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Starshot.Features.Background;
using Starshot.Frameworks;
using Starshot.Helpers;
using Starshot.Language;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Starshot.Features.Screenshot;

public sealed partial class ClipboardPage : PageBase
{
    private readonly ILogger<ClipboardPage> _logger = AppConfig.GetLogger<ClipboardPage>();

    /// <summary>剪贴板历史项（平铺，无分组）</summary>
    public ObservableCollection<ScreenshotItem> Items { get; } = new();

    /// <summary>剪贴板有变化、待刷新（回前台时据此补刷）</summary>
    private bool _clipboardDirty;

    /// <summary>ContentChanged 节流：500ms 内只在最后一次变化后增量同步一次</summary>
    private readonly DispatcherTimer _clipboardTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    /// <summary>UI 线程 DispatcherQueue（OnLoaded 捕获；ContentChanged 可能在后台线程触发，用字段不碰 Page 属性）</summary>
    private Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcherQueue;

    public ClipboardPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        await RefreshClipboard();
        await UpdateCurrentClipboardCardAsync();
        _uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _clipboardTimer.Tick += ClipboardTimer_Tick;
        Clipboard.ContentChanged += Clipboard_ContentChanged;
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(
            this,
            (_, m) =>
            {
                if (m.Activate && _clipboardDirty)
                {
                    _ = RefreshIncrementalAsync();
                }
            }
        );
    }

    protected override void OnUnloaded()
    {
        try
        {
            _clipboardTimer.Stop();
            _clipboardTimer.Tick -= ClipboardTimer_Tick;
            Clipboard.ContentChanged -= Clipboard_ContentChanged;
            WeakReferenceMessenger.Default.Unregister<MainWindowStateChangedMessage>(this);
            Items.Clear();
        }
        catch { }
    }

    public bool MutliSelect
    {
        get;
        set
        {
            field = value;
            GridView_Images.SelectionMode = value
                ? ListViewSelectionMode.Multiple
                : ListViewSelectionMode.None;
        }
    }

    public string SelectCountText
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>全量刷新：清空 + 重读全部历史。OnLoaded / 刷新按钮 / 切 tab 触发。</summary>
    private async Task RefreshClipboard()
    {
        try
        {
            if (!Clipboard.IsHistoryEnabled())
            {
                UpdateEmptyState(false);
                return;
            }
            var result = await Clipboard.GetHistoryItemsAsync();
            if (result.Status is not ClipboardHistoryItemsResultStatus.Success)
                return;
            var newItems = new List<ScreenshotItem>();
            foreach (var hist in result.Items)
            {
                try
                {
                    if (!hist.Content.Contains(StandardDataFormats.Bitmap))
                        continue;
                    var streamRef = await hist.Content.GetBitmapAsync();
                    var item = ScreenshotItem.FromClipboard(hist, streamRef);
                    using var ts = await streamRef.OpenReadAsync();
                    var bmp = new BitmapImage { DecodePixelWidth = 200 };
                    await bmp.SetSourceAsync(ts);
                    item.ThumbImage = bmp;
                    newItems.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RefreshClipboard: 解析历史项失败");
                }
            }
            Items.Clear();
            foreach (var i in newItems)
                Items.Add(i);
            _clipboardDirty = false;
            UpdateSelectCountText();
            UpdateEmptyState(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RefreshClipboard");
        }
    }

    /// <summary>控制空状态 UI：没开 / 开了没图片 / 有图片</summary>
    private void UpdateEmptyState(bool historyEnabled)
    {
        bool hasItems = Items.Count > 0;
        GridView_Images.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        StackPanel_NotEnabled.Visibility = !historyEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        StackPanel_NoImages.Visibility =
            (historyEnabled && !hasItems) ? Visibility.Visible : Visibility.Collapsed;
        TextBlock_SelectCount.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton_MultiSelect.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        Button_Refresh.Visibility = historyEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 增量刷新（节流 tick / 回前台）：先同步历史再更新当前卡——顺序不能反，
    /// 否则卡片比对时新历史项还没插进列表，小图会被误判成「不在历史」而重复展示。
    /// </summary>
    private async Task RefreshIncrementalAsync()
    {
        await AddNewClipboardItems();
        await UpdateCurrentClipboardCardAsync();
    }

    /// <summary>
    /// 增量同步：读历史 → 按 ClipboardHistoryItem.Id 比对 → 只把新项插到列表顶。
    /// 不动现有、不删（删除监听不到，由右键"删除"定向移除）。读失败（AccessDenied 没前台）则脏留着，回前台再试。
    /// </summary>
    private async Task AddNewClipboardItems()
    {
        try
        {
            if (!Clipboard.IsHistoryEnabled())
                return;
            var result = await Clipboard.GetHistoryItemsAsync();
            if (result.Status is not ClipboardHistoryItemsResultStatus.Success)
                return;
            var existingIds = Items
                .Where(i => i.HistoryItem is not null)
                .Select(i => i.HistoryItem!.Id)
                .ToHashSet();
            var newItems = new List<ScreenshotItem>();
            foreach (var hist in result.Items)
            {
                if (existingIds.Contains(hist.Id))
                    continue;
                if (!hist.Content.Contains(StandardDataFormats.Bitmap))
                    continue;
                try
                {
                    var streamRef = await hist.Content.GetBitmapAsync();
                    var item = ScreenshotItem.FromClipboard(hist, streamRef);
                    using var ts = await streamRef.OpenReadAsync();
                    var bmp = new BitmapImage { DecodePixelWidth = 200 };
                    await bmp.SetSourceAsync(ts);
                    item.ThumbImage = bmp;
                    newItems.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AddNewClipboardItems: 解析历史项失败");
                }
            }
            if (newItems.Count == 0)
            {
                _clipboardDirty = false;
                return;
            }
            // 插到列表顶（倒序 Insert(0) 保持历史顺序：最新在最上）
            for (int i = newItems.Count - 1; i >= 0; i--)
            {
                Items.Insert(0, newItems[i]);
            }
            _clipboardDirty = false;
            UpdateSelectCountText();
            // 空列表→有内容的切换只发生在全量刷新；增量首图进来时也切，否则 GridView 仍 Collapsed
            UpdateEmptyState(historyEnabled: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddNewClipboardItems");
        }
    }

    /// <summary>剪贴板变化：置脏 + 重置节流定时器。
    /// 此事件可能在剪贴板写入期间（如 Alt+A 的 EmptyClipboard 后）同步、在后台线程触发，
    /// 绝不能抛——否则会打断 SetBitmapDib，导致清空了没填回。用捕获的字段，不碰 Page 属性。</summary>
    private void Clipboard_ContentChanged(object? sender, object e)
    {
        _clipboardDirty = true;
        try
        {
            _uiDispatcherQueue?.TryEnqueue(() =>
            {
                _clipboardTimer.Stop();
                _clipboardTimer.Start();
            });
        }
        catch { }
    }

    private void ClipboardTimer_Tick(object? sender, object e)
    {
        _clipboardTimer.Stop();
        _ = RefreshIncrementalAsync();
    }

    private void GridView_Images_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (
                GridView_Images.SelectionMode is ListViewSelectionMode.None
                && e.ClickedItem is ScreenshotItem item
            )
            {
                OpenClipboardItem(item);
            }
        }
        catch { }
    }

    private void GridView_Images_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectCountText();
    }

    private void UpdateSelectCountText()
    {
        try
        {
            SelectCountText = MutliSelect
                ? $"{GridView_Images.SelectedItems.Count}/{Items.Count}"
                : $"{Items.Count}";
        }
        catch { }
    }

    private async void OpenClipboardItem(ScreenshotItem item, bool autoOcr = false)
    {
        // 右键「识别文字」未就绪：检测留在主窗口，toast + 配置对话框都在主窗口，不开查看器
        if (autoOcr && AppConfig.OcrEngine == 0 && !OcrHelper.IsOneOcrReady)
        {
            InAppToast.MainWindow?.Information(null, Lang.Ocr_NotConfigured, 3000);
            var dialog = new Setting.OcrEngineDialog { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
            return;
        }
        _ = new ImageViewWindow().ShowWindowAsync(
            this.XamlRoot.ContentIslandEnvironment.AppWindowId,
            item,
            Items,
            autoOcr
        );
    }

    /// <summary>每个剪贴板项都挂这个专用菜单：信息 / 打开 / 识别文字 / 重新复制 / 删除</summary>
    private void Grid_ImageItem_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args
    )
    {
        if (sender is Grid grid && args.NewValue is ScreenshotItem item)
        {
            grid.ContextFlyout = BuildClipboardFlyout(item);
        }
    }

    private MenuFlyout BuildClipboardFlyout(ScreenshotItem item)
    {
        var stream = item.ClipboardStream!;
        var flyout = new MenuFlyout();
        // 信息（尺寸+大小，异步加载；独立函数）
        var info = new MenuFlyoutItem
        {
            MinWidth = 208,
            FontSize = 12,
            IsEnabled = false,
            IsTextScaleFactorEnabled = false,
            Text = "…",
        };
        info.Icon = new FontIcon { Glyph = "", IsTextScaleFactorEnabled = false };
        _ = LoadClipboardInfoAsync(info, stream);
        flyout.Items.Add(info);
        flyout.Items.Add(new MenuFlyoutSeparator());
        // 打开
        var open = new MenuFlyoutItem { Text = Lang.Common_Open, IsTextScaleFactorEnabled = false };
        open.Icon = new FontIcon { Glyph = "", IsTextScaleFactorEnabled = false };
        open.Click += (_, _) => OpenClipboardItem(item);
        flyout.Items.Add(open);
        // 识别文字（打开查看器直接进入 OCR）
        var ocr = new MenuFlyoutItem
        {
            Text = Lang.ImageViewWindow_Ocr,
            IsTextScaleFactorEnabled = false,
        };
        ocr.Icon = new FluentIcons.WinUI.SymbolIcon
        {
            Symbol = FluentIcons.Common.Symbol.ScanText,
            IsTextScaleFactorEnabled = false,
        };
        ocr.Click += (_, _) => OpenClipboardItem(item, autoOcr: true);
        flyout.Items.Add(ocr);
        // 重新复制
        var recopy = new MenuFlyoutItem
        {
            Text = Lang.ScreenshotPage_Recopy,
            IsTextScaleFactorEnabled = false,
        };
        recopy.Icon = new FontIcon { Glyph = "", IsTextScaleFactorEnabled = false };
        recopy.Click += (_, _) => RecopyClipboardItem(item.HistoryItem!);
        flyout.Items.Add(recopy);
        flyout.Items.Add(new MenuFlyoutSeparator());
        // 删除（从剪贴板历史移除）
        var delete = new MenuFlyoutItem
        {
            Text = Lang.Common_Delete,
            IsTextScaleFactorEnabled = false,
        };
        delete.Icon = new FontIcon { Glyph = "", IsTextScaleFactorEnabled = false };
        try
        {
            delete.Foreground = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["SystemFillColorCriticalBrush"];
        }
        catch { }
        // 多选模式下作用于全部选中项（右键点在选中项上删整批，否则删单项）
        delete.Click += (_, _) =>
        {
            if (MutliSelect && GridView_Images.SelectedItems.Count > 1)
            {
                foreach (var sel in GridView_Images.SelectedItems.Cast<ScreenshotItem>().ToList())
                {
                    if (sel.HistoryItem is not null)
                    {
                        DeleteClipboardItem(sel.HistoryItem);
                    }
                }
            }
            else
            {
                DeleteClipboardItem(item.HistoryItem!);
            }
        };
        flyout.Items.Add(delete);
        return flyout;
    }

    private async Task LoadClipboardInfoAsync(
        MenuFlyoutItem info,
        RandomAccessStreamReference streamRef
    )
    {
        try
        {
            using var stream = await streamRef.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            long size = (long)stream.Size;
            const double KB = 1 << 10,
                MB = 1 << 20;
            string sizeStr = size >= MB ? $"{size / MB:F2} MB" : $"{size / KB:F2} KB";
            var codec = decoder.DecoderInformation.CodecId;
            string fmt =
                codec == BitmapDecoder.PngDecoderId ? "PNG"
                : codec == BitmapDecoder.JpegDecoderId ? "JPEG"
                : codec == BitmapDecoder.BmpDecoderId ? "BMP"
                : codec == BitmapDecoder.TiffDecoderId ? "TIFF"
                : codec == BitmapDecoder.GifDecoderId ? "GIF"
                : "IMG";
            info.Text = $"{fmt}  {sizeStr}  {decoder.PixelWidth} x {decoder.PixelHeight}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadClipboardInfoAsync");
        }
    }

    /// <summary>重新复制：把该历史项提升为当前剪贴板内容（专用 API）</summary>
    private void RecopyClipboardItem(ClipboardHistoryItem hist)
    {
        try
        {
            Clipboard.SetHistoryItemAsContent(hist);
            InAppToast.MainWindow?.Success(Lang.ImageViewWindow_CopiedToClipboard, null, 1500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RecopyClipboardItem");
        }
    }

    /// <summary>从剪贴板历史删除该项，并定向从列表移除</summary>
    private void DeleteClipboardItem(ClipboardHistoryItem hist)
    {
        try
        {
            Clipboard.DeleteItemFromHistory(hist);
            var item = Items.FirstOrDefault(i => i.HistoryItem?.Id == hist.Id);
            if (item is not null)
            {
                Items.Remove(item);
                UpdateSelectCountText();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteClipboardItem");
        }
    }

    private void Button_Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshAllAsync();
    }

    /// <summary>全量刷新 + 当前卡更新，必须串行：并行会让卡片比对跑在历史重读前，小图被误判重复展示</summary>
    private async Task RefreshAllAsync()
    {
        await RefreshClipboard();
        await UpdateCurrentClipboardCardAsync();
    }

    /// <summary>
    /// 当前剪贴板卡的承载项（点击打开查看器用）；无卡时为 null
    /// </summary>
    private ScreenshotItem? _currentItem;

    /// <summary>
    /// 更新「当前剪贴板」卡：读当前剪贴板图像，与历史第一条内容一致（小图，已进历史）则隐藏，
    /// 不一致（内容过大被系统历史跳过 / 历史为空或未开启）则显示。任何失败静默收卡，绝不抛。
    /// </summary>
    private async Task UpdateCurrentClipboardCardAsync()
    {
        try
        {
            var streamRef = await ClipboardHelper.GetClipboardImageAsync();
            if (streamRef is null)
            {
                HideCurrentClipboardCard();
                return;
            }
            // 尺寸/大小与缩略图各开一次流：decoder 与 SetSourceAsync 不能共用（流位置会被前者推进）
            uint width,
                height;
            long streamByteSize;
            using (var read = await streamRef.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(read);
                width = decoder.PixelWidth;
                height = decoder.PixelHeight;
                streamByteSize = (long)read.Size;
            }
            if (await IsSameAsFirstHistoryItem(streamRef))
            {
                HideCurrentClipboardCard();
                return;
            }
            var bmp = new BitmapImage { DecodePixelWidth = 200 };
            using (var read = await streamRef.OpenReadAsync())
            {
                await bmp.SetSourceAsync(read);
            }
            _currentItem = ScreenshotItem.FromCurrentClipboard(streamRef);
            _currentItem.ThumbImage = bmp;
            CachedImage_Current.Source = bmp;
            const double KB = 1 << 10,
                MB = 1 << 20;
            string sizeStr =
                streamByteSize >= MB
                    ? $"{streamByteSize / MB:F2} MB"
                    : $"{streamByteSize / KB:F2} KB";
            TextBlock_CurrentDims.Text = $"{width}×{height}  ·  {sizeStr}";
            Border_CurrentClipboard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateCurrentClipboardCardAsync");
            HideCurrentClipboardCard();
        }
    }

    private void HideCurrentClipboardCard()
    {
        _currentItem = null;
        Border_CurrentClipboard.Visibility = Visibility.Collapsed;
    }

    /// <summary>当前剪贴板图像是否与历史第一条图像逐像素一致（尺寸 + Bgra8 字节比较）</summary>
    private async Task<bool> IsSameAsFirstHistoryItem(RandomAccessStreamReference current)
    {
        try
        {
            var first = Items.FirstOrDefault(i => i.HistoryItem is not null);
            if (first?.ClipboardStream is null)
                return false;
            using var a = await current.OpenReadAsync();
            using var b = await first.ClipboardStream.OpenReadAsync();
            var da = await BitmapDecoder.CreateAsync(a);
            var db = await BitmapDecoder.CreateAsync(b);
            if (da.PixelWidth != db.PixelWidth || da.PixelHeight != db.PixelHeight)
                return false;
            var transform = new BitmapTransform();
            var pa = await da.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage
            );
            var pb = await db.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage
            );
            byte[] ba = pa.DetachPixelData();
            byte[] bb = pb.DetachPixelData();
            return ba.Length == bb.Length && ba.AsSpan().SequenceEqual(bb);
        }
        catch
        {
            return false;
        }
    }

    private void Border_CurrentClipboard_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e
    )
    {
        if (_currentItem is not null)
        {
            OpenClipboardItem(_currentItem);
        }
    }

    private async void Hyperlink_Settings_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:clipboard"));
    }
}
