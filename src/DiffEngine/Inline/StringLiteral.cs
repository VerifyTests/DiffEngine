/// <summary>
/// The parts of writing and reading a string literal that C# and F# now share.
/// <para>
/// They share them because they write the same shapes: a regular literal for one line, and an
/// indented multi-line one whose first line and closing indentation are layout rather than
/// content. C# has that second form in the language; F# has it by agreement between whoever
/// writes the literal and whoever reads it back, which is what <see cref="FsStringLiteral"/>
/// documents. Either way the text is the same, so it is produced and consumed here once.
/// </para>
/// <para>
/// What is left per language is small and real: which delimiter can hold the content, and what
/// the escapes in a regular literal mean.
/// </para>
/// </summary>
static class StringLiteral
{
    /// <summary>
    /// Renders content as a regular literal, escaping what the form cannot hold verbatim.
    /// <para>
    /// One escape set for both languages, which costs a NUL written as <c>\u0000</c> rather than
    /// C#'s shorter <c>\0</c> - F# has no <c>\0</c>, and a snapshot containing a NUL is not worth
    /// a second implementation.
    /// </para>
    /// </summary>
    public static string RenderRegular(string content)
    {
        var builder = new StringBuilder(content.Length + 2);
        builder.Append('"');
        foreach (var ch in content)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    continue;
                case '"':
                    builder.Append("\\\"");
                    continue;
                case '\n':
                    builder.Append("\\n");
                    continue;
                case '\r':
                    builder.Append("\\r");
                    continue;
                case '\a':
                    builder.Append("\\a");
                    continue;
                case '\b':
                    builder.Append("\\b");
                    continue;
                case '\f':
                    builder.Append("\\f");
                    continue;
                case '\t':
                    builder.Append("\\t");
                    continue;
                case '\v':
                    builder.Append("\\v");
                    continue;
            }

            // Everything else a literal cannot carry as itself. Past the C0 range that is the
            // three line terminators recognised beyond \n and \r: left as themselves they end the
            // literal rather than sit in it
            if (ch < ' ' ||
                ch == '\u007f' ||
                IsLineTerminator(ch))
            {
                builder.Append("\\u");
                builder.Append(((int) ch).ToString("x4"));
                continue;
            }

            builder.Append(ch);
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// The line terminators a lexer recognises beyond <c>\n</c> and <c>\r</c>: next line, line
    /// separator and paragraph separator.
    /// <para>
    /// A C# literal cannot hold one as itself. In a regular literal it ends the literal (CS1010),
    /// and in a raw one it reads as a line break, so the line after it no longer starts with the
    /// whitespace the closing delimiter defines (CS8999). Only an escape carries one, and only a
    /// regular literal has escapes, which is why content holding one is written as a regular
    /// literal whatever else it holds.
    /// </para>
    /// </summary>
    public static bool IsLineTerminator(char ch) =>
        ch is (char) 0x85 or (char) 0x2028 or (char) 0x2029;

