/// <summary>
/// Decoded on first use rather than all together.
/// <para>
/// A static constructor decoded all eleven the moment anything touched one, and the first thing to
/// touch one is the tray icon - before the icon exists, so the cost is time the user spends looking
/// at an empty notification area. Only <see cref="Default" /> is wanted then. Of the nine menu
/// images, four are on the fixed menu items and five are only ever shown when something is pending,
/// so most of that decoding was for a menu that may never have anything in it.
/// </para>
/// <para>
/// The <see cref="Lazy{T}" /> fields still initialise together, which is fine: constructing one
/// allocates and nothing more. Thread safe by default, and it has to be, because the icon is set
/// from the scan timer as well as from the UI thread.
/// </para>
/// </summary>
public static class Images
{
    public static Icon Active => active.Value;
    public static Icon Default => defaultIcon.Value;
    public static Image Exit => exit.Value;
    public static Image Delete => delete.Value;
    public static Image AcceptAll => acceptAll.Value;
    public static Image Accept => accept.Value;
    public static Image Discard => discard.Value;
    public static Image VisualStudio => visualStudio.Value;
    public static Image Folder => folder.Value;
    public static Image Options => options.Value;
    public static Image Link => link.Value;

    static Lazy<Icon> active = LazyIcon("active.ico");
    static Lazy<Icon> defaultIcon = LazyIcon("default.ico");
    static Lazy<Image> exit = LazyImage("exit.png");
    static Lazy<Image> delete = LazyImage("delete.png");
    static Lazy<Image> acceptAll = LazyImage("acceptAll.png");
    static Lazy<Image> accept = LazyImage("accept.png");
    static Lazy<Image> discard = LazyImage("discard.png");
    static Lazy<Image> visualStudio = LazyImage("vs.png");
    static Lazy<Image> folder = LazyImage("folder.png");
    static Lazy<Image> options = LazyImage("cogs.png");
    static Lazy<Image> link = LazyImage("link.png");

    static Lazy<Icon> LazyIcon(string name) =>
        new(() =>
        {
            using var stream = GetStream(name);
            return new Icon(stream);
        });

    static Lazy<Image> LazyImage(string name) =>
        new(() =>
        {
            using var stream = GetStream(name);
            return Image.FromStream(stream);
        });

    static Stream GetStream(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"DiffEngineTray.Images.{name}")!;
}
