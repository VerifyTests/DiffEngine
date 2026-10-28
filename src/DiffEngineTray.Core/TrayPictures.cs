/// <summary>
/// The Windows head's pictures, which the core embeds as well for the heads with no WinForms: the
/// icons as the PNGs macOS takes and the bitmaps a Linux tray's pixels are read from, and the
/// menu's glyphs as the PNGs they are, which dbusmenu and AppKit both take as they are.
/// </summary>
static class TrayPictures
{
    static readonly byte[] defaultIco = Bytes("default.ico");
    static readonly byte[] activeIco = Bytes("active.ico");
    static readonly byte[] defaultPng = Bytes("default.png");
    static readonly byte[] activePng = Bytes("active.png");

    public static IReadOnlyDictionary<string, byte[]> Glyphs { get; } = new Dictionary<string, byte[]>
    {
        ["exit"] = Bytes("exit.png"),
        ["options"] = Bytes("cogs.png"),
        ["folder"] = Bytes("folder.png"),
        ["link"] = Bytes("link.png"),
        ["solution"] = Bytes("vs.png"),
        ["delete"] = Bytes("delete.png"),
        ["accept"] = Bytes("accept.png"),
        ["acceptAll"] = Bytes("acceptAll.png"),
        ["discard"] = Bytes("discard.png")
    };

    /// <summary>
    /// At 48 pixels, the one size the icons have, which a panel scales to its own.
    /// </summary>
    public static byte[]? Pixels(TrayIcon icon, int size) =>
        IconPixels.FromIco(icon == TrayIcon.Active ? activeIco : defaultIco, size);

    /// <summary>
    /// 48 pixels, which StatusItem draws at the menu bar's height, sharp on any display.
    /// </summary>
    public static byte[] Png(TrayIcon icon) =>
        icon == TrayIcon.Active ? activePng : defaultPng;

    /// <summary>
    /// 24 pixels whatever size is asked for, the size the Windows menu draws them at.
    /// </summary>
    public static byte[]? Glyph(string name, int size) =>
        Glyphs.GetValueOrDefault(name);

    static byte[] Bytes(string name)
    {
        using var stream = typeof(TrayPictures).Assembly.GetManifestResourceStream($"DiffEngineTray.Images.{name}")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
