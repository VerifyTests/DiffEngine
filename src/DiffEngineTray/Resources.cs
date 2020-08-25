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
    }

    public static Icon ActiveIcon { get; }

    public static Icon DefaultIcon { get; }
}