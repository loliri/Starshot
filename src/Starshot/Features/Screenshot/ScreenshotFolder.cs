namespace Starshot.Features.Screenshot;

public class ScreenshotFolder
{

    public string Folder { get; set; }

    public bool Default { get; set; }

    public bool CanRemove => !Default;


    public ScreenshotFolder(string folder)
    {
        Folder = folder;
    }

}