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
