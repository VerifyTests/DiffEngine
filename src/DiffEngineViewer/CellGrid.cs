using System.Buffers;
using System.Globalization;

/// <summary>
/// Where the characters of a row's flattened text sit on the grid of character cells every head
/// draws, which is what a selection's columns count.
/// <para>
/// Decided here, once, rather than read back out of each head's text layout. A head used to draw a
/// row as one string and let its font decide where each character went, while a selection counted
/// one cell per code point: right for the monospace font's own characters, and wrong wherever a
/// character came from somewhere else. CJK falls back to a font 1.83 cells wide on Windows, a
/// combining mark takes no room at all, and Core Text substitutes fonts with widths of their own,
/// so past the first such character the highlight, the hit test and the copy each described a
/// different run. Now a head draws a row as <see cref="Segments"/>, each at its column, and a
/// character is wherever the grid says it is whatever its font makes of it.
/// </para>
/// <para>
/// A cluster is a character with the marks that attach to it: combining marks, variation
/// selectors and joiners take no cell of their own, and a character after a zero width joiner
/// belongs to the one before it, so an emoji sequence is one cluster. A wide character (East Asian
/// Wide or Fullwidth, and emoji) takes two cells, anything else one. Selection columns never land
/// inside a cluster (<see cref="Snap"/>), so what is highlighted and what is copied cannot differ
/// by half a character.
/// </para>
/// </summary>
static class CellGrid
{
    /// <summary>
    /// A run of <paramref name="Length"/> UTF-16 units at <paramref name="Start"/> in the flattened
    /// text, drawn starting at cell <paramref name="Column"/>.
    /// </summary>
    public readonly record struct Segment(int Start, int Length, int Column);

    /// <summary>
    /// How to draw <paramref name="flattened"/>: runs of characters every head's monospace font
    /// draws a cell wide, which can be drawn as one string, and every other cluster on its own at
    /// the column the grid gives it. A row of printable ASCII, which is nearly every row, is one
    /// segment at column 0, drawn exactly as a whole row always was.
    /// </summary>
    public static IReadOnlyList<Segment> Segments(string flattened)
    {
        if (flattened.Length == 0)
        {
            return [];
        }

        if (IsPlain(flattened))
        {
            return [new(0, flattened.Length, 0)];
        }

        var segments = new List<Segment>();
        var column = 0;
        var runStart = -1;
        var runColumn = 0;
        foreach (var cluster in Clusters(flattened))
        {
            if (cluster.Simple)
            {
                if (runStart < 0)
                {
                    runStart = cluster.Start;
                    runColumn = column;
                }
            }
            else
            {
                if (runStart >= 0)
                {
                    segments.Add(new(runStart, cluster.Start - runStart, runColumn));
                    runStart = -1;
                }

                segments.Add(new(cluster.Start, cluster.Length, column));
            }

            column += cluster.Width;
        }

        if (runStart >= 0)
        {
            segments.Add(new(runStart, flattened.Length - runStart, runColumn));
        }

        return segments;
    }

    /// <summary>
    /// How many cells <paramref name="flattened"/> takes.
    /// </summary>
    public static int Cells(string flattened)
    {
        if (IsPlain(flattened))
        {
            return flattened.Length;
        }

        var cells = 0;
        foreach (var cluster in Clusters(flattened))
        {
            cells += cluster.Width;
        }

        return cells;
    }

    /// <summary>
    /// The first cluster boundary at or after <paramref name="cell"/>, as a column. A column inside
    /// a wide cluster moves to its end, so a selection takes a wide character whole or not at all.
    /// </summary>
    public static int Snap(string flattened, int cell) =>
        Boundary(flattened, cell).Column;

    /// <summary>
    /// Where in <paramref name="flattened"/> the boundary <see cref="Snap"/> finds starts: never
    /// inside a surrogate pair, and never between a character and its marks.
    /// </summary>
    public static int Index(string flattened, int cell) =>
        Boundary(flattened, cell).Index;

    static (int Index, int Column) Boundary(string flattened, int cell)
    {
        if (cell <= 0)
        {
            return (0, 0);
        }

        if (IsPlain(flattened))
        {
            var plain = Math.Min(cell, flattened.Length);
            return (plain, plain);
        }

        var column = 0;
        foreach (var cluster in Clusters(flattened))
        {
            if (column >= cell)
            {
                return (cluster.Start, column);
            }

            column += cluster.Width;
        }

        return (flattened.Length, column);
    }

