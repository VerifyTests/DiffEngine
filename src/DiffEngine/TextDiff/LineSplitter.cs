/// <summary>
/// Splits text into lines on <c>\r\n</c>, <c>\r</c> or <c>\n</c>, as ranges of the original string,
/// so that no line is copied until something asks for it as a string.
/// <para>
/// Empty text is no lines at all rather than one empty line, so an empty side of a diff is all
/// additions or removals with nothing unchanged. A trailing terminator is a trailing empty line,
/// so a text that gained or lost its final newline differs by one line rather than not at all.
/// </para>
/// </summary>
static class LineSplitter
{
    public static LineRange[] Split(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var span = text.AsSpan();
        var count = 1;
        var start = 0;
        while (NextBreak(span, start, out var next) >= 0)
        {
            count++;
            start = next;
        }

        var lines = new LineRange[count];
        start = 0;
        for (var index = 0; index < count; index++)
        {
            var end = NextBreak(span, start, out var next);
            if (end < 0)
            {
                lines[index] = new(start, span.Length - start);
                break;
            }

            lines[index] = new(start, end - start);
            start = next;
        }

        return lines;
    }

    /// <summary>
    /// Where the line starting at <paramref name="start"/> ends, or -1 when it runs to the end of
    /// the text. <paramref name="next"/> is where the following line starts.
    /// </summary>
    static int NextBreak(CharSpan span, int start, out int next)
    {
        var offset = span[start..].IndexOfAny('\r', '\n');
        if (offset < 0)
        {
            next = span.Length;
            return -1;
        }

        var end = start + offset;
        next = end + 1;
        if (span[end] == '\r' &&
            next < span.Length &&
            span[next] == '\n')
        {
            next++;
        }

        return end;
    }
}
