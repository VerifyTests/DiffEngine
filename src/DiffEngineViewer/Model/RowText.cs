/// <summary>
/// Row text as a renderer wants it. A tab or a stray newline would break a character grid, and
/// every renderer has to resolve them the same way or the text snapshots stop describing what the
/// pixel ones show.
/// </summary>
static class RowText
{
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
