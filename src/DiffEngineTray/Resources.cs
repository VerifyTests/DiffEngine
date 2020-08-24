using System.Drawing;
using Resourcer;

public static class Resources
{
    public static Icon Icon()
    {
        using var iconStream = Resource.AsStream("icon.ico");
        return new Icon(iconStream);
    }
}