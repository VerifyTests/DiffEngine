/// <summary>
/// The Windows head's pictures, which this project embeds as well: the icons' bitmaps as the
/// tray's pixels, and the menu's glyphs as the PNGs they are, which dbusmenu carries as they are.
/// </summary>
static class TrayImages
{
    static readonly byte[] defaultIcon = Bytes("default.ico");
    static readonly byte[] activeIcon = Bytes("active.ico");

    static readonly Dictionary<string, byte[]> glyphs = new()
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
    /// At 48 pixels only, the one size the icons have, which a panel scales to its own.
    /// </summary>
    public static byte[]? Pixels(TrayIcon icon, int size) =>
        IconPixels.FromIco(icon == TrayIcon.Active ? activeIcon : defaultIcon, size);

    /// <summary>
    /// 24 pixels whatever size is asked for, the size the Windows menu draws them at.
    /// </summary>
    public static byte[]? Glyph(string name, int size) =>
        glyphs.GetValueOrDefault(name);

    static byte[] Bytes(string name)
    {
        using var stream = typeof(TrayImages).Assembly.GetManifestResourceStream($"DiffEngineTray.Images.{name}")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
