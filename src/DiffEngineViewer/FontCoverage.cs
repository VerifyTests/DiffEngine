using System.Buffers.Binary;

/// <summary>
/// Which characters the embedded font draws a cell wide: the ones it has a glyph for, at the
/// advance every other glyph of a monospace font has. Read out of the font's own tables, once.
/// <para>
/// This is what <see cref="CellGrid"/> needs to know to draw a run of characters as one string,
/// and it used to be a guess written as four ranges: Latin with its extensions, Greek, Cyrillic.
/// The guess was wrong both ways. It left out everything else the font has, so a row of box
/// drawing, arrows or typographic punctuation was cut into a segment a character, each laid out on
/// its own by every head. And it took in characters the font does not have, a ƀ or a Ѡ, which a
/// head then drew from some other font at some other width in the middle of a run, moving
/// everything after it off the grid.
/// </para>
/// <para>
/// Asked of the bytes rather than written out as a table, so replacing the font file cannot leave
/// a list behind that describes the old one.
/// </para>
/// </summary>
static class FontCoverage
{
    static readonly Lazy<Coverage> embedded = new(() => Read(EmbeddedFont.Bytes()));

    /// <summary>
    /// Whether the embedded font has a glyph for the code point that is one cell wide.
    /// </summary>
    public static bool Has(int codePoint) =>
        embedded.Value.Has(codePoint);

    /// <summary>
    /// The basic plane as a bit a code point, which is where nearly everything asked about is, and
    /// the few ranges past it in order.
    /// </summary>
    internal sealed class Coverage(ulong[] plane, (int First, int Last)[] beyond)
    {
        public static readonly Coverage None = new(new ulong[1024], []);

        public bool Has(int codePoint)
        {
            if (codePoint < 0)
            {
                return false;
            }

            if (codePoint <= 0xFFFF)
            {
                return (plane[codePoint >> 6] & (1UL << (codePoint & 63))) != 0;
            }

            foreach (var (first, last) in beyond)
            {
                if (codePoint < first)
                {
                    return false;
                }

                if (codePoint <= last)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The coverage of a font file. Nothing, rather than a throw, for bytes that are not a font
    /// this can read: every character is then drawn on its own at its column, which is slower and
    /// still right. Internal so the tests can hand it bytes that are not the embedded font.
    /// </summary>
    internal static Coverage Read(byte[] font)
    {
        try
        {
            return Parse(font);
        }
        catch (Exception exception)
            when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
        {
            return Coverage.None;
        }
    }

    static Coverage Parse(byte[] font)
    {
        var cmap = Table(font, "cmap");
        var hhea = Table(font, "hhea");
        var hmtx = Table(font, "hmtx");
        // The advances are a list one per glyph for the first so many glyphs, and the last of
        // them stands for every glyph after that
        var metrics = U16(font, hhea + 34);
        if (metrics == 0)
        {
            throw new InvalidDataException("The font has no horizontal metrics.");
        }

        int Advance(int glyph) =>
            U16(font, hmtx + 4 * Math.Min(glyph, metrics - 1));

        var plane = new ulong[1024];
        var beyond = new List<(int First, int Last)>();
        var mapped = new List<(int CodePoint, int Glyph)>();
        Mappings(font, cmap, mapped);
        var cell = -1;
        foreach (var (codePoint, glyph) in mapped)
        {
            if (codePoint == ' ')
            {
                cell = Advance(glyph);
            }
        }

        foreach (var (codePoint, glyph) in mapped)
        {
            if (glyph == 0 ||
                Advance(glyph) != cell)
            {
                continue;
            }

            if (codePoint <= 0xFFFF)
            {
                plane[codePoint >> 6] |= 1UL << (codePoint & 63);
            }
            else if (beyond.Count > 0 &&
                     beyond[^1].Last == codePoint - 1)
            {
                beyond[^1] = (beyond[^1].First, codePoint);
            }
            else
            {
                beyond.Add((codePoint, codePoint));
            }
        }

        return new(plane, beyond.ToArray());
    }

    /// <summary>
    /// Every code point the font maps and the glyph it maps to, in ascending order, from the
    /// widest subtable there is: the one that can name code points past the basic plane when the
    /// font has one, and the basic plane's own otherwise.
    /// </summary>
    static void Mappings(byte[] font, int cmap, List<(int CodePoint, int Glyph)> mapped)
    {
        var tables = U16(font, cmap + 2);
        var segmented = -1;
        for (var index = 0; index < tables; index++)
        {
            var subtable = cmap + (int) U32(font, cmap + 4 + index * 8 + 4);
            var format = U16(font, subtable);
            if (format == 12)
            {
                Groups(font, subtable, mapped);
                return;
            }

            if (format == 4)
            {
                segmented = subtable;
            }
        }

        if (segmented < 0)
        {
            throw new InvalidDataException("The font has no character map this reads.");
        }

        Segments(font, segmented, mapped);
    }

    // Format 12: runs of code points, each run mapped to a run of glyphs.
    static void Groups(byte[] font, int subtable, List<(int CodePoint, int Glyph)> mapped)
    {
        var groups = (int) U32(font, subtable + 12);
        for (var index = 0; index < groups; index++)
        {
            var group = subtable + 16 + index * 12;
            var first = (int) U32(font, group);
            // No further than Unicode goes, so a table that says otherwise is not walked
            var last = (int) Math.Min(U32(font, group + 4), 0x10FFFF);
            var glyph = (int) U32(font, group + 8);
            for (var codePoint = Math.Max(first, 0); codePoint <= last; codePoint++)
            {
                mapped.Add((codePoint, glyph + codePoint - first));
            }
        }
    }

    // Format 4: segments of the basic plane, each mapped by an offset or through a table.
    static void Segments(byte[] font, int subtable, List<(int CodePoint, int Glyph)> mapped)
    {
        var count = U16(font, subtable + 6) / 2;
        var ends = subtable + 14;
        var starts = ends + count * 2 + 2;
        var deltas = starts + count * 2;
        var offsets = deltas + count * 2;
        for (var index = 0; index < count; index++)
        {
            var first = U16(font, starts + index * 2);
            var last = U16(font, ends + index * 2);
            var delta = U16(font, deltas + index * 2);
            var offset = U16(font, offsets + index * 2);
            for (var codePoint = first; codePoint <= last && codePoint != 0xFFFF; codePoint++)
            {
                int glyph;
                if (offset == 0)
                {
                    glyph = (codePoint + delta) & 0xFFFF;
                }
                else
                {
                    glyph = U16(font, offsets + index * 2 + offset + (codePoint - first) * 2);
                    if (glyph != 0)
                    {
                        glyph = (glyph + delta) & 0xFFFF;
                    }
                }

                mapped.Add((codePoint, glyph));
            }
        }
    }

    static int Table(byte[] font, string tag)
    {
        var tables = U16(font, 4);
        for (var index = 0; index < tables; index++)
        {
            var record = 12 + index * 16;
            if (font[record] == tag[0] &&
                font[record + 1] == tag[1] &&
                font[record + 2] == tag[2] &&
                font[record + 3] == tag[3])
            {
                return (int) U32(font, record + 8);
            }
        }

        throw new InvalidDataException($"The font has no {tag} table.");
    }

    static int U16(byte[] font, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offset, 2));

    static uint U32(byte[] font, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(offset, 4));
}
