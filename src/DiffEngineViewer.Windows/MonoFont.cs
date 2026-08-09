/// <summary>
/// Registers the embedded JetBrains Mono with GDI+ and measures one character cell from it.
/// <para>
/// The collection and the pinned bytes are process wide and never released: GDI+ reads the memory
/// behind an <c>AddMemoryFont</c> registration for as long as any font from it is alive, and every
/// font here lives until the process ends.
/// </para>
/// </summary>
static class MonoFont
{
    const float pointSize = 11f;

    static readonly PrivateFontCollection collection = new();
    static readonly FontFamily family = Register();

    public static Font Create() =>
        new(family, pointSize, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>
    /// The advance width and line height of one cell, which is what the app's character grid is
    /// counted in. Measured rather than assumed, so a scaled display reports a grid that actually
    /// fits, which is the bug the hardcoded 9 by 18 in the native head still has.
    /// </summary>
    public static Size Cell(Graphics graphics, Font font)
    {
        var width = graphics.MeasureString("M", font, PointF.Empty, Painter.Format).Width;
        return new(
            Math.Max(1, (int) Math.Round(width)),
            Math.Max(1, (int) Math.Ceiling(font.GetHeight(graphics))));
    }

    static FontFamily Register()
    {
        var bytes = EmbeddedFont.Bytes();
        if (bytes.Length == 0)
        {
            // Nothing embedded, so take whatever monospaced face the machine has.
            return new(GenericFontFamilies.Monospace);
        }

        // Pinned for the process lifetime rather than copied to unmanaged memory and freed: GDI+
        // keeps reading this buffer, so freeing it is what produces the classic garbled glyphs.
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        collection.AddMemoryFont(handle.AddrOfPinnedObject(), bytes.Length);
        return collection.Families[0];
    }
}
