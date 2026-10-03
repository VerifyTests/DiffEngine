/// <summary>
/// Row text as a renderer wants it. A tab or a stray newline would break a character grid, and
/// every renderer has to resolve them the same way or the text snapshots stop describing what the
/// pixel ones show.
/// </summary>
static class RowText
{
    /// <summary>
    /// No more of a row than can be on screen. Nothing scrolls horizontally, so a character past
    /// the window's width is never drawn - but a renderer still laid out and measured the whole
    /// line, and a one megabyte minified line cost most of a second a paint. Every character is
    /// at least one cell wide, so the window's width in characters is always enough. Never ends
    /// between the halves of a surrogate pair.
    /// </summary>
    public static string Clip(string text, int cells)
    {
        if (text.Length <= cells)
        {
            return text;
        }

        var length = Math.Max(0, cells);
        if (length > 0 &&
            char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }

    /// <summary>
    /// The start of a row as a renderer draws it: flattened, and cut where its first
    /// <paramref name="cells"/> cells end. Exactly what flattening the whole row and cutting it at
    /// <see cref="CellGrid.Index"/> gives, so a wide character, or a character and its marks, is
    /// kept whole or not at all, and whatever is segmented and drawn from it lands where it would
    /// have from the whole row.
    /// <para>
    /// For a renderer that knows how many cells its pane holds, where <see cref="Clip"/> counts
    /// characters and is handed a row already flattened. Read from the front and only as far as
    /// it takes, because this is asked of every row on every paint and a row can be a megabyte:
    /// flattening all of one copies it when it holds a tab, and finding its cells walks all of it
    /// when it holds anything but ASCII.
    /// </para>
    /// </summary>
    public static string Shown(string text, int cells)
    {
        // One character past the cells asked for, which is what says the cut has a character
        // after it rather than being wherever the text read so far ran out.
        var take = Math.Min(Math.Max(cells, 0), text.Length) + 1;
        while (take < text.Length)
        {
            // Not between the halves of a surrogate pair. The first half alone reads as a
            // character of its own, where the pair may be a mark on the character before it.
            var end = char.IsHighSurrogate(text[take - 1]) ? take + 1 : take;
            var start = Flatten(text[..end]);
            var cut = CellGrid.Index(start, cells);
            if (cut < start.Length)
            {
                return start[..cut];
            }

            // Not enough yet, which takes characters that fill no cell: marks, joiners, a
            // carriage return. Twice as much next, so a row of nothing else costs two reads of
            // it at most rather than one for every cell.
            take *= 2;
        }

        var whole = Flatten(text);
        return whole[..CellGrid.Index(whole, cells)];
    }

    public static string Flatten(string text)
    {
        if (text.AsSpan().IndexOfAny('\t', '\r', '\n') < 0)
        {
            return text;
        }

        return text
            .Replace("\t", "    ")
            .Replace("\r", "")
            .Replace("\n", " ");
    }
}
