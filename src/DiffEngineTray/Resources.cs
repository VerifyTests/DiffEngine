using System.Drawing;
using Resourcer;

public static class Resources
{
    static Resources()
    {
        using var activeStream = Resource.AsStream("icon_active.ico");
        ActiveIcon = new Icon(activeStream);
        using var defaultStream = Resource.AsStream("icon_default.ico");
        DefaultIcon = new Icon(defaultStream);

        using var exitStream = Resource.AsStream("image_exit.png");
        ExitIcon = Image.FromStream(exitStream);
        using var deleteStream = Resource.AsStream("image_delete.png");
        DeleteIcon = Image.FromStream(deleteStream);
        using var acceptAllStream = Resource.AsStream("image_acceptAll.png");
        AcceptAllIcon = Image.FromStream(acceptAllStream);
        using var acceptStream = Resource.AsStream("image_accept.png");
        AcceptIcon = Image.FromStream(acceptStream);
    }

    public static Image AcceptIcon { get; }

    public static Image AcceptAllIcon { get; }

    public static Image DeleteIcon { get; }

    public static Image ExitIcon { get; }

    public static Icon ActiveIcon { get; }

    public static Icon DefaultIcon { get; }
}