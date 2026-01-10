public static class Images
{
    static Stream GetStream(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"DiffEngineTray.Images.{name}")!;

    static Images()
    {
        using var activeStream = GetStream("active.ico");
        Active = new(activeStream);
        using var defaultStream = GetStream("default.ico");
        Default = new(defaultStream);
        using var exitStream = GetStream("exit.png");
        Exit = Image.FromStream(exitStream);
        using var deleteStream = GetStream("delete.png");
        Delete = Image.FromStream(deleteStream);
        using var acceptAllStream = GetStream("acceptAll.png");
        AcceptAll = Image.FromStream(acceptAllStream);
        using var acceptStream = GetStream("accept.png");
        Accept = Image.FromStream(acceptStream);
        using var discardStream = GetStream("discard.png");
        Discard = Image.FromStream(discardStream);
        using var vsStream = GetStream("vs.png");
        VisualStudio = Image.FromStream(vsStream);
        using var folderStream = GetStream("folder.png");
        Folder = Image.FromStream(folderStream);
        using var optionsStream = GetStream("cogs.png");
        Options = Image.FromStream(optionsStream);
        using var linkStream = GetStream("link.png");
        Link = Image.FromStream(linkStream);
    }

    public static Image VisualStudio { get; }
    public static Image Link { get; }
    public static Image Discard { get; }
    public static Image Accept { get; }
    public static Image AcceptAll { get; }
    public static Image Delete { get; }
    public static Image Exit { get; }
    public static Image Folder { get; }
    public static Image Options { get; }
    public static Icon Active { get; }
    public static Icon Default { get; }
}
