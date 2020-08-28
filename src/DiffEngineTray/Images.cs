using System.Drawing;
using Resourcer;

public static class Images
{
    static Images()
    {
        using var activeStream = Resource.AsStream("Images/active.ico");
        Active = new Icon(activeStream);
        using var defaultStream = Resource.AsStream("Images/default.ico");
        Default = new Icon(defaultStream);
        using var exitStream = Resource.AsStream("Images/exit.png");
        Exit = Image.FromStream(exitStream);
        using var deleteStream = Resource.AsStream("Images/delete.png");
        Delete = Image.FromStream(deleteStream);
        using var acceptAllStream = Resource.AsStream("Images/acceptAll.png");
        AcceptAll = Image.FromStream(acceptAllStream);
        using var acceptStream = Resource.AsStream("Images/accept.png");
        Accept = Image.FromStream(acceptStream);
        using var clearStream = Resource.AsStream("Images/clear.png");
        Clear = Image.FromStream(clearStream);
        using var folderStream = Resource.AsStream("Images/folder.png");
        Folder = Image.FromStream(folderStream);
        using var optionsStream = Resource.AsStream("Images/cogs.png");
        Options = Image.FromStream(optionsStream);
    }

    public static Image Clear { get; }
    public static Image Accept { get; }
    public static Image AcceptAll { get; }
    public static Image Delete { get; }
    public static Image Exit { get; }
    public static Image Folder { get; }
    public static Image Options { get; }
    public static Icon Active { get; }
    public static Icon Default { get; }
}