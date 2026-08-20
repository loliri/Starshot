using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starshot.Features.Codec;
using Starshot.Frameworks;
using Starshot.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.DirectX;
using Windows.Storage;
using Windows.System;


namespace Starshot.Features.Screenshot;

public sealed partial class ScreenshotPage : PageBase
{


    private readonly ILogger<ScreenshotPage> _logger = AppConfig.GetLogger<ScreenshotPage>();




    public ScreenshotPage()
    {
        this.InitializeComponent();
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }



    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        Initialize();
    }



    protected override void OnUnloaded()
    {
        try
        {
            foreach (var item in _watchers)
            {
                item.Dispose();
            }
            _watchers.Clear();
            _folders.Clear();
            _folders = null!;
            _screenshotDict.Clear();
            _screenshotDict = null!;
            _screenshotItems = null;
            ScreenshotGroups = null!;
            ScreenshotViewSource.Source = null;
        }
        catch { }
    }



    public bool MutliSelect
    {
        get; set
        {
            field = value;
            GridView_Images.SelectionMode = value ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        }
    }


    public string SelectCountText { get; set => SetProperty(ref field, value); }


    private List<FileSystemWatcher> _watchers = new();

    private List<ScreenshotFolder> _folders = new();

    private Dictionary<string, ScreenshotItem> _screenshotDict = new();

    private ObservableCollection<ScreenshotItem>? _screenshotItems;

    public CollectionViewSource ScreenshotViewSource { get; set => SetProperty(ref field, value); } = new() { IsSourceGrouped = true };

    public ObservableCollection<ScreenshotItemGroup> ScreenshotGroups { get; set { if (SetProperty(ref field, value)) { ScreenshotViewSource.Source = value; } } }


    private async void Initialize()
    {
        try
        {
            foreach (var item in _watchers)
            {
                item.Dispose();
            }
            _folders.Clear();
            _screenshotDict.Clear();
            ScreenshotGroups = null!;

            // 默认截图文件夹
            string defaultFolder = AppConfig.ScreenshotFolder;
            if (string.IsNullOrWhiteSpace(defaultFolder))
            {
                defaultFolder = Path.Join(AppConfig.LogFolder, "Screenshots");
            }
            defaultFolder = Path.GetFullPath(defaultFolder);
            Directory.CreateDirectory(defaultFolder);
            if (_folders.FirstOrDefault(x => x.Folder == defaultFolder) is null)
            {
                _watchers.Add(CreateFileSystemWatcher(defaultFolder));
                _folders.Add(new(defaultFolder) { Default = true });
            }

            // 用户自加的文件夹
            string? externalFolder = AppConfig.ScreenshotFolders;
            if (!string.IsNullOrWhiteSpace(externalFolder))
            {
                foreach (var item in externalFolder.Split(';'))
                {
                    string folder = item.Trim();
                    if (Directory.Exists(folder))
                    {
                        folder = Path.GetFullPath(folder);
                        if (_folders.FirstOrDefault(x => x.Folder == folder) is null)
                        {
                            _watchers.Add(CreateFileSystemWatcher(folder));
                            _folders.Add(new(folder));
                        }
                    }
                }
            }

            List<ScreenshotItem> screenshots = new();
            foreach (var folderItem in _folders)
            {
                var files = Directory.GetFiles(folderItem.Folder);
                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    if (_screenshotDict.ContainsKey(name))
                    {
                        continue;
                    }
                    if (ScreenshotHelper.IsSupportedExtension(file) /*&& !File.GetAttributes(file).HasFlag((System.IO.FileAttributes)0x440000)*/)
                    {
                        var item = new ScreenshotItem(file);
                        screenshots.Add(item);
                        _screenshotDict[name] = item;
                    }
                }
            }

            _screenshotItems = new(screenshots.OrderByDescending(x => x.CreationTime).ToList());
            var groups = _screenshotItems.GroupBy(x => x.TimeMonthDay).Select(x => new ScreenshotItemGroup(x.Key, x)).ToList();
            ScreenshotGroups = new(groups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize");
        }
    }


    private FileSystemWatcher CreateFileSystemWatcher(string folder)
    {
        var watcher = new FileSystemWatcher(folder);
        watcher.NotifyFilter = NotifyFilters.FileName;
        foreach (var item in ScreenshotHelper.WatcherFilters)
        {
            watcher.Filters.Add(item);
        }
        watcher.Created += FileSystemWatcher_Created;
        watcher.Deleted += FileSystemWatcher_Deleted;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }


    private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                string name = Path.GetFileName(e.FullPath);
                if (ScreenshotHelper.IsSupportedExtension(e.FullPath) && File.Exists(e.FullPath))
                {
                    await ScreenshotHelper.WaitForFileReleaseAsync(e.FullPath, CancellationToken.None);
                    var item = new ScreenshotItem(e.FullPath);
                    // 字典/集合统一只在 UI 线程动：FSW 线程池续体与 UI 线程并发读写非线程安全集合；
                    // 页面卸载后字段已置空，迟到的续体（await 跨越了卸载）直接丢弃
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_screenshotDict is null) return;
                        if (_screenshotDict.ContainsKey(name)) return;
                        _screenshotDict[name] = item;
                        _screenshotItems ??= new();
                        _screenshotItems.Insert(0, item);
                        if (ScreenshotGroups.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                        {
                            group.Insert(0, item);
                        }
                        else
                        {
                            var newGroup = new ScreenshotItemGroup(item.TimeMonthDay, [item]);
                            ScreenshotGroups.Insert(0, newGroup);
                        }
                        UpdateSelectCountText();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File system watcher created event");
        }
    }


    private void FileSystemWatcher_Deleted(object sender, FileSystemEventArgs e)
    {
        // 同 Created：整体挪到 UI 线程，消除 FSW 线程与 UI 线程的并发读写
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_screenshotDict is null) return;
                string name = Path.GetFileName(e.FullPath);
                if (_screenshotDict.TryGetValue(name, out ScreenshotItem? item))
                {
                    if (e.FullPath == item.FilePath)
                    {
                        if (ScreenshotGroups?.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                        {
                            if (group.Contains(item))
                            {
                                // 字典键带扩展名（Created 用 GetFileName 存），item.Name 是去扩展名的显示名——之前拿它删键永不命中，
                                // 幽灵键残留导致同名文件重建时被 Created 的 ContainsKey 拦截、从图库消失
                                _screenshotDict.Remove(item.FileName);
                                _screenshotItems?.Remove(item);
                                group.Remove(item);
                                if (group.Count == 0)
                                {
                                    ScreenshotGroups.Remove(group);
                                }
                                UpdateSelectCountText();
                            }
                        }
                    }
                }
            }
            catch { }
        });
    }



    [RelayCommand]
    private void OpenImageBatchConvertWindow()
    {
        try
        {
            var list = MutliSelect ? GridView_Images.SelectedItems.Cast<ScreenshotItem>().ToList() : [];
            Frame.Navigate(typeof(ImageBatchConvertWindow), list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open image batch convert window");
        }
    }


    [RelayCommand]
    private async Task ManageScreenshotFolderAsync()
    {
        try
        {
            var dialog = new ScreenshotFolderManageDialog
            {
                Folders = _folders,
                XamlRoot = this.XamlRoot,
            };
            await dialog.ShowAsync();
            if (dialog.FolderChanged)
            {
                string folder = string.Join(';', dialog.Folders.Where(x => x.CanRemove).Select(x => x.Folder));
                AppConfig.ScreenshotFolders = folder;
                Initialize();
                UpdateSelectCountText();
            }
        }
        catch { }
    }


    private void GridView_Images_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.None)
            {
                if (e.ClickedItem is ScreenshotItem item)
                {
                    _ = new ImageViewWindow().ShowWindowAsync(this.XamlRoot.ContentIslandEnvironment.AppWindowId, item, _screenshotItems);
                }
            }
        }
        catch { }
    }


    private async void GridView_Images_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        try
        {
            var list = new List<StorageFile>();
            foreach (var dragItem in e.Items)
            {
                if (dragItem is ScreenshotItem item)
                {
                    if (File.Exists(item.FilePath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        list.Add(file);
                    }
                }
            }
            if (list.Count > 0)
            {
                e.Data.RequestedOperation = DataPackageOperation.Copy;
                e.Data.SetStorageItems(list, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drag image starting");
        }
    }


    private void GridView_Images_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectCountText();
    }


    private void UpdateSelectCountText()
    {
        try
        {
            if (MutliSelect)
            {
                SelectCountText = $"{GridView_Images.SelectedItems.Count}/{_screenshotItems?.Count ?? 0}";
            }
            else
            {
                SelectCountText = $"{_screenshotItems?.Count ?? 0}";
            }
        }
        catch { }
    }



    private void MenuFlyoutItem_ScreenshotInfo_Loading(FrameworkElement sender, object args)
    {
        try
        {
            if (sender.DataContext is ScreenshotItem item)
            {
                item.UpdatePixelSize();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get image pixel size");
        }
    }


    private void MenuFlyoutItem_Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ScreenshotItem item })
            {
                _ = new ImageViewWindow().ShowWindowAsync(this.XamlRoot.ContentIslandEnvironment.AppWindowId, item, _screenshotItems);
            }
        }
        catch { }
    }


    private async void MenuFlyoutItem_CopyFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.Multiple && GridView_Images.SelectedItems.Count > 0)
            {
                var list = new List<StorageFile>();
                foreach (ScreenshotItem item in GridView_Images.SelectedItems.Cast<ScreenshotItem>())
                {
                    if (File.Exists(item.FilePath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        list.Add(file);
                    }
                }
                if (list.Count > 0)
                {
                    ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, list.ToArray());
                    InAppToast.MainWindow?.Success(Lang.ImageViewWindow_CopiedToClipboard, string.Format(Lang.ScreenshotPage_Total0Files, list.Count), 1500);
                }
            }
            else if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, file);
                    InAppToast.MainWindow?.Success(Lang.ImageViewWindow_CopiedToClipboard, null, 1500);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow_FileDoesNotExist, item.FilePath, 5000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy file to clipboard");
        }
    }


    private async void MenuFlyoutItem_CopyImage_Click(object sender, RoutedEventArgs e)
    {
        static string GetSizeString(long size)
        {
            const double KB = 1 << 10;
            const double MB = 1 << 20;
            if (size >= MB)
            {
                return $"{size / MB:F2} MB";
            }
            else
            {
                return $"{size / KB:F2} KB";
            }
        }
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    // 全程内存、零写盘：.jpg 源直接复制原文件；其余解码到内存，
                    // HDR 过截图同款 TonemapToSdr（此前 CLI avifdec/djxl 直转对 PQ 不做色调映射，画面发灰），
                    // 以 CF_DIB 进剪贴板（区域截图同款可靠路径）
                    if (Path.GetExtension(item.FilePath).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, file);
                        InAppToast.MainWindow?.Success($"{Lang.ImageViewWindow_CopiedToClipboard} ({GetSizeString(new FileInfo(item.FilePath).Length)})", null, 1500);
                        return;
                    }
                    using var imageInfo = await ImageLoader.LoadImageAsync(item.FilePath);
                    var bitmap = imageInfo.CanvasBitmap;
                    int width = (int)bitmap.SizeInPixels.Width;
                    int height = (int)bitmap.SizeInPixels.Height;
                    // 统一画进 B8G8R8A8 再取字节：CF_DIB 要 BGRA。HDR 先过 tonemap——
                    // TonemapToSdr 输出 R8G8B8A8（Rgba 字节序）直喂 SetBitmapDib 会红蓝互换
                    CanvasRenderTarget? tonemapped = null;
                    ICanvasImage source = bitmap;
                    if (imageInfo.HDR)
                    {
                        tonemapped = ScreenCaptureService.TonemapToSdr(bitmap, ScreenCaptureService.GetSdrWhiteLevel());
                        source = tonemapped;
                    }
                    byte[] bgra;
                    using (tonemapped)
                    using (var rt = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied))
                    {
                        using var ds = rt.CreateDrawingSession();
                        ds.DrawImage(source);
                        bgra = rt.GetPixelBytes();
                    }
                    ClipboardHelper.SetBitmapDib(width, height, bgra);
                    InAppToast.MainWindow?.Success($"{Lang.ImageViewWindow_CopiedToClipboard} ({width}×{height})", null, 1500);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow_FileDoesNotExist, item.FilePath, 5000);
                }
            }
        }
        catch (Exception ex)
        {
            InAppToast.MainWindow?.Error(Lang.ImageViewWindow_CopyToClipboard, ex.Message);
            _logger.LogError(ex, "Failed to copy file as JPG to clipboard");
        }
    }


    private async void MenuFlyoutItem_OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    var options = new FolderLauncherOptions();
                    options.ItemsToSelect.Add(file);
                    await Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow_FileDoesNotExist, item.FilePath, 5000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file in explorer");
        }
    }


    private async void MenuFlyoutItem_OpenWithDefault_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    await Launcher.LaunchFileAsync(file);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow_FileDoesNotExist, item.FilePath, 5000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file with default application");
        }
    }


    private async void MenuFlyoutItem_OpenWith_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    var options = new LauncherOptions { DisplayApplicationPicker = true };
                    await Launcher.LaunchFileAsync(file, options);
                }
                else
                {
                    InAppToast.MainWindow?.Warning(Lang.ImageViewWindow_FileDoesNotExist, item.FilePath, 5000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file with application picker");
        }
    }


    private async void MenuFlyoutItem_Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridView_Images.SelectionMode is ListViewSelectionMode.Multiple && GridView_Images.SelectedItems.Count > 0)
            {
                var list = GridView_Images.SelectedItems.Cast<ScreenshotItem>().ToList();
                foreach (ScreenshotItem item in list)
                {
                    if (File.Exists(item.FilePath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                        await file.DeleteAsync();
                    }
                    _screenshotItems?.Remove(item);
                    if (ScreenshotGroups?.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                    {
                        if (group.Remove(item))
                        {
                            _screenshotDict.Remove(item.FileName);
                            if (group.Count == 0)
                            {
                                ScreenshotGroups.Remove(group);
                            }
                        }
                    }
                }
            }
            else if (sender is FrameworkElement fe && fe.DataContext is ScreenshotItem item)
            {
                if (File.Exists(item.FilePath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    await file.DeleteAsync();
                }
                _screenshotItems?.Remove(item);
                if (ScreenshotGroups?.FirstOrDefault(x => x.Header == item.TimeMonthDay) is ScreenshotItemGroup group)
                {
                    if (group.Remove(item))
                    {
                        _screenshotDict.Remove(item.FileName);
                        if (group.Count == 0)
                        {
                            ScreenshotGroups.Remove(group);
                        }
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            InAppToast.MainWindow?.Warning(Lang.ImageViewWindow_UnableToDeleteTheFile, Lang.ImageViewWindow_InsufficientPermissionsOrTheFileIsInUse, 5000);
            _logger.LogError(ex, "Failed to delete image file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image file");
        }
    }


}