    /// <summary>
    /// Printable ASCII throughout, which every head draws one cell a character with nothing to
    /// work out.
    /// </summary>
    static bool IsPlain(string text)
    {
        foreach (var character in text)
        {
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    readonly record struct Cluster(int Start, int Length, int Width, bool Simple);

    static IEnumerable<Cluster> Clusters(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            var rune = Read(text, ref index);
            // A mark with nothing before it for it to sit on still takes a cell, or it could be
            // neither drawn anywhere nor selected
            var width = !ZeroWidth(rune) && Wide(rune) ? 2 : 1;
            var simple = Simple(rune);
            while (index < text.Length)
            {
                var next = index;
                var attached = Read(text, ref next);
                if (!ZeroWidth(attached))
                {
                    break;
                }

                index = next;
                simple = false;
                // What a joiner joins is part of the same cluster: a family emoji is one picture
                if (attached.Value == 0x200D &&
                    index < text.Length)
                {
                    Read(text, ref index);
                }
            }

            yield return new(start, index - start, width, simple);
        }
    }

    static Rune Read(string text, ref int index)
    {
        if (Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed) != OperationStatus.Done)
        {
            // A lone surrogate: one unit, drawn as whatever the head draws for one
            index++;
            return Rune.ReplacementChar;
        }

        index += consumed;
        return rune;
    }

    /// <summary>
    /// Takes no cell: combining and enclosing marks, and format characters such as the zero width
    /// joiner and space. Variation selectors are nonspacing marks.
    /// </summary>
    static bool ZeroWidth(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format;

    /// <summary>
    /// A character the embedded monospace font has, and so draws exactly one cell wide in every
    /// head, which lets a run of them be drawn as one string: Latin with its extensions, Greek and
    /// Cyrillic. Everything else is drawn on its own, at its column, because where a fallback font
    /// would put the character after it is not something the grid can know.
    /// </summary>
    static bool Simple(Rune rune)
    {
        var value = rune.Value;
        if (ZeroWidth(rune))
        {
            return false;
        }

        return value is
            >= 0x20 and <= 0x7E or
            >= 0xA0 and <= 0x24F or
            >= 0x370 and <= 0x3FF or
            >= 0x400 and <= 0x52F;
    }

    /// <summary>
    /// East Asian Wide and Fullwidth, and the emoji blocks, by range. Not the whole Unicode
    /// property, which .NET does not expose, but the blocks where wide characters actually live.
    /// </summary>
    static bool Wide(Rune rune)
    {
        var value = rune.Value;
        foreach (var (first, last) in wide)
        {
            if (value < first)
            {
                return false;
            }

            if (value <= last)
            {
                return true;
            }
        }

        return false;
    }

    // In ascending order, which Wide relies on to stop early
    static readonly (int First, int Last)[] wide =
    [
        (0x1100, 0x115F),
        (0x231A, 0x231B),
        (0x2329, 0x232A),
        (0x23E9, 0x23EC),
        (0x23F0, 0x23F0),
        (0x23F3, 0x23F3),
        (0x25FD, 0x25FE),
        (0x2614, 0x2615),
        (0x2648, 0x2653),
        (0x267F, 0x267F),
        (0x2693, 0x2693),
        (0x26A1, 0x26A1),
        (0x26AA, 0x26AB),
        (0x26BD, 0x26BE),
        (0x26C4, 0x26C5),
        (0x26CE, 0x26CE),
        (0x26D4, 0x26D4),
        (0x26EA, 0x26EA),
        (0x26F2, 0x26F3),
        (0x26F5, 0x26F5),
        (0x26FA, 0x26FA),
        (0x26FD, 0x26FD),
        (0x2705, 0x2705),
        (0x270A, 0x270B),
        (0x2728, 0x2728),
        (0x274C, 0x274C),
        (0x274E, 0x274E),
        (0x2753, 0x2755),
        (0x2757, 0x2757),
        (0x2795, 0x2797),
        (0x27B0, 0x27B0),
        (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C),
        (0x2B50, 0x2B50),
        (0x2B55, 0x2B55),
        (0x2E80, 0x303E),
        (0x3041, 0x33FF),
        (0x3400, 0x4DBF),
        (0x4E00, 0x9FFF),
        (0xA000, 0xA4CF),
        (0xA960, 0xA97F),
        (0xAC00, 0xD7A3),
        (0xF900, 0xFAFF),
        (0xFE10, 0xFE19),
        (0xFE30, 0xFE6F),
        (0xFF00, 0xFF60),
        (0xFFE0, 0xFFE6),
        (0x16FE0, 0x16FE4),
        (0x17000, 0x18AFF),
        (0x1B000, 0x1B2FF),
        (0x1F004, 0x1F004),
        (0x1F0CF, 0x1F0CF),
        (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A),
        (0x1F200, 0x1F251),
        (0x1F300, 0x1F64F),
        (0x1F680, 0x1F6FF),
        (0x1F7E0, 0x1F7EB),
        (0x1F90C, 0x1F9FF),
        (0x1FA70, 0x1FAFF),
        (0x20000, 0x2FFFD),
        (0x30000, 0x3FFFD)
    ];
}
