/// <summary>
/// One text drawing setup for the whole head.
/// <para>
/// GDI+ <c>DrawString</c> rather than <c>TextRenderer</c>'s GDI. GDI honours whatever ClearType
/// setting the machine has, which would make a committed pixel baseline reproducible only on the
/// machine that produced it. GDI+ can be pinned to grayscale antialiasing, so it cannot.
/// </para>
/// </summary>
static class Painter
{
    /// <summary>
    /// Typographic rather than the default, whose extra side bearings would stop cell widths
    /// lining up with the measured advance. No <c>NoClip</c>, so a long line is clipped to its
    /// column instead of bleeding into the next one.
    /// </summary>
    public static readonly StringFormat Format = BuildFormat();

    static readonly Dictionary<Color, SolidBrush> brushes = [];

    static StringFormat BuildFormat()
    {
        var format = (StringFormat) StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap;
        // GenericTypographic arrives with NoClip and LineLimit set, and both have to go. NoClip is
        // what let a long file name in the queue paint straight over the pane beside it. LineLimit
        // then matters, because once clipping is on it turns a rect a pixel short of the measured
        // line height into nothing drawn at all rather than a line clipped at the bottom.
        format.FormatFlags &= ~(StringFormatFlags.NoClip | StringFormatFlags.LineLimit);
        format.Trimming = StringTrimming.None;
        return format;
    }

    public static void Prepare(Graphics graphics)
    {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
    }

    /// <summary>
    /// Cached because a frame draws one string per cell, and a brush per cell per frame is the
    /// kind of allocation that turns an idle window into a busy one.
    /// </summary>
    public static SolidBrush Brush(Color colour)
    {
        if (brushes.TryGetValue(colour, out var brush))
        {
            return brush;
        }

        brush = new(colour);
        brushes.Add(colour, brush);
        return brush;
    }

    public static void Draw(Graphics graphics, string text, Font font, Color colour, RectangleF bounds)
    {
        if (text.Length == 0)
        {
            return;
        }

        graphics.DrawString(text, font, Brush(colour), bounds, Format);
    }
}
