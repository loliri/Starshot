using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starshot.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;


namespace Starshot.Features.Screenshot;

[INotifyPropertyChanged]
public sealed partial class ScreenshotFolderManageDialog : ContentDialog
{


    private ILogger<ScreenshotFolderManageDialog> _logger = AppConfig.GetLogger<ScreenshotFolderManageDialog>();


    public List<ScreenshotFolder> Folders { get; set; }





    public ScreenshotFolderManageDialog()
    {
        InitializeComponent();
        Loaded += ScreenshotFolderManageDialog_Loaded;
        Unloaded += ScreenshotFolderManageDialog_Unloaded;
    }




    public ObservableCollection<ScreenshotFolder> ScreenshotFolders { get; set; } = new();


    public bool FolderChanged { get; set; }


    public bool CanSave { get; set => SetProperty(ref field, value); }



    private void ScreenshotFolderManageDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Folders is not null)
            {
                foreach (var item in Folders)
                {
                    ScreenshotFolders.Add(item);
                }
            }
        }
        catch { }
    }



    private void ScreenshotFolderManageDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ScreenshotFolders.Clear();
            ScreenshotFolders = null!;
        }
        catch { }
    }




    [RelayCommand]
    private async Task AddFolderAsync()
    {
        try
        {
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (Directory.Exists(folder))
            {
                if (ScreenshotFolders.FirstOrDefault(x => x.Folder == folder) is null)
                {
                    ScreenshotFolders.Add(new ScreenshotFolder(folder));
                    CanSave = true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add screenshot folder.");
        }
    }




    private async void Button_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ScreenshotFolder folder })
            {
                if (Directory.Exists(folder.Folder))
                {
                    await Launcher.LaunchFolderPathAsync(folder.Folder);
                }
            }
        }
        catch { }
    }



    private void Button_RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ScreenshotFolder folder })
            {
                ScreenshotFolders.Remove(folder);
                CanSave = true;
            }
        }
        catch { }
    }




    [RelayCommand]
    private void Save()
    {
        try
        {
            FolderChanged = true;
            Folders ??= new();
            Folders.Clear();
            Folders.AddRange(ScreenshotFolders.Where(x => x.CanRemove));
            this.Hide();
        }
        catch
        {
            this.Hide();
        }
    }



    [RelayCommand]
    private void Cancel()
    {
        this.Hide();
    }


}