    /// <summary>
    /// True when the content holds a terminator <see cref="IsLineTerminator" /> describes, which is
    /// what sends content that would otherwise be written multi-line to a regular literal instead.
    /// </summary>
    public static bool HasLineTerminator(string content)
    {
        foreach (var ch in content)
        {
            if (IsLineTerminator(ch))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as a multi-line literal delimited by
    /// <paramref name="delimiter"/>. The result starts with the opening delimiter (no leading
    /// indent on the first line) and ends with the closing one (no trailing newline).
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="indent">Whitespace prefix for content lines and the closing delimiter.</param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    /// <param name="delimiter">The quote run that opens and closes the literal.</param>
    public static string RenderMultiLine(string content, string indent, string eol, string delimiter)
    {
        if (content.IndexOf('\r') != -1)
        {
            // Content is meant to arrive \n normalized. Be defensive: a stray \r would
            // otherwise be emitted into the literal as content, corrupting the snapshot
            content = SourceLanguage.NormalizeNewlines(content);
        }

        var builder = new StringBuilder();
        builder.Append(delimiter);
        builder.Append(eol);
        foreach (var line in content.Split('\n'))
        {
            if (line.Length > 0)
            {
                builder.Append(indent);
                builder.Append(line);
            }

            builder.Append(eol);
        }

        builder.Append(indent);
        builder.Append(delimiter);
        return builder.ToString();
    }

    public static int LongestQuoteRun(string content)
    {
        var longest = 0;
        var current = 0;
        foreach (var ch in content)
        {
            if (ch == '"')
            {
                current++;
                if (current > longest)
                {
                    longest = current;
                }
            }
            else
            {
                current = 0;
            }
        }

        return longest;
    }

    public static int QuoteRunLength(string text, int index)
    {
        var count = 0;
        while (index + count < text.Length &&
               text[index + count] == '"')
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Scans a multi-line literal opening at <paramref name="start"/> with a run of
    /// <paramref name="quotes"/>, and returns what it holds with the layout taken off.
    /// </summary>
    public static bool TryScanMultiLine(string text, int start, int quotes, out string? value, out int end)
    {
        value = null;
        end = start;
        var contentStart = start + quotes;
        // The closing delimiter is a run of quotes at least as long as the opening one. A run
        // inside the content is shorter than that, by the rule that chose the opening length
        var index = contentStart;
        while (true)
        {
            if (index >= text.Length)
            {
                return false;
            }

            if (text[index] != '"')
            {
                index++;
                continue;
            }

            var run = QuoteRunLength(text, index);
            if (run >= quotes)
            {
                break;
            }

            index += run;
        }

        end = index + quotes;
        var content = text.Substring(contentStart, index - contentStart);
        if (!content.Contains('\n'))
        {
            // Single line: content is verbatim, with no layout to take off
            value = content;
            return true;
        }

        return TryStripLayout(SourceLanguage.NormalizeNewlines(content), out value);
    }

    /// <summary>
    /// Takes the layout off a multi-line literal's content: the first line, which holds nothing
    /// but the break after the opening delimiter, the indentation the closing delimiter sits at,
    /// and the line that delimiter is on.
    /// <para>
    /// Returns false when the text is not in that shape - a content line less indented than the
    /// closing delimiter, or a first line with something on it - which is a literal nobody wrote
    /// to this convention and whose value is therefore whatever it says.
    /// </para>
    /// </summary>
    public static bool TryStripLayout(string text, [NotNullWhen(true)] out string? value)
    {
        value = null;
        var lines = text.Split('\n');
        if (lines.Length < 2 ||
            lines[0].Trim().Length > 0)
        {
            return false;
        }

        var closeIndent = lines[lines.Length - 1];
        if (closeIndent.Trim().Length > 0)
        {
            return false;
        }

        var builder = new StringBuilder();
        for (var index = 1; index < lines.Length - 1; index++)
        {
            if (index > 1)
            {
                builder.Append('\n');
            }

            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(closeIndent, StringComparison.Ordinal))
            {
                builder.Append(line, closeIndent.Length, line.Length - closeIndent.Length);
                continue;
            }

            if (line.Trim().Length == 0)
            {
                // Whitespace-only line shorter than the indent
                continue;
            }

            return false;
        }

        value = builder.ToString();
        return true;
    }

    /// <summary>
    /// Scans a verbatim literal, where the only escape is a doubled quote.
    /// </summary>
    public static bool TryScanVerbatim(string text, int start, out string? value, out int end)
    {
        value = null;
        end = start;
        var builder = new StringBuilder();
        var index = start;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                if (index + 1 < text.Length &&
                    text[index + 1] == '"')
                {
                    builder.Append('"');
                    index += 2;
                    continue;
                }

                value = builder.ToString();
                end = index + 1;
                return true;
            }

            builder.Append(ch);
            index++;
        }

        return false;
    }

    public static bool TryReadHex(string text, ref int index, int min, int max, out uint result)
    {
        result = 0;
        var count = 0;
        while (count < max &&
               index < text.Length &&
               Uri.IsHexDigit(text[index]))
        {
            result = (result << 4) + (uint) Uri.FromHex(text[index]);
            index++;
            count++;
        }

        return count >= min;
    }
}
